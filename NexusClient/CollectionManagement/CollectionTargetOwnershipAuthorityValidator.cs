using System;
using System.IO;
using System.Diagnostics;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Validates and reloads the one native ownership authority permitted to mutate a canonical Collection target.
	/// </summary>
	/// <remarks>
	/// Validation must run while the C4.15 physical-target reservation is held. The validator re-resolves Game Storage
	/// identity, reloads native InstallLog/VMA/deployment state, validates the resulting InstallLog, then binds the physical
	/// target to that native authority. It never merges or guesses across conflicting InstallLogs.
	/// </remarks>
	public sealed class CollectionTargetOwnershipAuthorityValidator
	{
		private readonly GameStorageService _gameStorageService;
		private readonly ICollectionNativeStateReloader _nativeStateReloader;
		private readonly CollectionTargetOwnershipAuthorityStore _authorityStore;
		private readonly Action<GameStoragePathSet> _witcher3NativeMutationPreflight;

		/// <summary>
		/// Creates a production validator using the machine-wide authority store and current NMM service graph.
		/// </summary>
		public CollectionTargetOwnershipAuthorityValidator(GameStorageService gameStorageService, ServiceManager services)
			: this(gameStorageService, new CollectionServiceNativeStateReloader(services),
				new CollectionTargetOwnershipAuthorityStore(), CreateWitcher3Preflight(services))
		{
		}

		/// <summary>
		/// Creates a validator over explicit reload and binding services.
		/// </summary>
		public CollectionTargetOwnershipAuthorityValidator(GameStorageService gameStorageService,
			ICollectionNativeStateReloader nativeStateReloader, CollectionTargetOwnershipAuthorityStore authorityStore)
			: this(gameStorageService, nativeStateReloader, authorityStore, null)
		{
		}

		/// <summary>Testable counterpart-authority boundary. Production uses the live service graph.</summary>
		internal CollectionTargetOwnershipAuthorityValidator(GameStorageService gameStorageService,
			ICollectionNativeStateReloader nativeStateReloader, CollectionTargetOwnershipAuthorityStore authorityStore,
			Action<GameStoragePathSet> witcher3NativeMutationPreflight)
		{
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_nativeStateReloader = nativeStateReloader ?? throw new ArgumentNullException(nameof(nativeStateReloader));
			_authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
			_witcher3NativeMutationPreflight = witcher3NativeMutationPreflight;
		}

		private static Action<GameStoragePathSet> CreateWitcher3Preflight(ServiceManager services)
		{
			if (services == null) throw new ArgumentNullException(nameof(services));
			return paths =>
			{
				// Never validate a different game's target against the active service graph.
				var manager = services.ModManager;
				if (manager == null || manager.GameMode == null || manager.EnvironmentInfo == null ||
					!String.Equals(manager.GameMode.ModeId, paths.GameId, StringComparison.OrdinalIgnoreCase))
					throw new CollectionTargetOwnershipAuthorityException(
						CollectionTargetOwnershipAuthorityFailureKind.TargetChanged,
						"The active native service graph does not match this native mutation target.");
				// Use the reserved target's actual paths, including the proposed VirtualInstall
				// directory. Settings captured before Game Storage relocation are not authority.
				Witcher3RootOwnershipDecision decision = Witcher3RootOwnershipGuard.CheckForStorageTransition(
					manager.EnvironmentInfo, paths.GameId, paths.GameInstallPath,
					paths.InstallInfoPath, paths.VirtualInstallPath);
				if (!decision.AllowsNativeMutation)
					throw new CollectionTargetOwnershipAuthorityException(
						CollectionTargetOwnershipAuthorityFailureKind.Witcher3CounterpartAuthorityConflict,
						"Native root ownership is not exclusive (" + decision.Status + "): " + decision.Reason);
			};
		}

		private void ValidateWitcher3Counterpart(GameStoragePathSet paths)
		{
			if (_witcher3NativeMutationPreflight != null && (Nexus.Client.Games.GameModeRuntimePolicies.For(paths.GameId).NativeRootOwnershipProfile != null))
				_witcher3NativeMutationPreflight(paths);
		}

		/// <summary>
		/// Revalidates native target identity, reloads authoritative state, and returns the accepted authority binding.
		/// </summary>
		/// <remarks>
		/// Only the root canonical mutation lease may cross this boundary. An abandoned C4.15 mutex remains recovery-required
		/// until this method has successfully reloaded InstallLog, Virtual Mod Activator topology and deployment recovery.
		/// </remarks>
		public CollectionTargetOwnershipAuthorityBinding ValidateAndReload(CollectionTargetMutationLease mutationLease,
			CollectionTargetAuthority authority, GameStoragePathSet paths)
		{
			return ValidateAuthority(mutationLease, authority, paths, true);
		}

		/// <summary>Revalidates target identity after a committed native install under the same uninterrupted root reservation.</summary>
		internal CollectionTargetOwnershipAuthorityBinding ValidateCommittedAuthority(CollectionTargetMutationLease mutationLease,
			CollectionTargetAuthority authority, GameStoragePathSet paths)
		{
			ValidateLease(mutationLease, authority);
			if (mutationLease.RequiresRecovery)
				throw new InvalidOperationException("An abandoned target reservation requires native recovery before committed-state reuse.");
			if (!mutationLease.HasValidatedNativeAuthority)
				throw new InvalidOperationException("Native authority must be reloaded under this root reservation before committed-state reuse.");
			return ValidateAuthority(mutationLease, authority, paths, false);
		}

		/// <summary>Validates physical ownership, reloading native stores when entering a reservation or recovering unknown durability.</summary>
		private CollectionTargetOwnershipAuthorityBinding ValidateAuthority(CollectionTargetMutationLease mutationLease,
			CollectionTargetAuthority authority, GameStoragePathSet paths, bool reloadNativeState)
		{
			ValidateLease(mutationLease, authority);
			if (paths == null)
				throw new ArgumentNullException(nameof(paths));
			Stopwatch stopwatch = Stopwatch.StartNew();
			Trace.TraceInformation("Native authority validation: rechecking game and storage identity; reload={0}.", reloadNativeState);

			CollectionTargetAuthority liveAuthority;
			try
			{
				liveAuthority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			}
			catch (Exception exception) when (exception is CollectionTargetAuthorityException || exception is ArgumentException)
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.TargetChanged,
					"The live game/storage authority no longer matches the target that acquired the mutation reservation.", exception);
			}

			if (!SameAuthority(authority, liveAuthority))
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.TargetChanged,
					"The live game/storage authority changed after the mutation target was resolved. Mutation is blocked until the target is resolved again.");
			}
			Trace.TraceInformation("Native authority validation: storage identity checked at {0} ms.", stopwatch.ElapsedMilliseconds);

			// Target identity alone cannot authorize a second game edition to reuse this
			// game's native files. Check before reload, which may perform crash recovery.
			ValidateWitcher3Counterpart(paths);

			CollectionPhysicalDirectoryIdentity installInfoIdentity;
			try
			{
				installInfoIdentity = CollectionPhysicalDirectoryIdentity.Resolve(paths.InstallInfoPath);
			}
			catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is UnauthorizedAccessException ||
				exception is NotSupportedException || exception is System.Security.SecurityException || exception is System.ComponentModel.Win32Exception)
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.InstallInfoUnavailable,
					"The authoritative InstallInfo directory could not be resolved safely.", exception);
			}

			string installLogPath = Path.Combine(installInfoIdentity.CanonicalPath, "InstallLog.xml");
			Trace.TraceInformation("Native authority validation: physical directories checked at {0} ms.", stopwatch.ElapsedMilliseconds);
			if (reloadNativeState)
			{
				ValidateInstallLog(installLogPath);
				try
				{
					_nativeStateReloader.Reload(installLogPath);
				}
				catch (Exception exception)
				{
					throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.NativeReloadFailed,
						"Authoritative native state could not be reloaded while the physical target reservation was held.", exception);
				}

				// Reload may perform crash recovery; validate its resulting authoritative log before clearing recovery.
				ValidateInstallLog(installLogPath);
				// Recheck after reload: never publish an authority binding from evidence that
				// ceased to be valid while native services/recovery were being refreshed.
				ValidateWitcher3Counterpart(paths);
			}

			Trace.TraceInformation("Native authority validation: native stores checked at {0} ms; validating authority binding.", stopwatch.ElapsedMilliseconds);
			CollectionTargetOwnershipAuthorityBinding binding = _authorityStore.BindOrValidate(authority, installInfoIdentity.StableKey);
			if (mutationLease.RequiresRecovery)
				mutationLease.MarkRecoveryCompleted();
			if (reloadNativeState)
				mutationLease.MarkNativeAuthorityValidated();
			Trace.TraceInformation("Native authority validation: completed in {0} ms.", stopwatch.ElapsedMilliseconds);

			return binding;
		}

		/// <summary>
		/// Requires a live root lease that owns the C4.15 cross-process reservation for the exact target being validated.
		/// </summary>
		private static void ValidateLease(CollectionTargetMutationLease mutationLease, CollectionTargetAuthority authority)
		{
			if (mutationLease == null)
				throw new ArgumentNullException(nameof(mutationLease));
			if (authority == null)
				throw new ArgumentNullException(nameof(authority));
			if (mutationLease.IsDisposed || !mutationLease.IsRoot || !mutationLease.HasCrossProcessReservation ||
				!StringComparer.Ordinal.Equals(mutationLease.TargetFingerprint, authority.Target.Fingerprint))
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.MutationLeaseRequired,
					"Ownership-authority validation requires the live root cross-process mutation reservation for this exact canonical target.");
			}
		}

		/// <summary>
		/// Verifies that target identity did not change between target resolution and mutation-lease acquisition.
		/// </summary>
		private static bool SameAuthority(CollectionTargetAuthority expected, CollectionTargetAuthority actual)
		{
			return actual != null &&
				StringComparer.Ordinal.Equals(expected.Target.Fingerprint, actual.Target.Fingerprint) &&
				StringComparer.OrdinalIgnoreCase.Equals(expected.PhysicalGameKey, actual.PhysicalGameKey) &&
				StringComparer.OrdinalIgnoreCase.Equals(expected.StorageId, actual.StorageId);
		}

		/// <summary>
		/// Fails closed on missing, malformed or future-version InstallLogs before changing the live native service graph.
		/// </summary>
		private static void ValidateInstallLog(string installLogPath)
		{
			if (!InstallLog.HasPersistedLog(installLogPath))
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.InstallLogMissing,
					"The authoritative Game Storage does not contain an authoritative InstallLog.");
			}
			if (!InstallLog.IsLogValid(installLogPath))
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.InstallLogInvalid,
					"The authoritative InstallLog is invalid and cannot be used for Collection mutation.");
			}

			Version version;
			try
			{
				version = InstallLog.ReadVersion(installLogPath);
			}
			catch (Exception exception)
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.InstallLogInvalid,
					"The authoritative InstallLog version could not be read.", exception);
			}

			if (version.Equals(new Version(0, 0, 0, 0)))
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.InstallLogInvalid,
					"The authoritative InstallLog format is not recognized.");
			}
			if (version.CompareTo(InstallLog.CurrentVersion) > 0)
			{
				throw new CollectionTargetOwnershipAuthorityException(CollectionTargetOwnershipAuthorityFailureKind.InstallLogUnsupported,
					"The authoritative InstallLog was created by a newer unsupported NMM version.");
			}
		}

	}
}
