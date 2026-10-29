using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Additive NMM profile companion document; existing plugin loadorder.txt remains unchanged.</summary>
	internal sealed class NativeOrderProfileState
	{
		internal const string FileName = "native-load-order.json";
		internal const string DictionaryKey = "native-load-order";
		[JsonProperty(Required = Required.Always)] public int Version { get; set; } = 1;
		[JsonProperty(Required = Required.Always)] public string ModeId { get; set; }
		[JsonProperty(Required = Required.Always)] public Dictionary<string, LarianOrderPreference> Profiles { get; set; }

		/// <summary>Bounds and validates all names and preferences before a profile switch can begin.</summary>
		internal static NativeOrderProfileState Decode(byte[] bytes, string modeId)
		{
			if (bytes == null) return null;
			if (bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid native profile order state size.");
			JObject json;
			using (var text = new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
			using (var reader = new JsonTextReader(text) { MaxDepth = 32 })
			{
				json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
				if (reader.Read() || json.DescendantsAndSelf().OfType<JObject>().Any(item => item.Properties().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)))
					throw new InvalidDataException("Ambiguous native profile order document.");
			}
			NativeOrderProfileState state = json.ToObject<NativeOrderProfileState>(JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }));
			if (state.Version != 1 || state.ModeId != modeId || state.Profiles == null || state.Profiles.Count > 256 || state.Profiles.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Profiles.Count)
				throw new InvalidDataException("The native profile order state has an unsupported version or target.");
			foreach (var entry in state.Profiles)
			{
				StructuredXmlRecordRule.Segment(entry.Key);
				if (entry.Value == null) throw new InvalidDataException("A native profile preference is missing.");
				entry.Value.Encode();
			}
			return state;
		}
	}

	public sealed partial class NativeLoadOrderService
	{
		/// <summary>Captures current order policies for NMM profile save/clone/backup, without inspecting packages or mutating the game.</summary>
		internal static byte[] CaptureProfileState(IGameMode mode, IInstallLog log)
		{
			LoadOrderCapability capability = (mode as ILoadOrderCapabilityProvider)?.LoadOrder;
			if (capability == null) return null;
			string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), capability.Profiles.ProfileRoot);
			var state = new NativeOrderProfileState { ModeId = mode.ModeId, Profiles = new Dictionary<string, LarianOrderPreference>(StringComparer.OrdinalIgnoreCase) };
			if (Directory.Exists(root))
			{
				string[] directories = Directory.EnumerateDirectories(root).Take(257).ToArray();
				if (directories.Length > 256) throw new InvalidDataException("Too many native game profiles.");
				var store = new StructuredXmlRecordStore(capability.Profiles);
				foreach (string directory in directories)
				{
					string profile = Path.GetFileName(directory);
					if (capability.Profiles.ExcludedProfiles.Contains(profile, StringComparer.OrdinalIgnoreCase)) continue;
					string path = store.GetManifestPath(profile);
					if (!File.Exists(path)) continue;
					LarianOrderPreference preference = LarianOrderPreference.Decode(LarianOrderPreference.Read(log, profile));
					if (preference == null)
					{
						LarianLoadOrderDocument document = LarianLoadOrderDocument.Read(path);
						preference = new LarianOrderPreference { Source = "Manual", Order = document.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray(), Excluded = new string[0] };
					}
					state.Profiles.Add(profile, preference);
				}
			}
			byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state)); NativeOrderProfileState.Decode(bytes, mode.ModeId); return bytes;
		}

		/// <summary>Captures a rollback baseline from the live native target before profile deployment changes any mods.</summary>
		public byte[] CaptureProfileState() => WithAuthority(authority => CaptureProfileState(_mode, _services.ModManager.InstallationLog));

		/// <summary>Validates the selected companion before any profile install/uninstall work starts.</summary>
		public void ValidateProfileState(byte[] bytes) => NativeOrderProfileState.Decode(bytes, ModeId);

		/// <summary>Restores a profile policy after its files are deployed, through reviewed publication and the same recovery protocol.</summary>
		public void RestoreProfileState(byte[] bytes, CancellationToken cancellationToken)
		{
			NativeOrderProfileState state = NativeOrderProfileState.Decode(bytes, ModeId);
			if (state == null) return; // Legacy profiles keep their existing native behavior.
			foreach (var entry in state.Profiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				NativeLoadOrderDraft draft = CreateDraft(entry.Key, cancellationToken);
				foreach (string id in entry.Value.Excluded.Where(id => draft.IncludedIdentities.Contains(id))) draft.SetIncluded(id, false);
				foreach (string id in entry.Value.Order.Where(id => draft.AvailableModules.Any(module => module.Metadata.Identity == id))) draft.SetIncluded(id, true);
				int position = 0;
				foreach (string id in entry.Value.Order.Where(id => draft.IncludedIdentities.Contains(id))) draft.Move(id, position++);
				NativeLoadOrderReview review = Review(entry.Key, draft);
				review.Pending.Changes.Single(change => change.Key == LarianOrderPreference.Key(entry.Key)).After = entry.Value.Encode();
				try
				{
					NativeLoadOrderRecovery result = Save(review, true, cancellationToken);
					if (result.State != NativeLoadOrderRecoveryState.None) throw new InvalidOperationException(result.Diagnostic ?? "Native order recovery is required.");
				}
				catch
				{
					// Compensate only the journal created by this exact restore; unrelated/unknown recovery stays blocked.
					bool ours = WithAuthority(authority => { NativeOrderPending pending = LoadPending(entry.Key, authority); return pending != null && pending.Id == review.Pending.Id; });
					if (ours) Recover(entry.Key, NativeLoadOrderRecoveryAction.RestoreOriginal, true, CancellationToken.None);
					throw;
				}
			}
		}
	}
}
