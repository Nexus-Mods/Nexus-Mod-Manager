using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Publication outcomes, including stale review and uncertain interrupted state.</summary>
	public enum IniOrderPublishStatus
	{
		Blocked = 0,
		Unchanged = 1,
		Published = 2,
		StaleReview = 3,
		RecoveryRequired = 4,
		FailedBeforeMutation = 5
	}

	/// <summary>Detached publication result and retained original backup path.</summary>
	public sealed class IniOrderPublishResult
	{
		/// <summary>Captures publication status without implying native archive mutation.</summary>
		internal IniOrderPublishResult(IniOrderPublishStatus status,
			string diagnostic, string backupPath)
		{
			Status = status;
			Diagnostic = diagnostic;
			BackupPath = backupPath;
		}

		public IniOrderPublishStatus Status { get; }
		public string Diagnostic { get; }
		/// <summary>Unique backup path when known. During interrupted staging the file may not exist yet.</summary>
		public string BackupPath { get; }
	}

	/// <summary>Recovery evidence for an interrupted exact-byte publication.</summary>
	public enum IniOrderRecoveryStatus
	{
		None = 0,
		OriginalIntact = 1,
		PublishedIntact = 2,
		Conflict = 3,
		InvalidMarker = 4
	}

	/// <summary>Read-only recovery evidence; conflicts are never cleaned automatically.</summary>
	public sealed class IniOrderRecoveryInspection
	{
		/// <summary>Captures reconciled file/backup evidence without granting recovery permission.</summary>
		internal IniOrderRecoveryInspection(IniOrderRecoveryStatus status,
			string diagnostic, string backupPath, string markerPath)
		{
			Status = status;
			Diagnostic = diagnostic;
			BackupPath = backupPath;
			MarkerPath = markerPath;
		}

		public IniOrderRecoveryStatus Status { get; }
		public string Diagnostic { get; }
		public string BackupPath { get; }
		public string MarkerPath { get; }
	}

	/// <summary>Internal failure-injection checkpoints for the isolated W3R.5b file publication tests.</summary>
	/// <summary>Internal checkpoints for the retained publication compatibility seam.</summary>
	internal enum IniOrderPublicationStep
	{
		AfterMarker,
		AfterBackup,
		AfterTemporary,
		BeforeFinalPreimageCheck,
		AfterReplacement
	}

	/// <summary>
	/// Existing-file publication under a live validated native lease and compiled host policy.
	/// Flushes pending intent before staging, verifies exact pre/postimages and retains the backup.
	/// Recovery acknowledges only known images and never rewrites the game file.
	/// </summary>
	internal sealed class IniOrderPublisher
	{
		private readonly string MarkerSuffix;
		private readonly string SidecarPrefix;
		private readonly string MarkerVersion;
		private const int MaxMarkerBytes = 512;
		private static readonly UTF8Encoding MarkerEncoding = new UTF8Encoding(false, true);

		private readonly string _target;
		private readonly IniOrderFormat _format;
		private readonly string _gameId;
		private readonly bool _allowCreate;
		private static readonly string MissingHash = new string('0', 64);

		/// <summary>Uses compiled sidecar identities; existing Witcher markers retain their historical format.</summary>
		internal IniOrderPublisher(string target, IniOrderFormat format, string gameId,
			string markerSuffix, string sidecarPrefix, string markerVersion,
			bool allowCreate = false)
		{
			_target = Path.GetFullPath(target);
			_format = format ?? throw new ArgumentNullException(nameof(format));
			_gameId = gameId ?? throw new ArgumentNullException(nameof(gameId));
			MarkerSuffix = markerSuffix;
			SidecarPrefix = sidecarPrefix;
			MarkerVersion = markerVersion;
			_allowCreate = allowCreate;
		}

		/// <summary>The bounded persisted pre/postimage identities for one interrupted operation.</summary>
		private sealed class PendingRecord
		{
			internal string Id;
			internal long BeforeLength;
			internal string BeforeHash;
			internal long AfterLength;
			internal string AfterHash;
		}

		/// <summary>Read-only reconciliation view, safe to call before acquiring a mutation lease.</summary>
		internal IniOrderRecoveryInspection InspectRecovery()
		{
			string target = _target;
			string marker = target + MarkerSuffix;
			byte[] bytes;
			try
			{
				if ((File.GetAttributes(marker) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
					return Recovery(IniOrderRecoveryStatus.InvalidMarker,
						"The publication marker is a directory or link; manual review is required.", null, marker);
				bytes = ReadBounded(marker, MaxMarkerBytes, FileShare.ReadWrite | FileShare.Delete);
			}
			catch (FileNotFoundException)
			{
				return Recovery(IniOrderRecoveryStatus.None, null, null, marker);
			}
			catch (DirectoryNotFoundException)
			{
				return Recovery(IniOrderRecoveryStatus.None, null, null, marker);
			}
			catch (Exception error) when (error is IOException || error is InvalidDataException ||
				error is UnauthorizedAccessException || error is System.Security.SecurityException)
			{
				return Recovery(IniOrderRecoveryStatus.InvalidMarker,
					"The pending publication record cannot be read: " + error.Message, null, marker);
			}

			PendingRecord record;
			if (!TryParseMarker(bytes, out record))
				return Recovery(IniOrderRecoveryStatus.InvalidMarker,
					"The pending publication record is incomplete or unrecognized; do not clean it automatically.", null, marker);

			string backup = BackupPath(target, record.Id);
			string temporary = TemporaryPath(target, record.Id);
			try
			{
				if (((File.Exists(target) || Directory.Exists(target)) && IsLinkedOrDirectory(target)) ||
					((File.Exists(backup) || Directory.Exists(backup)) && IsLinkedOrDirectory(backup)) ||
					((File.Exists(temporary) || Directory.Exists(temporary)) && IsLinkedOrDirectory(temporary)))
					return Recovery(IniOrderRecoveryStatus.Conflict,
						"A pending game file, backup or stage was redirected to a directory or link.", backup, marker);
				if (File.Exists(backup) && !MatchesFile(backup, record.BeforeLength, record.BeforeHash))
					return Recovery(IniOrderRecoveryStatus.Conflict,
						"The retained original backup no longer matches its recorded identity.", backup, marker);
				if (File.Exists(temporary) && !MatchesFile(temporary, record.AfterLength, record.AfterHash))
					return Recovery(IniOrderRecoveryStatus.Conflict,
						"The unpublished temporary file no longer matches its recorded identity.", backup, marker);
				if (record.BeforeLength == -1)
				{
					if (!File.Exists(target) && !Directory.Exists(target))
						return Recovery(IniOrderRecoveryStatus.OriginalIntact,
							"The file was not created. The interrupted first publication can be acknowledged.", null, marker);
					if (MatchesFile(target, record.AfterLength, record.AfterHash))
						return Recovery(IniOrderRecoveryStatus.PublishedIntact,
							"The newly created file matches its recorded bytes. Acknowledge before another write.", null, marker);
					return Recovery(IniOrderRecoveryStatus.Conflict,
						"The newly created order file differs from the pending image; manual recovery is required.", null, marker);
				}
				if (MatchesFile(target, record.BeforeLength, record.BeforeHash))
					return Recovery(IniOrderRecoveryStatus.OriginalIntact,
						"The old settings bytes are intact. The interrupted publication can be acknowledged without replacing them.", backup, marker);
				if (File.Exists(backup) && MatchesFile(target, record.AfterLength, record.AfterHash))
					return Recovery(IniOrderRecoveryStatus.PublishedIntact,
						"The new settings and original backup are intact. Acknowledge the committed change before another write.", backup, marker);
				return Recovery(IniOrderRecoveryStatus.Conflict,
					"The game settings differ from both recorded versions or the required backup is missing; manual review is required.",
					backup, marker);
			}
			catch (Exception error) when (error is IOException || error is InvalidDataException ||
				error is UnauthorizedAccessException || error is System.Security.SecurityException)
			{
				return Recovery(IniOrderRecoveryStatus.Conflict,
					"Could not verify the pending settings and backup: " + error.Message, backup, marker);
			}
		}

		/// <summary>
		/// After restart and native recovery, acknowledges ONLY a verified old or new image.
		/// Deletes the pending marker and verified leftover staging bytes, never the backup or
		/// order settings. Conflicting/invalid states remain blocked for manual recovery.
		/// </summary>
		internal IniOrderRecoveryInspection AcknowledgeRecovery(
			CollectionTargetAuthority authority, CollectionTargetMutationLease lease)
		{
			RequireNativeAuthority(authority, lease);
			string target = _target;
			IniOrderRecoveryInspection inspection = InspectRecovery();
			if (inspection.Status != IniOrderRecoveryStatus.OriginalIntact &&
				inspection.Status != IniOrderRecoveryStatus.PublishedIntact)
				return inspection;
			try
			{
				byte[] firstMarker = ReadBounded(target + MarkerSuffix, MaxMarkerBytes,
					FileShare.ReadWrite | FileShare.Delete);
				PendingRecord record;
				if (!TryParseMarker(firstMarker, out record))
					return InspectRecovery();
				// Re-evaluate immediately before cleanup; do not trust an earlier UI inspection.
				inspection = InspectRecovery();
				if (inspection.Status != IniOrderRecoveryStatus.OriginalIntact &&
					inspection.Status != IniOrderRecoveryStatus.PublishedIntact)
					return inspection;
				// Another tool may have replaced the pending marker itself while it was read.
				if (!firstMarker.SequenceEqual(ReadBounded(target + MarkerSuffix,
					MaxMarkerBytes, FileShare.ReadWrite | FileShare.Delete)))
					return Recovery(IniOrderRecoveryStatus.Conflict,
						"The pending marker changed during recovery acknowledgement.", inspection.BackupPath, target + MarkerSuffix);
				string temporary = TemporaryPath(target, record.Id);
				if (File.Exists(temporary))
				{
					if (!MatchesFile(temporary, record.AfterLength, record.AfterHash))
						return Recovery(IniOrderRecoveryStatus.Conflict,
							"The pending staged bytes changed during acknowledgement.", inspection.BackupPath, target + MarkerSuffix);
					File.Delete(temporary);
				}
				File.Delete(target + MarkerSuffix);
				return Recovery(IniOrderRecoveryStatus.None,
					"Verified publication record acknowledged; the original backup, if created, was retained.",
					inspection.BackupPath, target + MarkerSuffix);
			}
			catch (Exception error) when (error is IOException || error is InvalidDataException ||
					error is UnauthorizedAccessException || error is System.Security.SecurityException)
			{
				return Recovery(IniOrderRecoveryStatus.Conflict,
					"Unable to finalize publication recovery: " + error.Message,
					inspection.BackupPath, target + MarkerSuffix);
			}
		}

		/// <summary>
		/// Requires explicit approval, an exact reviewed preimage and live host policy before
		/// staging and immediately before replacement. Checkpoints retain the compatibility seam.
		/// </summary>
		internal IniOrderPublishResult Publish(IniOrderChangePreview preview,
			CollectionTargetAuthority authority,
			CollectionTargetMutationLease lease, bool explicitlyApproved,
			Func<bool> livePolicy, Action<IniOrderPublicationStep> checkpoint = null)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (livePolicy == null) throw new ArgumentNullException(nameof(livePolicy));
			RequireNativeAuthority(authority, lease);
			string target = _target;
			if (!String.Equals(Path.GetFullPath(preview.FilePath), target, StringComparison.OrdinalIgnoreCase))
				return Result(IniOrderPublishStatus.Blocked,
					"The reviewed file is not the configured resolved settings file.");
			bool creating = preview.ObservedStatus == IniOrderReadStatus.Missing;
			if ((creating && !_allowCreate) || !SameFormat(preview.Format) || !explicitlyApproved ||
				preview.Status == IniOrderChangePlanStatus.Blocked)
				return Result(IniOrderPublishStatus.Blocked,
					"Explicit approval and a supported configured game-order preview are required.");
			if (!livePolicy())
				return Result(IniOrderPublishStatus.Blocked,
					"The live host policy does not permit order publication.");
			if (InspectRecovery().Status != IniOrderRecoveryStatus.None)
				return Result(IniOrderPublishStatus.RecoveryRequired,
					"A previous publication must be inspected and acknowledged before another write.");

			byte[] proposed;
			try
			{
				if (!preview.TextEncoding.HasValue || !preview.ProposedByteLength.HasValue ||
					(!creating && (!preview.ObservedByteLength.HasValue || String.IsNullOrEmpty(preview.ObservedSha256))) ||
					(creating && (preview.ObservedByteLength.HasValue || preview.ObservedSha256 != null ||
						preview.TextEncoding != IniOrderTextEncoding.Utf8)) ||
					String.IsNullOrEmpty(preview.ProposedSha256) || preview.ProposedText == null)
					return Result(IniOrderPublishStatus.Blocked, "The reviewed file identity is incomplete.");
				proposed = IniOrderByteCodec.Encode(preview.ProposedText, preview.TextEncoding.Value);
				if (proposed.Length > IniOrderFileReader.DefaultMaxBytes)
					return Result(IniOrderPublishStatus.Blocked,
						"The proposed settings exceed the bounded file reader's capacity.");
				if (!Matches(proposed, preview.ProposedByteLength.Value, preview.ProposedSha256) ||
					!IniOrderParser.Parse(preview.ProposedText, _format).IsUnambiguous)
					return Result(IniOrderPublishStatus.Blocked, "The proposed settings identity or format is invalid.");
				if ((File.Exists(target) || Directory.Exists(target)) && IsLinkedOrDirectory(target))
					return Result(IniOrderPublishStatus.Blocked, "Directory or linked settings targets cannot be replaced.");
			}
			catch (Exception error) when (error is IOException || error is InvalidDataException ||
				error is UnauthorizedAccessException || error is System.Security.SecurityException)
			{
				return Result(IniOrderPublishStatus.FailedBeforeMutation,
					"Could not inspect the settings file: " + error.Message);
			}

			IniOrderFileObservation current = IniOrderFileReader.Read(target, _format);
			if (!(creating ? current.Status == IniOrderReadStatus.Missing : SamePreimage(current, preview)))
				return Result(IniOrderPublishStatus.StaleReview,
					"The settings file changed since review. Refresh the preview instead of overwriting external changes.");
			if (preview.Status == IniOrderChangePlanStatus.Unchanged)
				return Result(IniOrderPublishStatus.Unchanged, null);
			if (preview.Status != IniOrderChangePlanStatus.PreviewAvailable ||
				String.Equals(preview.ObservedSha256, preview.ProposedSha256, StringComparison.OrdinalIgnoreCase))
				return Result(IniOrderPublishStatus.Blocked, "No distinct reviewed change is available.");

			var record = new PendingRecord
			{
				Id = Guid.NewGuid().ToString("N"), BeforeLength = creating ? -1 : preview.ObservedByteLength.Value,
				BeforeHash = creating ? MissingHash : preview.ObservedSha256.ToUpperInvariant(),
				AfterLength = proposed.Length, AfterHash = preview.ProposedSha256.ToUpperInvariant()
			};
			string marker = target + MarkerSuffix;
			string backup = BackupPath(target, record.Id);
			string temporary = TemporaryPath(target, record.Id);
			bool markerCreated = false;
			try
			{
				if (creating) RequireSafeNewFileParent(target);
				// CreateNew is the final exclusive collision check even if another NMM process
				// targets the same Documents directory through a different game authority.
				WriteFlushedNew(marker, SerializeMarker(record));
				markerCreated = true;
				checkpoint?.Invoke(IniOrderPublicationStep.AfterMarker);

				if (!creating)
				{
					byte[] original = ReadBounded(target, IniOrderFileReader.DefaultMaxBytes,
						FileShare.Read | FileShare.Delete);
					if (!Matches(original, record.BeforeLength, record.BeforeHash))
						return Result(IniOrderPublishStatus.RecoveryRequired,
							"The original settings changed while preparing their backup. The pending record was retained.");
					WriteFlushedNew(backup, original);
					if (!MatchesFile(backup, record.BeforeLength, record.BeforeHash))
						throw new IOException("The original backup did not verify after flushing.");
				}
				checkpoint?.Invoke(IniOrderPublicationStep.AfterBackup);

				WriteFlushedNew(temporary, proposed);
				if (!MatchesFile(temporary, record.AfterLength, record.AfterHash))
					throw new IOException("The replacement stage did not verify after flushing.");
				checkpoint?.Invoke(IniOrderPublicationStep.AfterTemporary);
				checkpoint?.Invoke(IniOrderPublicationStep.BeforeFinalPreimageCheck);
				RequireNativeAuthority(authority, lease);
				if (!livePolicy())
					return Result(IniOrderPublishStatus.RecoveryRequired,
						"The live host policy changed during publication; no replacement was attempted.", backup);
				if ((File.Exists(target) || Directory.Exists(target)) && IsLinkedOrDirectory(target))
					return Result(IniOrderPublishStatus.RecoveryRequired,
						"The settings target was redirected before replacement; no replacement was attempted.", creating ? null : backup);
				if (creating)
				{
					// Move must fail when another editor creates the file first. No synthetic
					// zero-byte preimage and no overwrite of an unobserved external file.
					if (File.Exists(target) || Directory.Exists(target))
						return Result(IniOrderPublishStatus.RecoveryRequired,
							"Another writer created mods.settings after review. The pending intent is retained.");
					File.Move(temporary, target);
				}
				else
				{
					// Deny ordinary concurrent writers while hashing and replacing the SAME file.
					// FileShare.Delete permits Windows ReplaceFile to operate on the target.
					using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read,
						FileShare.Read | FileShare.Delete))
					{
						byte[] latest = ReadBounded(locked, IniOrderFileReader.DefaultMaxBytes);
						if (!Matches(latest, record.BeforeLength, record.BeforeHash))
							return Result(IniOrderPublishStatus.RecoveryRequired,
								"An external edit invalidated the staged publication; the pending record was retained.", backup);
						File.Replace(temporary, target, null);
					}
				}
				checkpoint?.Invoke(IniOrderPublicationStep.AfterReplacement);

				if (!MatchesFile(target, record.AfterLength, record.AfterHash) ||
					(!creating && !MatchesFile(backup, record.BeforeLength, record.BeforeHash)))
					return Result(IniOrderPublishStatus.RecoveryRequired,
						"Post-write verification failed. The recovery marker and original backup were retained.", backup);
				File.Delete(marker);
				return Result(IniOrderPublishStatus.Published, null, creating ? null : backup);
			}
			catch (Exception error)
			{
				// A failed CreateNew/Flush can leave an incomplete marker. Any marker
				// requires reconciliation even if the target itself was never changed.
				bool pending = markerCreated || File.Exists(marker);
				return Result(pending ? IniOrderPublishStatus.RecoveryRequired :
					IniOrderPublishStatus.FailedBeforeMutation,
					"The settings publication was interrupted: " + error.Message +
					(pending ? " Inspect the pending marker before retrying." : ""),
					File.Exists(backup) ? backup : null);
			}
		}

		/// <summary>Requires the exact live validated canonical root reservation for this game.</summary>
		private void RequireNativeAuthority(CollectionTargetAuthority authority, CollectionTargetMutationLease lease)
		{
			if (authority == null) throw new ArgumentNullException(nameof(authority));
			if (lease == null) throw new ArgumentNullException(nameof(lease));
			if (lease.IsDisposed) throw new ObjectDisposedException(nameof(lease));
			if (!lease.IsRoot || !lease.HasCrossProcessReservation || !lease.HasValidatedNativeAuthority || lease.RequiresRecovery)
				throw new InvalidOperationException("Order publication requires live validated native root authority.");
			if (authority.Target == null || !authority.Target.IsCanonical ||
				!String.Equals(authority.Target.Fingerprint, lease.TargetFingerprint, StringComparison.Ordinal) ||
				!String.Equals(authority.GameId, _gameId, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The load-order publisher requires the configured game and its exact validated native target authority.");
		}

		/// <summary>Prevents a preview from a different interpretation from being published to this target.</summary>
		private bool SameFormat(IniOrderFormat format) => format != null &&
			format.ComponentPrefix == _format.ComponentPrefix && format.PriorityKey == _format.PriorityKey && format.EnabledKey == _format.EnabledKey;

		/// <summary>Compares original bytes and encoding rather than relying on file timestamps.</summary>
		private static bool SamePreimage(IniOrderFileObservation current,
			IniOrderChangePreview preview)
		{
			return current.Status == IniOrderReadStatus.Loaded && current.Document != null &&
				current.Document.IsUnambiguous && current.Encoding == preview.TextEncoding &&
				current.ByteLength == preview.ObservedByteLength &&
				String.Equals(current.ContentSha256, preview.ObservedSha256, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Creates a publication result without changing recovery evidence.</summary>
		private static IniOrderPublishResult Result(IniOrderPublishStatus status,
			string diagnostic, string backup = null)
		{
			return new IniOrderPublishResult(status, diagnostic, backup);
		}

		/// <summary>Creates a detached recovery inspection.</summary>
		private static IniOrderRecoveryInspection Recovery(IniOrderRecoveryStatus status,
			string diagnostic, string backup, string marker)
		{
			return new IniOrderRecoveryInspection(status, diagnostic, backup, marker);
		}

		/// <summary>Rejects a directory or linked file in place of a regular target.</summary>
		private static bool IsLinkedOrDirectory(string path)
		{
			return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0;
		}

		/// <summary>Retains the original backup beside the target with the compiled naming convention.</summary>
		private string BackupPath(string file, string id) => file + SidecarPrefix + id + ".backup";
		/// <summary>Stages the replacement beside the target for atomic file replacement.</summary>
		private string TemporaryPath(string file, string id) => file + SidecarPrefix + id + ".tmp";

		/// <summary>Preserves the bounded versioned marker wire format.</summary>
		private byte[] SerializeMarker(PendingRecord record)
		{
			return MarkerEncoding.GetBytes(String.Join("\n", new[]
			{
				MarkerVersion, record.Id, record.BeforeLength.ToString(CultureInfo.InvariantCulture),
				record.BeforeHash, record.AfterLength.ToString(CultureInfo.InvariantCulture),
				record.AfterHash, ""
			}));
		}

		/// <summary>Accepts only complete bounded marker identities with canonical GUID/hash spelling.</summary>
		private bool TryParseMarker(byte[] bytes, out PendingRecord record)
		{
			record = null;
			if (bytes == null) return false;
			string[] fields;
			try { fields = MarkerEncoding.GetString(bytes).Split('\n'); }
			catch (DecoderFallbackException) { return false; }
			if (fields.Length != 7 || fields[0] != MarkerVersion || fields[6] != "") return false;
			Guid id;
			long before, after;
			if (!Guid.TryParseExact(fields[1], "N", out id) || id.ToString("N") != fields[1] ||
				!Int64.TryParse(fields[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out before) ||
				!Int64.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out after) ||
				before > IniOrderFileReader.DefaultMaxBytes || after > IniOrderFileReader.DefaultMaxBytes ||
				!ValidHash(fields[3]) || !ValidHash(fields[5]) ||
				(before < 0 && !(_allowCreate && before == -1 && fields[3] == MissingHash))) return false;
			record = new PendingRecord { Id = fields[1], BeforeLength = before, BeforeHash = fields[3],
				AfterLength = after, AfterHash = fields[5] };
			return true;
		}

		/// <summary>Accepts an exact uppercase SHA-256 hexadecimal identity.</summary>
		private static bool ValidHash(string hash)
		{
			return hash != null && hash.Length == 64 && hash.All(c => c >= '0' && c <= '9' || c >= 'A' && c <= 'F');
		}

		/// <summary>
		/// Only the Documents/The Witcher 3 directory may be provisioned for a new file.
		/// Reject redirected ancestors; never follow an existing junction/symlink or create
		/// an arbitrary chain of missing parent directories.
		/// </summary>
		private static void RequireSafeNewFileParent(string target)
		{
			string parent = Path.GetDirectoryName(target);
			string documents = Path.GetDirectoryName(parent);
			if (!Directory.Exists(documents) ||
				(File.GetAttributes(documents) & FileAttributes.ReparsePoint) != 0)
				throw new IOException("The configured Documents directory is missing or redirected.");
			if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
			FileAttributes attributes = File.GetAttributes(parent);
			if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
				throw new IOException("The game's Documents settings directory is not a direct directory.");
		}

		/// <summary>Exclusively creates and durably flushes a sidecar before proceeding.</summary>
		private static void WriteFlushedNew(string path, byte[] bytes)
		{
			using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				output.Write(bytes, 0, bytes.Length);
				output.Flush(true);
			}
		}

		/// <summary>Reads one existing file with explicit sharing and byte limits.</summary>
		private static byte[] ReadBounded(string path, int limit, FileShare share)
		{
			using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, share))
				return ReadBounded(input, limit);
		}

		/// <summary>Rejects oversized or growing input while reading bounded bytes.</summary>
		private static byte[] ReadBounded(FileStream input, int limit)
		{
			if (input.Length > limit) throw new InvalidDataException("File exceeds the bounded settings/marker limit.");
			using (var memory = new MemoryStream())
			{
				var buffer = new byte[8192];
				int count;
				while ((count = input.Read(buffer, 0, Math.Min(buffer.Length,
					limit - (int)memory.Length + 1))) > 0)
				{
					memory.Write(buffer, 0, count);
					if (memory.Length > limit) throw new InvalidDataException("File grew beyond the bounded settings/marker limit.");
				}
				return memory.ToArray();
			}
		}

		/// <summary>Compares an existing bounded file against the recorded exact-byte identity.</summary>
		private static bool MatchesFile(string path, long length, string hash)
		{
			try { return Matches(ReadBounded(path, IniOrderFileReader.DefaultMaxBytes,
				FileShare.ReadWrite | FileShare.Delete), length, hash); }
			catch (FileNotFoundException) { return false; }
			catch (DirectoryNotFoundException) { return false; }
		}

		/// <summary>Compares both byte count and exact content hash.</summary>
		private static bool Matches(byte[] bytes, long length, string hash)
		{
			if (bytes.Length != length) return false;
			using (var sha = SHA256.Create())
				return String.Equals(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", String.Empty),
					hash, StringComparison.OrdinalIgnoreCase);
		}
	}
}
