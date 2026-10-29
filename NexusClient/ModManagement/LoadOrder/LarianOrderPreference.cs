using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Optional live native-value reader, including an enclosing transaction's buffered changes.</summary>
	public interface ICurrentGameSpecificValueReader
	{
		/// <summary>Returns the current native owner value, including buffered changes, without exposing mutable history bytes.</summary>
		bool TryReadCurrentGameSpecificValue(string key, out byte[] value);
	}

	/// <summary>Optional transaction-aware access to a specific owner's value, including shadowed history.</summary>
	public interface IOwnedGameSpecificValueReader
	{
		/// <summary>Returns detached bytes for the specified owner, or false when its edit is absent.</summary>
		bool TryReadOwnedGameSpecificValue(IMod owner, string key, out byte[] value);
	}

	/// <summary>Versioned inclusion/order preferences stored in the existing native value owner stack.</summary>
	public sealed class LarianOrderPreference
	{
		internal const string Prefix = "larian-order-preference-v1|";
		[JsonProperty(Required = Required.Always)] public int Version { get; set; } = 1;
		[JsonProperty(Required = Required.Always)] public string Source { get; set; }
		[JsonProperty(Required = Required.Always)] public string[] Order { get; set; }
		[JsonProperty(Required = Required.Always)] public string[] Excluded { get; set; }
		public string[] Managed { get; set; } = new string[0];

		/// <summary>Builds a stable profile-specific native key without introducing another ownership database.</summary>
		internal static string Key(string profile) => Prefix + StructuredXmlRecordRule.Segment(profile) + "|state";

		/// <summary>Parses only this bounded native namespace.</summary>
		internal static bool TryProfile(string key, out string profile)
		{
			profile = null;
			if (key == null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
			string[] parts = key.Substring(Prefix.Length).Split('|');
			if (parts.Length != 2 || parts[1] != "state") throw new InvalidDataException("Malformed native order preference key.");
			profile = StructuredXmlRecordRule.Segment(parts[0]); return true;
		}

		/// <summary>Decodes bounded, duplicate-free preference data while retaining absent legacy state.</summary>
		internal static LarianOrderPreference Decode(byte[] bytes)
		{
			if (bytes == null) return null;
			if (bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("Invalid native order preference size.");
			JObject json;
			using (var text = new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
			using (var reader = new JsonTextReader(text) { MaxDepth = 16 })
			{
				json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
				if (reader.Read() || json.Properties().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)) throw new InvalidDataException("Ambiguous preference document.");
			}
			LarianOrderPreference preference = json.ToObject<LarianOrderPreference>(JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }));
			if (preference.Version != 1 || (preference.Source != "Manual" && preference.Source != "Curator") || preference.Order == null || preference.Excluded == null ||
				preference.Order.Length > 10000 || preference.Excluded.Length > 10000) throw new InvalidDataException("Unsupported native order preference.");
			preference.Order = preference.Order.Select(LarianLoadOrderDocument.Identity).ToArray();
			preference.Excluded = preference.Excluded.Select(LarianLoadOrderDocument.Identity).ToArray();
			if (preference.Managed == null || preference.Managed.Length > 10000) throw new InvalidDataException("Invalid curator module membership.");
			preference.Managed = preference.Managed.Select(LarianLoadOrderDocument.Identity).ToArray();
			if (preference.Order.Distinct().Count() != preference.Order.Length || preference.Excluded.Distinct().Count() != preference.Excluded.Length || preference.Managed.Distinct().Count() != preference.Managed.Length || preference.Order.Intersect(preference.Excluded).Any())
				throw new InvalidDataException("Duplicate or contradictory native order preferences.");
			return preference;
		}

		/// <summary>Produces exact native owner-value bytes after validating the complete preference.</summary>
		internal byte[] Encode()
		{
			byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this)); Decode(bytes); return bytes;
		}

		/// <summary>Persists explicit inclusion choices while retaining temporarily absent module positions and curator membership.</summary>
		internal static LarianOrderPreference FromReview(NativeLoadOrderPreview preview, LarianOrderPreference previous)
		{
			string[] included = preview.After.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray();
			return new LarianOrderPreference
			{
				Source = "Manual", Managed = previous?.Managed ?? new string[0],
				Order = included.Concat((previous?.Order ?? new string[0]).Where(id => !preview.KnownIdentities.Contains(id) && !included.Contains(id))).ToArray(),
				Excluded = (previous?.Excluded ?? new string[0]).Union(preview.KnownIdentities.Except(included)).Except(included).ToArray()
			};
		}

		/// <summary>Reads preferences through the active native transaction, never a stale committed-only snapshot.</summary>
		internal static byte[] Read(IInstallLog log, string profile)
		{
			ICurrentGameSpecificValueReader reader = log as ICurrentGameSpecificValueReader;
			if (reader == null) throw new NotSupportedException("The native log does not expose transaction-aware game values.");
			byte[] value; reader.TryReadCurrentGameSpecificValue(Key(profile), out value); Decode(value); return value;
		}

		/// <summary>Reapplies inclusion choices; saved positions are restored only for an explicitly requested ordering operation.</summary>
		internal static byte[] Apply(byte[] original, string path, LarianOrderPreference preference, bool applyOrder = false, IEnumerable<string> inclusionScope = null)
		{
			if (preference == null) return original;
			var observed = new LarianLoadOrderDocument(path, original);
			var inventory = new LoadOrderInventory(Path.GetDirectoryName(path), new LoadOrderPackageObservation[0], true, new LoadOrderDiagnostic[0]);
			var draft = new NativeLoadOrderDraft(observed, inventory);
			var affected = new HashSet<string>(inclusionScope ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			foreach (string id in preference.Excluded.Where(id => (applyOrder || affected.Contains(id)) && observed.Entries.Any(entry => !entry.IsProtected && entry.Identity == id))) draft.SetIncluded(id, false);
			if (applyOrder)
			{
				int position = 0;
				foreach (string id in preference.Order.Where(id => draft.IncludedIdentities.Contains(id))) draft.Move(id, position++);
			}
			return draft.Prepare().After.Bytes;
		}

		/// <summary>Keeps surviving modules in their pre-operation sequence; newly introduced/restored entries append stably.</summary>
		internal static byte[] PreserveRelativeOrder(byte[] current, string path, IEnumerable<string> previousOrder)
		{
			if (previousOrder == null) return current;
			var document = new LarianLoadOrderDocument(path, current);
			var inventory = new LoadOrderInventory(Path.GetDirectoryName(path), new LoadOrderPackageObservation[0], true, new LoadOrderDiagnostic[0]);
			var draft = new NativeLoadOrderDraft(document, inventory);
			int position = 0;
			foreach (string id in previousOrder.Where(id => draft.IncludedIdentities.Contains(id))) draft.Move(id, position++);
			return draft.Prepare().After.Bytes;
		}
	}
}
