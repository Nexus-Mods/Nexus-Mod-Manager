using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Recognized locations of a durable cleanup batch; mixed locations can result from an interruption.</summary>
	public enum LoadOrderFolderRecoveryState { OriginalIntact, RetainedIntact, Mixed, Conflict }

	/// <summary>Read-only recovery evidence for reviewing retained content before choosing restore or completion.</summary>
	public sealed class LoadOrderFolderRecovery
	{
		/// <summary>Exposes the intent and current evidence without moving content.</summary>
		internal LoadOrderFolderRecovery(FolderRepairJournal journal, string directory, LoadOrderFolderRecoveryState state, string diagnostic)
		{ RecoveryId = journal.Id; Directory = directory; State = state; Diagnostic = diagnostic; Items = Array.AsReadOnly(journal.Items); }
		public string RecoveryId { get; }
		public string Directory { get; }
		public LoadOrderFolderRecoveryState State { get; }
		public string Diagnostic { get; }
		public IReadOnlyList<LoadOrderFolderRepairItem> Items { get; }
	}

	/// <summary>Preserves the cleanup recovery location when a batch stops before all moves have completed.</summary>
	public sealed class LoadOrderFolderRepairException : IOException
	{
		/// <summary>Reports recoverable partial cleanup rather than hiding already retained content.</summary>
		internal LoadOrderFolderRepairException(string directory, Exception inner) : base("Folder cleanup stopped. Inspect the retained recovery record before restoring content or completing cleanup.", inner)
		{ RetainedContentDirectory = directory; }
		public string RetainedContentDirectory { get; }
	}

	/// <summary>One bounded unowned item that can be retained outside the package folder, without flattening or deleting it.</summary>
	public sealed class LoadOrderFolderRepairItem
	{
		[JsonProperty(Required = Required.Always)] public string Name { get; internal set; }
		[JsonProperty(Required = Required.Always)] public bool IsDirectory { get; internal set; }
		[JsonProperty(Required = Required.Always)] public string ContentIdentity { get; internal set; }
	}

	/// <summary>A stale-sensitive cleanup preview. Managed, linked, oversized and package-containing directories require manual resolution.</summary>
	public sealed class LoadOrderFolderRepairReview
	{
		internal readonly FolderRepairJournal Journal;
		/// <summary>Captures the exact eligible items and explains every item that cannot safely be moved.</summary>
		internal LoadOrderFolderRepairReview(FolderRepairJournal journal, IEnumerable<LoadOrderIssue> issues)
		{ Journal = journal; Issues = Array.AsReadOnly(issues.ToArray()); }
		public string RecoveryId => Journal.Id;
		public IReadOnlyList<LoadOrderFolderRepairItem> Items => Array.AsReadOnly(Journal.Items);
		public IReadOnlyList<LoadOrderIssue> Issues { get; }
	}

	/// <summary>Durable move intent. Source and retained identities make interrupted moves explicitly recoverable.</summary>
	internal sealed class FolderRepairJournal
	{
		[JsonProperty(Required = Required.Always)] public int Version { get; set; } = 1;
		[JsonProperty(Required = Required.Always)] public string Id { get; set; }
		[JsonProperty(Required = Required.Always)] public string ModeId { get; set; }
		[JsonProperty(Required = Required.Always)] public string Target { get; set; }
		[JsonProperty(Required = Required.Always)] public string PackageRoot { get; set; }
		[JsonProperty(Required = Required.Always)] public string Profile { get; set; }
		[JsonProperty(Required = Required.Always)] public string ManifestIdentity { get; set; }
		[JsonProperty(Required = Required.Always)] public string InventoryIdentity { get; set; }
		[JsonProperty(Required = Required.Always)] public LoadOrderFolderRepairItem[] Items { get; set; }
	}

	public sealed partial class NativeLoadOrderService
	{
		private const string FolderRecoveryDirectory = ".nmm-load-order-recovery";
		private const int MaximumFolderJournalBytes = 4 * 1024 * 1024;

		/// <summary>Reviews unowned unexpected content under native authority; an ownership ledger is never bypassed by a raw move.</summary>
		public LoadOrderFolderRepairReview ReviewFolderRepairs(string profile, LoadOrderAnalysis analysis, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (analysis == null) throw new ArgumentNullException(nameof(analysis));
			return WithAuthority(authority =>
			{
				RequireNoPendingManifest(Manifest(profile));
				string root = ModDeploymentTargetResolver.GetPhysicalRootPath(_mode, ModDeploymentRoot.Data);
				if (!SamePath(root, analysis.Inventory.PackageRoot) || !SamePath(Manifest(profile), analysis.Document.FilePath) || Read(profile).Sha256 != analysis.Document.Sha256)
					throw new InvalidOperationException("The cleanup target changed after diagnosis.");
				var items = new List<LoadOrderFolderRepairItem>(); var issues = new List<LoadOrderIssue>();
				foreach (LoadOrderDiagnostic issue in analysis.Inventory.Diagnostics.Where(issue => issue.Kind == LoadOrderDiagnosticKind.UnexpectedDirectory || issue.Kind == LoadOrderDiagnosticKind.UnexpectedFile))
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						if (items.Count >= 128) throw new InvalidDataException("At most 128 unexpected items can be retained in one repair.");
						string name = StructuredXmlRecordRule.Segment(Path.GetFileName(issue.Path));
						if (String.Equals(name, "repair.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The recovery metadata name requires manual handling.");
						if (!SamePath(Path.Combine(root, name), issue.Path)) throw new InvalidDataException("Cleanup requires a direct child of the configured package folder.");
						RequireUnownedFolderItem(issue.Path); RequireDirectAncestors(root);
						bool directory = (File.GetAttributes(issue.Path) & FileAttributes.Directory) != 0;
						items.Add(new LoadOrderFolderRepairItem { Name = name, IsDirectory = directory, ContentIdentity = FolderItemIdentity(issue.Path, directory, cancellationToken) });
					}
					catch (Exception error) when (IsRecoveryError(error) || error is InvalidOperationException)
					{
						issues.Add(new LoadOrderIssue(LoadOrderIssueKind.Inventory, LoadOrderDiagnosticSeverity.Error,
							Nexus.Client.Util.Localization.LanguageManager.Get("LoadOrder.FolderRepair.Manual.Message", "This unexpected item cannot be moved automatically."),
							Nexus.Client.Util.Localization.LanguageManager.Get("LoadOrder.FolderRepair.Manual.Direction", "Resolve the owning mod through NMM or move the item deliberately after checking its contents. Linked items, nested packages and large trees require manual review."), path: issue.Path));
						System.Diagnostics.Trace.TraceWarning("Load-order folder repair: {0}: {1}", issue.Path, error.Message);
					}
				}
				return new LoadOrderFolderRepairReview(new FolderRepairJournal
				{
					Id = Guid.NewGuid().ToString("N"), ModeId = ModeId, Target = authority.Target.Fingerprint, PackageRoot = Path.GetFullPath(root),
					Profile = profile, ManifestIdentity = analysis.Document.Sha256, InventoryIdentity = NativeLoadOrderDraft.InventoryIdentity(analysis.Inventory), Items = items.ToArray()
				}, issues);
			});
		}

		/// <summary>Retains reviewed content on the same volume outside Mods. The flushed journal remains available after success or interruption.</summary>
		public string ApplyFolderRepairs(LoadOrderFolderRepairReview review, bool explicitlyApproved, CancellationToken cancellationToken)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			if (!explicitlyApproved) throw new InvalidOperationException("The folder repair preview requires explicit approval.");
			return WithAuthority(authority =>
			{
				FolderRepairJournal journal = review.Journal; ValidateFolderJournal(journal, authority);
				RequireNoPendingManifest(Manifest(journal.Profile));
				if (Read(journal.Profile).Sha256 != journal.ManifestIdentity) throw new InvalidOperationException("The native order changed after folder review.");
				LoadOrderInventory inventory = LoadOrderProviderRegistry.Create(_mode).ReadInventory(cancellationToken);
				if (NativeLoadOrderDraft.InventoryIdentity(inventory) != journal.InventoryIdentity) throw new InvalidOperationException("The package folder changed after repair review.");
				if (journal.Items.Length == 0) return null;
				foreach (LoadOrderFolderRepairItem item in journal.Items) RequireFolderItem(journal, item, false, cancellationToken);
				string directory = FolderRecoveryPath(journal.PackageRoot, journal.Id);
				RequireDirectAncestors(Path.GetDirectoryName(journal.PackageRoot));
				if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("The retained repair destination already exists.");
				string recoveryRoot = Path.GetDirectoryName(directory);
				if (ExistsFolderEntry(recoveryRoot)) RequireDirectAncestors(recoveryRoot);
				else { Directory.CreateDirectory(recoveryRoot); RequireDirectAncestors(recoveryRoot); }
				Directory.CreateDirectory(directory); RequireDirectAncestors(directory);
				byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(journal));
				if (bytes.Length > MaximumFolderJournalBytes) throw new InvalidDataException("Folder recovery record is too large.");
				WriteNewFlushed(Path.Combine(directory, "repair.json"), bytes);
				try { MoveFolderItems(journal, false, LarianLoadOrderDocument.Hash(bytes), cancellationToken); }
				catch (Exception error) { throw new LoadOrderFolderRepairException(directory, error); }
				return directory;
			});
		}

		/// <summary>Explicitly restores retained items or completes interrupted moves, refusing unknown bytes, ownership, paths and occupied destinations.</summary>
		public string RecoverFolderRepairs(string recoveryId, bool restoreOriginal, bool explicitlyApproved, CancellationToken cancellationToken)
		{
			if (!explicitlyApproved) throw new InvalidOperationException("An explicit folder recovery choice is required.");
			return WithAuthority(authority =>
			{
				string root = ModDeploymentTargetResolver.GetPhysicalRootPath(_mode, ModDeploymentRoot.Data);
				string directory = FolderRecoveryPath(root, recoveryId);
				FolderRepairJournal journal = ReadFolderJournal(recoveryId, authority, out string identity);
				RequireNoPendingManifest(Manifest(journal.Profile));
				MoveFolderItems(journal, restoreOriginal, identity, cancellationToken);
				return directory;
			});
		}

		/// <summary>Inspects exact content locations and current ownership without automatically choosing a recovery direction.</summary>
		public LoadOrderFolderRecovery InspectFolderRecovery(string recoveryId, CancellationToken cancellationToken)
		{
			return WithAuthority(authority =>
			{
				FolderRepairJournal journal = ReadFolderJournal(recoveryId, authority, out string identity);
				string directory = FolderRecoveryPath(journal.PackageRoot, journal.Id); int retainedCount = 0;
				try
				{
					RequireNoPendingManifest(Manifest(journal.Profile)); RequireDirectAncestors(journal.PackageRoot);
					foreach (LoadOrderFolderRepairItem item in journal.Items)
					{
						bool original = ExistsFolderEntry(Path.Combine(journal.PackageRoot, item.Name));
						bool retained = ExistsFolderEntry(Path.Combine(directory, item.Name));
						if (original == retained) throw new IOException("An item has missing or conflicting copies.");
						RequireFolderItem(journal, item, retained, cancellationToken); if (retained) retainedCount++;
					}
					RequireFolderJournalIdentity(journal, identity);
					return new LoadOrderFolderRecovery(journal, directory, retainedCount == 0 ? LoadOrderFolderRecoveryState.OriginalIntact : retainedCount == journal.Items.Length ? LoadOrderFolderRecoveryState.RetainedIntact : LoadOrderFolderRecoveryState.Mixed, null);
				}
				catch (Exception error) when (IsRecoveryError(error) || error is InvalidOperationException)
				{ return new LoadOrderFolderRecovery(journal, directory, LoadOrderFolderRecoveryState.Conflict, error.Message); }
			});
		}

		/// <summary>Reads strict bounded recovery metadata, bound to the current native target and selected opaque identity.</summary>
		private FolderRepairJournal ReadFolderJournal(string recoveryId, CollectionTargetAuthority authority, out string identity)
		{
			string root = ModDeploymentTargetResolver.GetPhysicalRootPath(_mode, ModDeploymentRoot.Data);
			string directory = FolderRecoveryPath(root, recoveryId); RequireDirectAncestors(directory);
			byte[] bytes = LarianLoadOrderDocument.ReadBytes(Path.Combine(directory, "repair.json"), MaximumFolderJournalBytes);
			FolderRepairJournal journal;
			using (var text = new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
			using (var reader = new JsonTextReader(text) { MaxDepth = 16 })
			{
				JObject json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
				if (reader.Read() || json.DescendantsAndSelf().OfType<JObject>().Any(item => item.Properties().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))) throw new InvalidDataException("Ambiguous folder recovery record.");
				journal = json.ToObject<FolderRepairJournal>(JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }));
			}
			ValidateFolderJournal(journal, authority);
			if (journal.Id != recoveryId) throw new InvalidDataException("Folder recovery identity differs.");
			identity = LarianLoadOrderDocument.Hash(bytes); return journal;
		}

		/// <summary>Discovers durable cleanup intents after a restart without moving or deleting any retained content.</summary>
		public IReadOnlyList<string> ListFolderRecoveryIds()
		{
			RequireCurrentMode();
			string root = ModDeploymentTargetResolver.GetPhysicalRootPath(_mode, ModDeploymentRoot.Data);
			string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd('\\')), FolderRecoveryDirectory);
			if (!ExistsFolderEntry(directory)) return Array.AsReadOnly(new string[0]);
			RequireDirectAncestors(directory);
			string[] directories = Directory.EnumerateDirectories(directory).Take(1025).ToArray();
			if (directories.Length > 1024) throw new InvalidDataException("Too many retained folder repairs. Review the recovery folder before continuing.");
			return Array.AsReadOnly(directories.Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out Guid id) && ExistsFolderEntry(Path.Combine(path, "repair.json")))
				.Select(Path.GetFileName).OrderBy(id => id, StringComparer.Ordinal).ToArray());
		}

		/// <summary>Validates all path segments against the active configured root rather than trusting journal paths.</summary>
		private void ValidateFolderJournal(FolderRepairJournal journal, CollectionTargetAuthority authority)
		{
			Guid id;
			if (journal == null || journal.Version != 1 || !Guid.TryParseExact(journal.Id, "N", out id) || journal.ModeId != ModeId || journal.Target != authority.Target.Fingerprint ||
				!SamePath(journal.PackageRoot, ModDeploymentTargetResolver.GetPhysicalRootPath(_mode, ModDeploymentRoot.Data)) || journal.Items == null || journal.Items.Length > 128 ||
				journal.Items.Any(item => item == null || item.ContentIdentity == null || item.ContentIdentity.Length != 64 || item.ContentIdentity.Any(character => !Uri.IsHexDigit(character))) ||
				journal.Items.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Items.Length)
				throw new InvalidDataException("Unrecognized folder recovery record.");
			Manifest(journal.Profile);
			foreach (LoadOrderFolderRepairItem item in journal.Items)
			{
				StructuredXmlRecordRule.Segment(item.Name);
				if (String.Equals(item.Name, "repair.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("An item conflicts with recovery metadata.");
				if (_capability.DirectoryPolicy.AllowedExtensions.Contains(Path.GetExtension(item.Name), StringComparer.OrdinalIgnoreCase) && !item.IsDirectory)
					throw new InvalidDataException("A folder repair cannot move a package.");
			}
		}

		/// <summary>Moves only recognized pre/postimages and never overwrites a newly occupied source or destination.</summary>
		private void MoveFolderItems(FolderRepairJournal journal, bool restore, string journalIdentity, CancellationToken cancellationToken)
		{
			string directory = FolderRecoveryPath(journal.PackageRoot, journal.Id);
			RequireDirectAncestors(directory); RequireDirectAncestors(journal.PackageRoot);
			RequireFolderJournalIdentity(journal, journalIdentity);
			// Check the entire batch before the first move. An interrupted batch remains recoverable from this same intent.
			foreach (LoadOrderFolderRepairItem item in journal.Items)
			{
				string original = Path.Combine(journal.PackageRoot, item.Name), retained = Path.Combine(directory, item.Name);
				bool atOriginal = ExistsFolderEntry(original), atRetained = ExistsFolderEntry(retained);
				if (atOriginal == atRetained) throw new IOException("Folder recovery requires exactly one recognized copy of each item.");
				RequireFolderItem(journal, item, atRetained, cancellationToken);
			}
			foreach (LoadOrderFolderRepairItem item in journal.Items)
			{
				cancellationToken.ThrowIfCancellationRequested();
				RequireFolderJournalIdentity(journal, journalIdentity);
				string original = Path.Combine(journal.PackageRoot, item.Name), retained = Path.Combine(directory, item.Name);
				string source = restore ? retained : original, destination = restore ? original : retained;
				if (!ExistsFolderEntry(source)) continue;
				RequireFolderItem(journal, item, restore, cancellationToken);
				if (ExistsFolderEntry(destination)) throw new IOException("The folder repair destination became occupied.");
				if (item.IsDirectory) Directory.Move(source, destination); else File.Move(source, destination);
				RequireFolderItem(journal, item, !restore, cancellationToken);
			}
		}

		/// <summary>Stops recovery if its on-disk intent changes after inspection or approval.</summary>
		private static void RequireFolderJournalIdentity(FolderRepairJournal journal, string identity)
		{
			if (LarianLoadOrderDocument.Hash(LarianLoadOrderDocument.ReadBytes(Path.Combine(FolderRecoveryPath(journal.PackageRoot, journal.Id), "repair.json"), MaximumFolderJournalBytes)) != identity)
				throw new InvalidDataException("The folder recovery record changed during the operation.");
		}

		/// <summary>Revalidates content and ownership for each recognized item before mutation.</summary>
		private void RequireFolderItem(FolderRepairJournal journal, LoadOrderFolderRepairItem item, bool retained, CancellationToken cancellationToken)
		{
			string original = Path.Combine(journal.PackageRoot, item.Name);
			RequireUnownedFolderItem(original);
			string path = retained ? Path.Combine(FolderRecoveryPath(journal.PackageRoot, journal.Id), item.Name) : original;
			if (FolderItemIdentity(path, item.IsDirectory, cancellationToken) != item.ContentIdentity) throw new IOException("An item changed after folder repair review.");
		}

		/// <summary>Protects every native owner, shadow owner and virtual link at, below or above the proposed item.</summary>
		private void RequireUnownedFolderItem(string path)
		{
			InstallLogReadSnapshot snapshot = _services.ModManager.InstallationLog.GetCommittedStateSnapshot();
			Func<string, bool> overlaps = candidate => SamePath(candidate, path) || Path.GetFullPath(candidate).StartsWith(Path.GetFullPath(path).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
				Path.GetFullPath(path).StartsWith(Path.GetFullPath(candidate).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
			Func<ModDeploymentTarget, bool> matches = target => overlaps(ModDeploymentTargetResolver.GetPhysicalPath(_mode, target));
			if (snapshot.Files.Any(file => matches(file.Target) && file.OwnerKeys.Count > 0) || snapshot.DeploymentTargets.Any(file => matches(file.Target) && file.OwnerKeys.Count > 0) ||
				_services.ModManager.VirtualModActivator.GetReadSnapshot().Links.Any(link => matches(link.Target)) ||
				snapshot.IniEdits.Any(edit => !String.IsNullOrEmpty(edit.File) && overlaps(Path.IsPathRooted(edit.File) ? edit.File : Path.Combine(ModDeploymentTargetResolver.GetPhysicalRootPath(_mode, ModDeploymentRoot.Data), edit.File))))
				throw new InvalidOperationException("The item overlaps native file/INI ownership or a virtual deployment link.");
		}

		/// <summary>Computes a bounded tree/content identity without following links or silently moving misplaced packages.</summary>
		internal static string FolderItemIdentity(string path, bool directory, CancellationToken cancellationToken)
		{
			var pending = new Stack<Tuple<string, int>>(); pending.Push(Tuple.Create(path, 0));
			var entries = new List<string>(); long total = 0;
			while (pending.Count > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Tuple<string, int> current = pending.Pop(); FileAttributes attributes = File.GetAttributes(current.Item1);
				if ((attributes & FileAttributes.ReparsePoint) != 0 || current.Item2 > 16 || entries.Count >= 1000 || pending.Count > 1000) throw new InvalidDataException("Linked or oversized content requires manual review.");
				bool isDirectory = (attributes & FileAttributes.Directory) != 0;
				StructuredXmlRecordRule.Segment(Path.GetFileName(current.Item1));
				if (current.Item2 == 0 && isDirectory != directory) throw new IOException("The item changed type.");
				string relative = current.Item2 == 0 ? "." : current.Item1.Substring(path.Length + 1);
				if (isDirectory)
				{
					entries.Add("D|" + relative);
					foreach (string child in Directory.EnumerateFileSystemEntries(current.Item1).Take(1002))
					{
						if (pending.Count >= 1001) throw new InvalidDataException("Too many files for automatic folder repair.");
						pending.Push(Tuple.Create(child, current.Item2 + 1));
					}
				}
				else
				{
					if (String.Equals(Path.GetExtension(current.Item1), ".pak", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Misplaced packages require deliberate installation through NMM.");
					using (var stream = new FileStream(current.Item1, FileMode.Open, FileAccess.Read, FileShare.Read))
					using (SHA256 hash = SHA256.Create())
					{
						total += stream.Length; if (total > 32 * 1024 * 1024) throw new InvalidDataException("Large content requires manual review.");
						entries.Add("F|" + relative + "|" + stream.Length + "|" + BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", String.Empty));
					}
				}
			}
			return LarianLoadOrderDocument.Hash(Encoding.UTF8.GetBytes(String.Join("\n", entries.OrderBy(entry => entry, StringComparer.Ordinal))));
		}

		/// <summary>Rejects redirected ancestors on either side of same-volume native folder recovery.</summary>
		private static void RequireDirectAncestors(string path)
		{
			for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
				if ((File.GetAttributes(current) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory) throw new InvalidDataException("Folder repair requires direct ordinary directories.");
		}

		/// <summary>Builds a same-volume destination outside the package folder from a validated opaque identity.</summary>
		private static string FolderRecoveryPath(string root, string id)
		{
			Guid parsed;
			if (!Guid.TryParseExact(id, "N", out parsed)) throw new InvalidDataException("Invalid folder recovery identity.");
			return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd('\\')), FolderRecoveryDirectory, id);
		}

		/// <summary>Distinguishes a missing path from access failures, including dangling link entries.</summary>
		private static bool ExistsFolderEntry(string path)
		{
			try { File.GetAttributes(path); return true; }
			catch (FileNotFoundException) { return false; }
			catch (DirectoryNotFoundException) { return false; }
		}
	}
}
