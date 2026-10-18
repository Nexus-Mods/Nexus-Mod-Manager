using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.ModManagement.Witcher3
{
	public enum Witcher3ModSettingsPublishStatus
	{
		Blocked = 0,
		Unchanged = 1,
		Published = 2,
		StaleReview = 3,
		RecoveryRequired = 4,
		FailedBeforeMutation = 5
	}

	public sealed class Witcher3ModSettingsPublishResult
	{
		internal Witcher3ModSettingsPublishResult(Witcher3ModSettingsPublishStatus status,
			string diagnostic, string backupPath)
		{
			Status = status;
			Diagnostic = diagnostic;
			BackupPath = backupPath;
		}

		public Witcher3ModSettingsPublishStatus Status { get; }
		public string Diagnostic { get; }
		/// <summary>Unique backup path when known. During interrupted staging the file may not exist yet.</summary>
		public string BackupPath { get; }
	}

	public enum Witcher3ModSettingsRecoveryStatus
	{
		None = 0,
		OriginalIntact = 1,
		PublishedIntact = 2,
		Conflict = 3,
		InvalidMarker = 4
	}

	public sealed class Witcher3ModSettingsRecoveryInspection
	{
		internal Witcher3ModSettingsRecoveryInspection(Witcher3ModSettingsRecoveryStatus status,
			string diagnostic, string backupPath, string markerPath)
		{
			Status = status;
			Diagnostic = diagnostic;
			BackupPath = backupPath;
			MarkerPath = markerPath;
		}

		public Witcher3ModSettingsRecoveryStatus Status { get; }
		public string Diagnostic { get; }
		public string BackupPath { get; }
		public string MarkerPath { get; }
	}

	/// <summary>Internal failure-injection checkpoints for the isolated W3R.5b file publication tests.</summary>
	internal enum Witcher3ModSettingsPublicationStep
	{
		AfterMarker,
		AfterBackup,
		AfterTemporary,
		BeforeFinalPreimageCheck,
		AfterReplacement
	}

	/// <summary>Legacy-only publication adapter preserving the historical Witcher recovery protocol.</summary>
	public static class Witcher3ModSettingsPublisher
	{
		/// <summary>Resolves the fixed target and original persisted marker/backup naming.</summary>
		private static IniOrderPublisher Publisher(string documentsDirectory) => new IniOrderPublisher(
			Witcher3ModSettingsFileReader.GetFilePath(documentsDirectory), Witcher3ModSettingsParser.Format,
			"Witcher3", ".nmm-w3r.pending", ".nmm-w3r.", "nmm-ce.witcher3.mods-settings.pending/1");

		/// <summary>Publishes an explicitly approved Legacy preview under exact native authority.</summary>
		public static Witcher3ModSettingsPublishResult Publish(Witcher3ModSettingsChangePreview preview,
			string documentsDirectory, Witcher3Edition expectedEdition, CollectionTargetAuthority authority,
			CollectionTargetMutationLease lease, bool explicitlyApproved) =>
			PublishCore(preview, documentsDirectory, expectedEdition, authority, lease, explicitlyApproved,
				path => Witcher3InstallationProbe.Inspect(path).Edition.Edition, null);

		/// <summary>Observes historical pending state without repairing or acknowledging it.</summary>
		public static Witcher3ModSettingsRecoveryInspection InspectRecovery(string documentsDirectory) =>
			FromShared(Publisher(documentsDirectory).InspectRecovery());

		/// <summary>Acknowledges only exact known pre/postimages under the existing native lease.</summary>
		public static Witcher3ModSettingsRecoveryInspection AcknowledgeRecovery(string documentsDirectory,
			CollectionTargetAuthority authority, CollectionTargetMutationLease lease) =>
			FromShared(Publisher(documentsDirectory).AcknowledgeRecovery(authority, lease));

		/// <summary>Retains the internal checkpoint seam and live Legacy probe for compatibility.</summary>
		internal static Witcher3ModSettingsPublishResult PublishCore(Witcher3ModSettingsChangePreview preview,
			string documentsDirectory, Witcher3Edition expectedEdition, CollectionTargetAuthority authority,
			CollectionTargetMutationLease lease, bool explicitlyApproved,
			Func<string, Witcher3Edition> liveEditionProbe, Action<Witcher3ModSettingsPublicationStep> checkpoint)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (liveEditionProbe == null) throw new ArgumentNullException(nameof(liveEditionProbe));
			IniOrderPublishResult result = Publisher(documentsDirectory).Publish(preview.ToShared(), authority, lease,
				explicitlyApproved && expectedEdition == Witcher3Edition.Legacy,
				() => liveEditionProbe(authority.CanonicalGameInstallPath) == Witcher3Edition.Legacy,
				checkpoint == null ? (Action<IniOrderPublicationStep>)null : step => checkpoint((Witcher3ModSettingsPublicationStep)step));
			return new Witcher3ModSettingsPublishResult((Witcher3ModSettingsPublishStatus)result.Status, result.Diagnostic, result.BackupPath);
		}

		/// <summary>Preserves public recovery statuses and paths for the existing lifecycle/UI.</summary>
		private static Witcher3ModSettingsRecoveryInspection FromShared(IniOrderRecoveryInspection state) =>
			new Witcher3ModSettingsRecoveryInspection((Witcher3ModSettingsRecoveryStatus)state.Status,
				state.Diagnostic, state.BackupPath, state.MarkerPath);
	}
}
