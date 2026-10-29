using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ChinhDo.Transactions;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Mods;
using Nexus.Transactions;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Explicit recovery choices; neither startup nor inspection chooses a replacement file automatically.</summary>
	public enum NativeLoadOrderRecoveryAction { Acknowledge, RestoreOriginal, CompletePublication }

	/// <summary>Current recovery evidence, including disagreement between native history and the physical file.</summary>
	public enum NativeLoadOrderRecoveryState { None, OriginalIntact, PublishedIntact, Interrupted, Conflict }

	/// <summary>Failure-injection boundaries for maintainer-owned native publication/recovery fixtures.</summary>
	internal enum NativeLoadOrderPublicationStep { AfterJournal, AfterBackup, AfterReplacement, AfterCommit, BeforeAcknowledgement }

	/// <summary>Detached recovery evidence; a conflict never grants permission to overwrite an unknown file.</summary>
	public sealed class NativeLoadOrderRecovery
	{
		/// <summary>Captures one journal inspection without changing a game file or history.</summary>
		internal NativeLoadOrderRecovery(NativeLoadOrderRecoveryState state, string diagnostic, string backupPath)
		{ State = state; Diagnostic = diagnostic; BackupPath = backupPath; }
		public NativeLoadOrderRecoveryState State { get; }
		public string Diagnostic { get; }
		public string BackupPath { get; }
	}

	/// <summary>Review binds the physical draft to the current winning native values and their untouched shadow histories.</summary>
	public sealed class NativeLoadOrderReview
	{
		internal NativeOrderPending Pending;
		/// <summary>Captures the complete file preview and native reconciliation proposal.</summary>
		internal NativeLoadOrderReview(NativeLoadOrderPreview preview, NativeOrderPending pending) { Preview = preview; Pending = pending; }
		public NativeLoadOrderPreview Preview { get; }
		public int UpdatedNativeValues => Pending.Changes.Length;
	}

	/// <summary>Durable publication intent. This is recovery evidence, not a replacement ownership database.</summary>
	internal sealed class NativeOrderPending
	{
		[JsonIgnore] internal string JournalIdentity { get; set; }
		[JsonProperty(Required = Required.Always)] public int Version { get; set; } = 2;
		[JsonProperty(Required = Required.Always)] public string Id { get; set; }
		[JsonProperty(Required = Required.Always)] public string ModeId { get; set; }
		[JsonProperty(Required = Required.Always)] public string Target { get; set; }
		[JsonProperty(Required = Required.Always)] public string Profile { get; set; }
		[JsonProperty(Required = Required.Always)] public string InventoryIdentity { get; set; }
		[JsonProperty(Required = Required.Always)] public string BeforeHash { get; set; }
		[JsonProperty(Required = Required.Always)] public string AfterHash { get; set; }
		[JsonProperty(Required = Required.Always)] public byte[] Before { get; set; }
		[JsonProperty(Required = Required.Always)] public byte[] After { get; set; }
		[JsonProperty(Required = Required.Always)] public NativeOrderValueChange[] Changes { get; set; }
	}

	/// <summary>One winning owner update; prior and shadowed owner entries remain intact.</summary>
	internal sealed class NativeOrderValueChange
	{
		public bool BeforeMissing { get; set; }
		[JsonProperty(Required = Required.Always)] public string Key { get; set; }
		[JsonProperty(Required = Required.Always)] public string Owner { get; set; }
		[JsonProperty(Required = Required.Always)] public string ShadowIdentity { get; set; }
		[JsonProperty(Required = Required.AllowNull)] public byte[] Before { get; set; }
		[JsonProperty(Required = Required.AllowNull)] public byte[] After { get; set; }
	}

	/// <summary>Configured native LSX read/draft/save/recovery boundary under NMM's existing target reservation.</summary>
	public sealed partial class NativeLoadOrderService
	{
		internal const string PendingSuffix = ".nmm-larian-order.pending";
		private const int MaximumJournalBytes = 64 * 1024 * 1024;
		private readonly ServiceManager _services;
		private readonly GameStorageService _storage;
		private readonly IGameMode _mode;
		private readonly LoadOrderCapability _capability;
		private readonly string _profilesRoot;
		internal Action<NativeLoadOrderPublicationStep> PublicationCheckpoint { get; set; }

		/// <summary>Tests capability identity without probing files or starting the package helper.</summary>
		public static bool Supports(IGameMode mode) => (mode as ILoadOrderCapabilityProvider)?.LoadOrder?.Provider == LoadOrderCapability.LarianProviderId;

		/// <summary>Binds existing native services; no startup reads, repairs or publications occur.</summary>
		public NativeLoadOrderService(ServiceManager services, GameStorageService storage)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_storage = storage ?? throw new ArgumentNullException(nameof(storage));
			_mode = services.ModManager.GameMode;
			if (!Supports(_mode)) throw new ArgumentException("The mode does not declare the native module-settings capability.");
			_capability = ((ILoadOrderCapabilityProvider)_mode).LoadOrder;
			_profilesRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _capability.Profiles.ProfileRoot));
		}
		public string ModeId => _mode.ModeId;

		/// <summary>Lists existing eligible profiles without creating or modifying them; the caller chooses the target explicitly.</summary>
		public IReadOnlyList<string> ListProfiles()
		{
			RequireCurrentMode();
			try
			{
				FileAttributes attributes = File.GetAttributes(_profilesRoot);
				if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("The configured profile root is not a direct directory.");
			}
			catch (FileNotFoundException) { return Array.AsReadOnly(new string[0]); }
			catch (DirectoryNotFoundException) { return Array.AsReadOnly(new string[0]); }
			string[] profiles = Directory.EnumerateDirectories(_profilesRoot).Take(257).ToArray();
			if (profiles.Length > 256) throw new InvalidDataException("The profile count exceeds the supported bound.");
			return Array.AsReadOnly(profiles.Select(Path.GetFileName).Where(profile => !_capability.Profiles.ExcludedProfiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
				.Where(profile => File.Exists(Path.Combine(_profilesRoot, profile, _capability.Profiles.ManifestFile)))
				.OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase).ToArray());
		}

		/// <summary>Reads the selected existing native manifest; missing or invalid files remain unchanged.</summary>
		public LarianLoadOrderDocument Read(string profile) => LarianLoadOrderDocument.Read(Manifest(profile));

		/// <summary>Creates a detached draft using a fresh explicitly requested package scan.</summary>
		public NativeLoadOrderDraft CreateDraft(string profile, System.Threading.CancellationToken cancellationToken)
		{
			RequireNoPendingManifest(Manifest(profile));
			return new NativeLoadOrderDraft(Read(profile), LoadOrderProviderRegistry.Create(_mode).ReadInventory(cancellationToken));
		}

		/// <summary>Reviews native owner reconciliation under the same authority used later for publication.</summary>
		public NativeLoadOrderReview Review(string profile, NativeLoadOrderDraft draft) => Review(profile, draft, System.Threading.CancellationToken.None);

		/// <summary>Reviews an order while allowing cancellation of native reservation waits.</summary>
		public NativeLoadOrderReview Review(string profile, NativeLoadOrderDraft draft, System.Threading.CancellationToken cancellationToken)
		{
			if (draft == null) throw new ArgumentNullException(nameof(draft));
			NativeLoadOrderPreview preview = draft.Prepare();
			return WithAuthority(authority =>
			{
				string path = Manifest(profile); RequireNoPendingManifest(path);
				if (!SamePath(path, preview.Before.FilePath) || Read(profile).Sha256 != preview.Before.Sha256) throw new InvalidOperationException("The reviewed manifest changed. Refresh the draft.");
				NativeOrderPending pending = new NativeOrderPending
				{
					Id = Guid.NewGuid().ToString("N"), ModeId = ModeId, Target = authority.Target.Fingerprint, Profile = profile,
					InventoryIdentity = preview.InventoryIdentity, Before = preview.Before.Bytes, After = preview.After.Bytes,
					BeforeHash = preview.Before.Sha256, AfterHash = preview.After.Sha256,
					Changes = BuildChanges(profile, preview.After, LarianOrderPreference.FromReview(preview,
						LarianOrderPreference.Decode(LarianOrderPreference.Read(_services.ModManager.InstallationLog, profile))).Encode())
				};
				return new NativeLoadOrderReview(preview, pending);
			}, cancellationToken);
		}

		/// <summary>Saves an approved review, rejecting stale package, file, owner or shadow-history evidence.</summary>
		public NativeLoadOrderRecovery Save(NativeLoadOrderReview review, bool explicitlyApproved, System.Threading.CancellationToken cancellationToken)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			if (!explicitlyApproved) throw new InvalidOperationException("An approved native order review is required.");
			return WithAuthority(authority =>
			{
				NativeOrderPending pending = review.Pending;
				ValidatePending(pending, authority, pending.Profile);
				string path = Manifest(pending.Profile); RequireNoPendingManifest(path);
				if (Read(pending.Profile).Sha256 != pending.BeforeHash || !HistoryMatches(pending, false, false)) throw new InvalidOperationException("Native state changed since review.");
				RequireInventory(pending.InventoryIdentity, cancellationToken);
				if (pending.BeforeHash == pending.AfterHash && pending.Changes.All(change => !change.BeforeMissing && Equal(change.Before, change.After)))
					return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.None, "No order changes.", null);
				byte[] journal = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(pending));
				if (journal.Length > MaximumJournalBytes) throw new InvalidDataException("The recovery record exceeds its supported size.");
				WriteNewFlushed(path + PendingSuffix, journal);
				pending.JournalIdentity = LarianLoadOrderDocument.Hash(journal);
				PublicationCheckpoint?.Invoke(NativeLoadOrderPublicationStep.AfterJournal);
				// From here, any interruption remains visible until file and native histories are reconciled.
				WriteNewFlushed(Backup(path, pending), pending.Before);
				PublicationCheckpoint?.Invoke(NativeLoadOrderPublicationStep.AfterBackup);
				Apply(path, pending, true);
				return Finish(path, pending);
			}, cancellationToken);
		}

		/// <summary>Inspects physical and native recovery evidence under freshly reloaded authority; never repairs automatically.</summary>
		public NativeLoadOrderRecovery InspectRecovery(string profile) => InspectRecovery(profile, System.Threading.CancellationToken.None);

		/// <summary>Inspects recovery with a cancellable native reservation wait.</summary>
		public NativeLoadOrderRecovery InspectRecovery(string profile, System.Threading.CancellationToken cancellationToken) => WithAuthority(authority =>
		{
			try { return Inspect(Manifest(profile), LoadPending(profile, authority)); }
			catch (Exception error) when (IsRecoveryError(error)) { return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.Conflict, error.Message, null); }
		}, cancellationToken);

		/// <summary>Explicitly acknowledges aligned evidence or restores/completes only recognized pre/postimages.</summary>
		public NativeLoadOrderRecovery Recover(string profile, NativeLoadOrderRecoveryAction action, bool explicitlyApproved,
			System.Threading.CancellationToken cancellationToken)
		{
			if (!explicitlyApproved || !Enum.IsDefined(typeof(NativeLoadOrderRecoveryAction), action)) throw new InvalidOperationException("An explicit recovery choice is required.");
			return WithAuthority(authority =>
			{
				string path = Manifest(profile); NativeOrderPending pending;
				try { pending = LoadPending(profile, authority); }
				catch (Exception error) when (IsRecoveryError(error)) { return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.Conflict, error.Message, null); }
				NativeLoadOrderRecovery evidence = Inspect(path, pending);
				if (evidence.State == NativeLoadOrderRecoveryState.None || evidence.State == NativeLoadOrderRecoveryState.Conflict) return evidence;
				if (action == NativeLoadOrderRecoveryAction.Acknowledge)
				{
					if (evidence.State == NativeLoadOrderRecoveryState.Interrupted) return evidence;
				}
				else
				{
					bool publish = action == NativeLoadOrderRecoveryAction.CompletePublication;
					if (publish) RequireInventory(pending.InventoryIdentity, cancellationToken);
					Apply(path, pending, publish);
				}
				return Finish(path, pending);
			}, cancellationToken);
		}

		/// <summary>Stops native installation/removal callbacks from consuming a manifest with unresolved manual publication.</summary>
		internal static void RequireNoPendingManifest(string path)
		{
			try { File.GetAttributes(path + PendingSuffix); }
			catch (FileNotFoundException) { return; }
			catch (DirectoryNotFoundException) { return; }
			throw new InvalidDataException("The native load-order publication needs recovery before another manifest edit.");
		}

		/// <summary>Captures current winning values and derives their new payloads without replacing original/shadowed owners.</summary>
		private NativeOrderValueChange[] BuildChanges(string profile, LarianLoadOrderDocument after, byte[] preference)
		{
			InstallLogReadSnapshot snapshot = _services.ModManager.InstallationLog.GetCommittedStateSnapshot();
			RequireNoPayloadOwnership(Manifest(profile), snapshot);
			var changes = new List<NativeOrderValueChange>();
			foreach (InstallLogReadGameValue value in snapshot.GameValues.Where(value => IsProfileKey(value.Key, profile)))
			{
				string owner = _services.ModManager.InstallationLog.GetCurrentGameSpecificValueEditOwnerKey(value.Key);
				if (owner == null || owner == snapshot.OriginalValuesKey && value.Key != LarianOrderPreference.Key(profile)) continue;
				InstallLogReadBinaryValue winner = value.Values.Single(item => item.OwnerKey == owner);
				changes.Add(new NativeOrderValueChange { Key = value.Key, Owner = owner, Before = winner.Value,
					After = value.Key == LarianOrderPreference.Key(profile) ? preference : Payload(value.Key, profile, after), ShadowIdentity = Shadow(value, owner) });
			}
			if (!changes.Any(change => change.Key == LarianOrderPreference.Key(profile)))
				changes.Add(new NativeOrderValueChange { Key = LarianOrderPreference.Key(profile), Owner = snapshot.OriginalValuesKey, BeforeMissing = true,
					After = preference, ShadowIdentity = LarianLoadOrderDocument.Hash(Encoding.UTF8.GetBytes("[]")) });
			return changes.OrderBy(change => change.Key, StringComparer.Ordinal).ToArray();
		}

		/// <summary>Updates only captured winning values in the same native transaction as the complete file write.</summary>
		private void Apply(string path, NativeOrderPending pending, bool publish)
		{
			RequireSameJournal(path, pending);
			NativeLoadOrderRecovery evidence = Inspect(path, pending);
			if (evidence.State == NativeLoadOrderRecoveryState.Conflict) throw new InvalidOperationException(evidence.Diagnostic);
			string expectedFileHash = LarianLoadOrderDocument.Hash(LarianLoadOrderDocument.ReadBytes(path, LarianLoadOrderDocument.MaximumBytes));
			RequireNoPayloadOwnership(path, _services.ModManager.InstallationLog.GetCommittedStateSnapshot());
			if (!File.Exists(Backup(path, pending))) WriteNewFlushed(Backup(path, pending), pending.Before);
			byte[] desired = publish ? pending.After : pending.Before;
			string temporary = path + ".nmm-larian-order." + pending.Id + (publish ? ".publish.tmp" : ".restore.tmp");
			if (File.Exists(temporary))
			{
				if (!Equal(LarianLoadOrderDocument.ReadBytes(temporary, LarianLoadOrderDocument.MaximumBytes), desired)) throw new InvalidDataException("The retained stage has unknown contents.");
			}
			else WriteNewFlushed(temporary, desired);
			using (var transaction = new TransactionScope())
			{
				RequireSameJournal(path, pending);
				new TxFileManager().Snapshot(path);
				foreach (NativeOrderValueChange change in pending.Changes)
				{
					IMod owner = _services.ModManager.InstallationLog.GetCurrentGameSpecificValueEditOwner(change.Key);
					if (change.BeforeMissing)
					{
						if (publish)
						{
							if (owner == null) _services.ModManager.InstallationLog.LogOriginalGameSpecificValue(change.Key, change.After);
							else _services.ModManager.InstallationLog.ReplaceGameSpecificValueEdit(owner, change.Key, change.After);
						}
						else if (owner != null) _services.ModManager.InstallationLog.RemoveGameSpecificValueEdit(owner, change.Key);
						continue;
					}
					if (owner == null || _services.ModManager.InstallationLog.GetModKey(owner) != change.Owner) throw new InvalidOperationException("A native winning owner changed.");
					_services.ModManager.InstallationLog.ReplaceGameSpecificValueEdit(owner, change.Key, publish ? change.After : change.Before);
				}
				if (LarianLoadOrderDocument.Hash(LarianLoadOrderDocument.ReadBytes(path, LarianLoadOrderDocument.MaximumBytes)) != expectedFileHash)
					throw new InvalidOperationException("The native file changed during publication staging.");
				File.Replace(temporary, path, null);
				PublicationCheckpoint?.Invoke(NativeLoadOrderPublicationStep.AfterReplacement);
				transaction.Complete();
			}
			PublicationCheckpoint?.Invoke(NativeLoadOrderPublicationStep.AfterCommit);
		}

		/// <summary>Reports aligned pre/postimages and mixed interrupted commits without overwriting external edits.</summary>
		private NativeLoadOrderRecovery Inspect(string path, NativeOrderPending pending)
		{
			if (pending == null) return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.None, null, null);
			string backup = Backup(path, pending);
			if (Directory.Exists(backup)) return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.Conflict, "The original backup path became a directory.", backup);
			if (File.Exists(backup) && LarianLoadOrderDocument.Hash(LarianLoadOrderDocument.ReadBytes(backup, LarianLoadOrderDocument.MaximumBytes)) != pending.BeforeHash)
				return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.Conflict, "The original backup changed; review it before recovery.", backup);
			string hash = LarianLoadOrderDocument.Hash(LarianLoadOrderDocument.ReadBytes(path, LarianLoadOrderDocument.MaximumBytes));
			bool before = hash == pending.BeforeHash, after = hash == pending.AfterHash;
			bool historyBefore = HistoryMatches(pending, false, false), historyAfter = HistoryMatches(pending, true, false);
			NativeLoadOrderRecoveryState state = !before && !after || !HistoryMatches(pending, false, true) ? NativeLoadOrderRecoveryState.Conflict :
				before && historyBefore ? NativeLoadOrderRecoveryState.OriginalIntact : after && historyAfter ? NativeLoadOrderRecoveryState.PublishedIntact : NativeLoadOrderRecoveryState.Interrupted;
			return new NativeLoadOrderRecovery(state, state == NativeLoadOrderRecoveryState.Conflict ? "Recovery conflicts with current file or ownership evidence; nothing was changed." : null, Backup(path, pending));
		}

		/// <summary>Checks current owners, every captured shadow entry and exact values, allowing mixed pre/post values only for recovery.</summary>
		private bool HistoryMatches(NativeOrderPending pending, bool after, bool either)
		{
			InstallLogReadSnapshot snapshot = _services.ModManager.InstallationLog.GetCommittedStateSnapshot();
			string[] currentKeys = snapshot.GameValues.Where(value => IsProfileKey(value.Key, pending.Profile))
				.Where(value => { string owner = _services.ModManager.InstallationLog.GetCurrentGameSpecificValueEditOwnerKey(value.Key); return owner != null && (owner != snapshot.OriginalValuesKey || value.Key == LarianOrderPreference.Key(pending.Profile)); })
				.Select(value => value.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray();
			if (currentKeys.Except(pending.Changes.Select(change => change.Key), StringComparer.Ordinal).Any()) return false;
			foreach (NativeOrderValueChange change in pending.Changes)
			{
				InstallLogReadGameValue record = snapshot.GameValues.SingleOrDefault(value => value.Key == change.Key);
				if (change.BeforeMissing && (record == null || record.Values.Count == 0))
				{
					if (after && !either) return false;
					continue;
				}
				if (record == null || _services.ModManager.InstallationLog.GetCurrentGameSpecificValueEditOwnerKey(change.Key) != change.Owner || Shadow(record, change.Owner) != change.ShadowIdentity) return false;
				byte[] currentValue = record.Values.Single(item => item.OwnerKey == change.Owner).Value;
				if (!Equal(currentValue, after ? change.After : change.Before) && !(either && Equal(currentValue, change.After))) return false;
			}
			return true;
		}

		/// <summary>Finalizes only aligned file/history states, retaining the exact original backup.</summary>
		private NativeLoadOrderRecovery Finish(string path, NativeOrderPending pending)
		{
			NativeLoadOrderRecovery evidence = Inspect(path, pending);
			if (evidence.State != NativeLoadOrderRecoveryState.OriginalIntact && evidence.State != NativeLoadOrderRecoveryState.PublishedIntact) return evidence;
			foreach (string suffix in new[] { ".publish.tmp", ".restore.tmp" })
			{
				string stage = path + ".nmm-larian-order." + pending.Id + suffix;
				if (Directory.Exists(stage)) return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.Conflict, "A retained stage path became a directory.", Backup(path, pending));
				if (!File.Exists(stage)) continue;
				string hash = LarianLoadOrderDocument.Hash(LarianLoadOrderDocument.ReadBytes(stage, LarianLoadOrderDocument.MaximumBytes));
				if (hash != pending.BeforeHash && hash != pending.AfterHash) return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.Conflict, "A retained stage has unknown contents.", Backup(path, pending));
				File.Delete(stage);
			}
			PublicationCheckpoint?.Invoke(NativeLoadOrderPublicationStep.BeforeAcknowledgement);
			RequireSameJournal(path, pending);
			File.Delete(path + PendingSuffix);
			return new NativeLoadOrderRecovery(NativeLoadOrderRecoveryState.None, "Native file and ownership histories agree; the original backup was retained.", Backup(path, pending));
		}

		/// <summary>Loads only a bounded recognized journal associated with the current profile and native authority.</summary>
		private NativeOrderPending LoadPending(string profile, CollectionTargetAuthority authority)
		{
			string path = Manifest(profile) + PendingSuffix;
			byte[] bytes;
			try { bytes = LarianLoadOrderDocument.ReadBytes(path, MaximumJournalBytes); }
			catch (FileNotFoundException) { return null; }
			catch (DirectoryNotFoundException) { return null; }
			NativeOrderPending pending;
			using (var text = new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
			using (var reader = new JsonTextReader(text) { MaxDepth = 32 })
			{
				Newtonsoft.Json.Linq.JObject json = Newtonsoft.Json.Linq.JObject.Load(reader,
					new Newtonsoft.Json.Linq.JsonLoadSettings { DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error });
				if (json.DescendantsAndSelf().OfType<Newtonsoft.Json.Linq.JObject>().Any(item => item.Properties().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)))
					throw new InvalidDataException("Ambiguous recovery record fields.");
				pending = json.ToObject<NativeOrderPending>(JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }));
				if (reader.Read()) throw new InvalidDataException("Trailing recovery record data.");
			}
			ValidatePending(pending, authority, profile);
			pending.JournalIdentity = LarianLoadOrderDocument.Hash(bytes);
			return pending;
		}

		/// <summary>Validates journal scope and derived after-values before any recovery operation can mutate native history.</summary>
		private void ValidatePending(NativeOrderPending pending, CollectionTargetAuthority authority, string profile)
		{
			Guid id;
			if (pending == null || (pending.Version != 1 && pending.Version != 2) || !Guid.TryParseExact(pending.Id, "N", out id) || pending.ModeId != ModeId || !String.Equals(pending.Profile, profile, StringComparison.OrdinalIgnoreCase) ||
				pending.Target != authority.Target.Fingerprint || pending.Before == null || pending.After == null ||
				pending.Before.Length > LarianLoadOrderDocument.MaximumBytes || pending.After.Length > LarianLoadOrderDocument.MaximumBytes ||
				LarianLoadOrderDocument.Hash(pending.Before) != pending.BeforeHash || LarianLoadOrderDocument.Hash(pending.After) != pending.AfterHash ||
				pending.Changes == null || pending.Changes.Length > 10001 || pending.Changes.Any(change => change == null) ||
				pending.Changes.Select(change => change.Key).Distinct(StringComparer.Ordinal).Count() != pending.Changes.Length)
				throw new InvalidDataException("Unrecognized or inconsistent native order recovery record.");
			var before = new LarianLoadOrderDocument(Manifest(profile), pending.Before);
			var after = new LarianLoadOrderDocument(Manifest(profile), pending.After);
			foreach (NativeOrderValueChange change in pending.Changes)
			{
				bool preference = change.Key == LarianOrderPreference.Key(profile);
				if (!IsProfileKey(change.Key, profile) || String.IsNullOrEmpty(change.Owner) || String.IsNullOrEmpty(change.ShadowIdentity) ||
					change.BeforeMissing && (!preference || change.Before != null || change.Owner != _services.ModManager.InstallationLog.OriginalValuesKey) ||
					!preference && !Equal(change.After, Payload(change.Key, profile, after)))
					throw new InvalidDataException("Recovery contains an unrelated or invalid native value update.");
				if (preference)
				{
					LarianOrderPreference desired = LarianOrderPreference.Decode(change.After);
					string[] active = after.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray();
					if (desired == null || desired.Excluded.Intersect(active).Any() ||
						!desired.Order.Where(active.Contains).Concat(active.Except(desired.Order)).SequenceEqual(active)) throw new InvalidDataException("Preference and native order differ.");
					LarianOrderPreference.Decode(change.Before);
				}
			}
		}

		/// <summary>Derives the established compact record/aggregate payload format from the native document.</summary>
		private byte[] Payload(string key, string profile, LarianLoadOrderDocument document)
		{
			string prefix = _capability.Profiles.KeyPrefix + profile + "|";
			if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				XmlElement record;
				return document.Records.TryGetValue(LarianLoadOrderDocument.Identity(key.Substring(prefix.Length)), out record) ? Encoding.UTF8.GetBytes(record.OuterXml) : null;
			}
			var payload = new XmlDocument(); payload.AppendChild(payload.CreateElement("bg3-order"));
			payload.DocumentElement.AppendChild(payload.ImportNode(document.Mods, true));
			if (document.LegacyOrder != null) payload.DocumentElement.AppendChild(payload.ImportNode(document.LegacyOrder, true));
			return Encoding.UTF8.GetBytes(payload.OuterXml);
		}

		/// <summary>Recognizes only existing configured module keys and the retained Collection aggregate key for this profile.</summary>
		private bool IsProfileKey(string key, string profile) => key != null &&
			(key == LarianOrderPreference.Key(profile) || key.StartsWith(_capability.Profiles.KeyPrefix, StringComparison.Ordinal) && key.Substring(_capability.Profiles.KeyPrefix.Length).StartsWith(profile + "|", StringComparison.OrdinalIgnoreCase) ||
			key.StartsWith(BaldursGate3CollectionRecordStore.Prefix, StringComparison.Ordinal) && String.Equals(key.Substring(BaldursGate3CollectionRecordStore.Prefix.Length), profile + "|" + BaldursGate3CollectionRecordStore.Identity, StringComparison.OrdinalIgnoreCase));

		/// <summary>Preserves every shadowed value and its order across manual publication.</summary>
		private static string Shadow(InstallLogReadGameValue value, string winner) => LarianLoadOrderDocument.Hash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value.Values.Where(item => item.OwnerKey != winner))));

		/// <summary>Performs explicit mutations only under the current native target's recovered cross-process reservation.</summary>
		private T WithAuthority<T>(Func<CollectionTargetAuthority, T> work, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			RequireCurrentMode();
			if (Transaction.Current != null) throw new InvalidOperationException("Native order operations require their own transaction boundary.");
			GameStoragePathSet paths = _storage.FromGameMode(_mode);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_storage).Resolve(paths);
			using (CollectionTargetMutationLease lease = CollectionTargetMutationLeaseManager.Shared.Acquire(authority, cancellationToken))
			{
				new CollectionTargetOwnershipAuthorityValidator(_storage, _services).ValidateAndReload(lease, authority, paths);
				cancellationToken.ThrowIfCancellationRequested();
				RequireCurrentMode(); return work(authority);
			}
		}

		/// <summary>Rechecks the same capability without caching stale installation logs across reloads.</summary>
		private void RequireCurrentMode()
		{
			if (!ReferenceEquals(_mode, _services.ModManager.GameMode) || !ReferenceEquals(_capability, (_mode as ILoadOrderCapabilityProvider)?.LoadOrder))
				throw new InvalidOperationException("The current load-order capability changed.");
		}

		/// <summary>Resolves one existing configured profile, rejecting excluded names and redirected profile descendants.</summary>
		private string Manifest(string profile)
		{
			RequireCurrentMode(); profile = StructuredXmlRecordRule.Segment(profile);
			if (_capability.Profiles.ExcludedProfiles.Contains(profile, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("The profile is excluded from native editing.");
			string path = Path.Combine(_profilesRoot, profile, _capability.Profiles.ManifestFile);
			for (string current = path; current != null; current = Path.GetDirectoryName(current))
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked native order paths cannot be published.");
				if (SamePath(current, _profilesRoot)) break;
			}
			return path;
		}

		/// <summary>Re-inspects package metadata, not just timestamps, before approving a stale-sensitive publication.</summary>
		private void RequireInventory(string identity, System.Threading.CancellationToken cancellationToken)
		{
			LoadOrderInventory inventory = LoadOrderProviderRegistry.Create(_mode).ReadInventory(cancellationToken);
			if (!NativeLoadOrderDraft.CanEditInventory(inventory) || NativeLoadOrderDraft.InventoryIdentity(inventory) != identity)
				throw new InvalidOperationException("The inspected package inventory changed. Refresh the draft.");
		}

		/// <summary>Creates and flushes immutable recovery evidence before a native transaction can change the game.</summary>
		private static void WriteNewFlushed(string path, byte[] bytes)
		{
			using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
		}
		/// <summary>Uses a fixed adjacent original-backup path outside the package directory.</summary>
		private static string Backup(string path, NativeOrderPending pending) => path + ".nmm-larian-order." + pending.Id + ".backup";
		/// <summary>Compares nullable native values without inventing an empty record for an absent module.</summary>
		private static bool Equal(byte[] first, byte[] second) => first == null ? second == null : second != null && first.SequenceEqual(second);

		/// <summary>Does not bypass native full-file/link/INI ownership when a manifest was installed as ordinary payload.</summary>
		private void RequireNoPayloadOwnership(string path, InstallLogReadSnapshot snapshot)
		{
			Func<ModDeploymentTarget, bool> matches = target => target != null &&
				String.Equals(Path.GetFileName(target.RelativePath), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) &&
				SamePath(ModDeploymentTargetResolver.GetPhysicalPath(_mode, target), path);
			if (snapshot.Files.Any(file => matches(file.Target) && file.OwnerKeys.Any(owner => owner != snapshot.OriginalValuesKey)) ||
				snapshot.DeploymentTargets.Any(file => matches(file.Target) && file.OwnerKeys.Any(owner => owner != snapshot.OriginalValuesKey)) ||
				_services.ModManager.VirtualModActivator.GetReadSnapshot().Links.Any(link => matches(link.Target)) ||
				snapshot.IniEdits.Any(edit => String.Equals(Path.GetFileName(edit.File), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) && edit.Values.Any(value => value.OwnerKey != snapshot.OriginalValuesKey)))
				throw new InvalidOperationException("The native manifest is installed or edited as payload. Resolve its existing ownership before standalone order publication.");
		}

		/// <summary>Classifies malformed or inaccessible recovery evidence without hiding programming failures.</summary>
		private static bool IsRecoveryError(Exception error) => error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException || error is JsonException || error is XmlException || error is ArgumentException;

		/// <summary>Does not mutate history or delete recovery evidence if the exact pending record was replaced externally.</summary>
		private static void RequireSameJournal(string path, NativeOrderPending pending)
		{
			if (LarianLoadOrderDocument.Hash(LarianLoadOrderDocument.ReadBytes(path + PendingSuffix, MaximumJournalBytes)) != pending.JournalIdentity)
				throw new InvalidDataException("The pending recovery record changed during the operation.");
		}
		/// <summary>Compares normalized Windows paths without changing persisted settings.</summary>
		private static bool SamePath(string first, string second) => String.Equals(Path.GetFullPath(first).TrimEnd('\\', '/'), Path.GetFullPath(second).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
	}
}
