using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>
	/// One fresh, read-only observation of the game's settings file, pending publication and edition.
	/// A snapshot is diagnostic; it is never a write lease or proof of game-effective 5.x ordering.
	/// </summary>
	public sealed class Witcher3ModSettingsLifecycleSnapshot
	{
		internal Witcher3ModSettingsLifecycleSnapshot(string modeId, Witcher3Edition edition,
			string editionDiagnostic, Witcher3ModSettingsFileReadResult settings,
			Witcher3ModSettingsRecoveryInspection recovery)
		{
			ModeId = modeId;
			Edition = edition;
			EditionDiagnostic = editionDiagnostic;
			Settings = settings;
			Recovery = recovery;
		}

		public string ModeId { get; }
		public Witcher3Edition Edition { get; }
		public string EditionDiagnostic { get; }
		public Witcher3ModSettingsFileReadResult Settings { get; }
		public Witcher3ModSettingsRecoveryInspection Recovery { get; }
		public bool HasPendingPublication => Recovery.Status != Witcher3ModSettingsRecoveryStatus.None;

		/// <summary>
		/// A read-only readiness hint for W3R.6. Publish independently probes the live edition
		/// and revalidates the native target under its exclusive cross-process reservation.
		/// </summary>
		public bool CanPreviewLegacyManagedOrder =>
			String.Equals(ModeId, "Witcher3", StringComparison.OrdinalIgnoreCase) &&
			Edition == Witcher3Edition.Legacy && !HasPendingPublication &&
			Settings.IsLoaded && Settings.Document != null && Settings.Document.IsUnambiguous;
	}

	/// <summary>
	/// W3R.5c: the active-game lifecycle boundary for the W3R.4 reader and W3R.5a/b
	/// review/publisher. Startup and manual refresh only observe external game state.
	/// Publication/acknowledgement explicitly acquire the existing physical-target lease,
	/// reload and validate native authority, then refresh from disk. No startup auto-repair,
	/// archive Sort integration or Remastered 5.x game-order writer is provided here.
	/// </summary>
	public sealed class Witcher3ModSettingsLifecycleService
	{
		private readonly string _modeId;
		private readonly string _documentsDirectory;
		private readonly string _filePath;
		private readonly Func<string> _currentGameRoot;
		private readonly Func<string, Witcher3Edition> _editionProbe;
		private readonly Func<Tuple<CollectionTargetAuthority, GameStoragePathSet>> _currentTarget;
		private readonly CollectionTargetMutationLeaseManager _leases;
		private readonly Action<CollectionTargetMutationLease, CollectionTargetAuthority, GameStoragePathSet> _validateAndReload;
		private readonly Func<Witcher3ModSettingsChangePreview, string, Witcher3Edition,
			CollectionTargetAuthority, CollectionTargetMutationLease, bool, Witcher3ModSettingsPublishResult> _publish;
		private Witcher3ModSettingsLifecycleSnapshot _current;

		public static bool SupportsMode(string modeId) =>
			String.Equals(modeId, "Witcher3", StringComparison.OrdinalIgnoreCase) ||
			String.Equals(modeId, "Witcher3Remastered", StringComparison.OrdinalIgnoreCase);

		/// <summary>Production adapter; always resolves the CURRENT game/storage authority on a write.</summary>
		public Witcher3ModSettingsLifecycleService(ServiceManager services,
			GameStorageService gameStorageService, string documentsDirectory)
			: this(RequireProductionMode(services, gameStorageService), documentsDirectory,
				() => services.ModManager.GameMode.GameModeEnvironmentInfo.InstallationPath,
				root => Witcher3InstallationProbe.Inspect(root).Edition.Edition,
				() =>
				{
					GameStoragePathSet paths = gameStorageService.FromGameMode(services.ModManager.GameMode);
					return Tuple.Create(new CollectionTargetIdentityResolver(gameStorageService).Resolve(paths), paths);
				},
				CollectionTargetMutationLeaseManager.Shared,
				(lease, authority, paths) => new CollectionTargetOwnershipAuthorityValidator(
					gameStorageService, services).ValidateAndReload(lease, authority, paths),
				(preview, docs, edition, authority, lease, approved) =>
					Witcher3ModSettingsPublisher.Publish(preview, docs, edition, authority, lease, approved))
		{
		}

		/// <summary>Injectable integration boundary for disk fixtures; it does not weaken production validation.</summary>
		internal Witcher3ModSettingsLifecycleService(string modeId, string documentsDirectory,
			Func<string> currentGameRoot, Func<string, Witcher3Edition> editionProbe,
			Func<Tuple<CollectionTargetAuthority, GameStoragePathSet>> currentTarget,
			CollectionTargetMutationLeaseManager leases,
			Action<CollectionTargetMutationLease, CollectionTargetAuthority, GameStoragePathSet> validateAndReload,
			Func<Witcher3ModSettingsChangePreview, string, Witcher3Edition,
				CollectionTargetAuthority, CollectionTargetMutationLease, bool, Witcher3ModSettingsPublishResult> publish)
		{
			if (!SupportsMode(modeId)) throw new ArgumentException("A Witcher 3 game mode is required.", nameof(modeId));
			_filePath = Witcher3ModSettingsFileReader.GetFilePath(documentsDirectory);
			_modeId = modeId;
			_documentsDirectory = Path.GetFullPath(documentsDirectory);
			_currentGameRoot = currentGameRoot ?? throw new ArgumentNullException(nameof(currentGameRoot));
			_editionProbe = editionProbe ?? throw new ArgumentNullException(nameof(editionProbe));
			_currentTarget = currentTarget ?? throw new ArgumentNullException(nameof(currentTarget));
			_leases = leases ?? throw new ArgumentNullException(nameof(leases));
			_validateAndReload = validateAndReload ?? throw new ArgumentNullException(nameof(validateAndReload));
			_publish = publish ?? throw new ArgumentNullException(nameof(publish));
		}

		public string ModeId => _modeId;
		/// <summary>Resolved Documents root for read-only Witcher diagnostics.</summary>
		public string DocumentsDirectory => _documentsDirectory;

		/// <summary>The most recently published observation, or null before the first Refresh.</summary>
		public Witcher3ModSettingsLifecycleSnapshot Current => Volatile.Read(ref _current);

		/// <summary>
		/// Called once on startup and again on demand/after a write. Never acknowledges a pending
		/// marker, creates a settings file, caches an InstallLog, or changes the game.
		/// External editors can change this state at any time; always refresh before review.
		/// </summary>
		public Witcher3ModSettingsLifecycleSnapshot Refresh()
		{
			Witcher3Edition edition = Witcher3Edition.Ambiguous;
			string editionDiagnostic = null;
			try
			{
				edition = _editionProbe(_currentGameRoot());
			}
			catch (Exception error) when (error is ArgumentException || error is IOException ||
				error is UnauthorizedAccessException || error is SecurityException ||
				error is NotSupportedException || error is Win32Exception)
			{
				editionDiagnostic = "The current game edition could not be inspected: " + error.Message;
			}
			var settings = Witcher3ModSettingsFileReader.Read(_filePath);
			var recovery = Witcher3ModSettingsPublisher.InspectRecovery(_documentsDirectory);
			var fresh = new Witcher3ModSettingsLifecycleSnapshot(_modeId, edition,
				editionDiagnostic, settings, recovery);
			Volatile.Write(ref _current, fresh);
			return fresh;
		}

		/// <summary>
		/// Explicitly approved, Legacy-only publication. The preimage/edition and pending marker
		/// are rechecked by the W3R.5b publisher under freshly validated native authority.
		/// </summary>
		public Witcher3ModSettingsPublishResult Publish(Witcher3ModSettingsChangePreview preview,
			bool explicitlyApproved)
		{
			return Publish(preview, explicitlyApproved, null);
		}

		/// <summary>
		/// The UI binds its approval to the exact physical game root it originally displayed.
		/// This is checked after the current target authority has been reloaded under its lease.
		/// </summary>
		public Witcher3ModSettingsPublishResult Publish(Witcher3ModSettingsChangePreview preview,
			bool explicitlyApproved, string expectedPhysicalGameRoot)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (!IsLegacyMode || !explicitlyApproved)
				return new Witcher3ModSettingsPublishResult(Witcher3ModSettingsPublishStatus.Blocked,
					"Only an explicitly approved Legacy Witcher 3 change can be published.", null);
			try
			{
				return WithValidatedAuthority((authority, lease) =>
				{
					if (expectedPhysicalGameRoot != null &&
						!String.Equals(NormalizedRoot(expectedPhysicalGameRoot),
							NormalizedRoot(authority.CanonicalGameInstallPath), StringComparison.OrdinalIgnoreCase))
						return new Witcher3ModSettingsPublishResult(Witcher3ModSettingsPublishStatus.StaleReview,
							"The selected physical Witcher 3 installation changed since the review.", null);
					return _publish(preview, _documentsDirectory,
						Witcher3Edition.Legacy, authority, lease, true);
				});
			}
			finally { Refresh(); }
		}

		private static string NormalizedRoot(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A physical game root is required.", nameof(path));
			string full = Path.GetFullPath(path);
			string root = Path.GetPathRoot(full);
			return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
		}

		/// <summary>
		/// User-approved acknowledgement is separate from startup inspection. A conflict/invalid
		/// marker cannot be acknowledged; the W3R.5b recovery code preserves the game file/backup.
		/// </summary>
		public Witcher3ModSettingsRecoveryInspection AcknowledgePendingPublication(bool explicitlyApproved)
		{
			if (!IsLegacyMode)
				throw new InvalidOperationException("Only the Legacy game mode can acknowledge its publication marker.");
			if (!explicitlyApproved)
				return Refresh().Recovery;
			try
			{
				return WithValidatedAuthority((authority, lease) =>
					Witcher3ModSettingsPublisher.AcknowledgeRecovery(_documentsDirectory, authority, lease));
			}
			finally { Refresh(); }
		}

		private bool IsLegacyMode => String.Equals(_modeId, "Witcher3", StringComparison.OrdinalIgnoreCase);

		private T WithValidatedAuthority<T>(Func<CollectionTargetAuthority, CollectionTargetMutationLease, T> work)
		{
			Tuple<CollectionTargetAuthority, GameStoragePathSet> target = _currentTarget();
			if (target == null || target.Item1 == null || target.Item2 == null ||
				!String.Equals(target.Item1.GameId, _modeId, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The currently resolved Witcher 3 target is unavailable or different.");
			using (CollectionTargetMutationLease lease = _leases.Acquire(target.Item1))
			{
				_validateAndReload(lease, target.Item1, target.Item2);
				if (lease.RequiresRecovery || !lease.HasValidatedNativeAuthority)
					throw new InvalidOperationException("The native target must be recovered and validated before editing game settings.");
				return work(target.Item1, lease);
			}
		}

		private static string RequireProductionMode(ServiceManager services, GameStorageService gameStorageService)
		{
			if (services == null) throw new ArgumentNullException(nameof(services));
			if (gameStorageService == null) throw new ArgumentNullException(nameof(gameStorageService));
			if (services.ModManager == null || services.ModManager.GameMode == null)
				throw new InvalidOperationException("Witcher 3 load-order lifecycle requires the current native game services.");
			return services.ModManager.GameMode.ModeId;
		}
	}
}
