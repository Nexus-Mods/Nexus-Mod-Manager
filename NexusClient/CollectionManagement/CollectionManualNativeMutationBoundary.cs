using System;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Places ordinary native mod mutations under the same canonical physical-target reservation used by Collections.
	/// </summary>
	/// <remarks>
	/// The boundary is configured for the active game after the production service graph has been created. A root native
	/// operation acquires the C4 process/cross-process reservation, reloads authoritative native state while that reservation
	/// is held, then rebinds the already-constructed installer/uninstaller to the refreshed services before mutation begins.
	/// Collection children that already inherit a parent reservation do not use this root boundary.
	/// </remarks>
	internal sealed class CollectionManualNativeMutationBoundary
	{
		private static readonly object SyncRoot = new object();
		private static CollectionManualNativeMutationBoundary s_current;

		private readonly object _owner;
		private readonly Func<Tuple<CollectionTargetAuthority, GameStoragePathSet>> _targetProvider;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly Action<CollectionTargetMutationLease, CollectionTargetAuthority, GameStoragePathSet> _validateAndReload;
		private readonly Action<ModInstallerBase> _rebindNativeOperation;

		/// <summary>Configures ordinary native operations for the current production game/service graph.</summary>
		internal static void Configure(ServiceManager services, GameStorageService gameStorageService)
		{
			if (services == null)
				throw new ArgumentNullException(nameof(services));
			if (gameStorageService == null)
				throw new ArgumentNullException(nameof(gameStorageService));
			if (services.ModManager == null || services.ModManager.GameMode == null)
				throw new InvalidOperationException("Manual native mutation coordination requires the active ModManager and game mode.");

			var targetResolver = new CollectionTargetIdentityResolver(gameStorageService);
			var authorityValidator = new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services);
			var boundary = new CollectionManualNativeMutationBoundary(services,
				() =>
				{
					GameStoragePathSet paths = gameStorageService.FromGameMode(services.ModManager.GameMode);
					return Tuple.Create(targetResolver.Resolve(paths), paths);
				},
				CollectionTargetMutationLeaseManager.Shared,
				(lease, authority, paths) => authorityValidator.ValidateAndReload(lease, authority, paths),
				operation => operation.RebindNativeMutationServices(services.ModManager));

			lock (SyncRoot)
				s_current = boundary;
		}

		/// <summary>Clears the production boundary when the service graph that configured it is being released.</summary>
		internal static void Clear(ServiceManager services)
		{
			if (services == null)
				return;

			lock (SyncRoot)
			{
				if (s_current != null && ReferenceEquals(s_current._owner, services))
					s_current = null;
			}
		}

		/// <summary>Attempts to acquire the configured canonical root boundary for an ordinary native operation.</summary>
		internal static bool TryAcquire(ModInstallerBase operation, out CollectionTargetMutationLease lease)
		{
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));

			CollectionManualNativeMutationBoundary boundary;
			lock (SyncRoot)
				boundary = s_current;

			if (boundary == null)
			{
				lease = null;
				return false;
			}

			lease = boundary.Acquire(operation);
			return true;
		}

		/// <summary>Creates a boundary over explicit seams for deterministic coordination tests.</summary>
		internal CollectionManualNativeMutationBoundary(object owner,
			Func<Tuple<CollectionTargetAuthority, GameStoragePathSet>> targetProvider,
			CollectionTargetMutationLeaseManager mutationLeaseManager,
			Action<CollectionTargetMutationLease, CollectionTargetAuthority, GameStoragePathSet> validateAndReload,
			Action<ModInstallerBase> rebindNativeOperation)
		{
			_owner = owner;
			_targetProvider = targetProvider ?? throw new ArgumentNullException(nameof(targetProvider));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_validateAndReload = validateAndReload ?? throw new ArgumentNullException(nameof(validateAndReload));
			_rebindNativeOperation = rebindNativeOperation ?? throw new ArgumentNullException(nameof(rebindNativeOperation));
		}

		/// <summary>Acquires, reloads and rebinds one root ordinary native operation before it may mutate native state.</summary>
		internal CollectionTargetMutationLease Acquire(ModInstallerBase operation)
		{
			if (operation == null)
				throw new ArgumentNullException(nameof(operation));

			Tuple<CollectionTargetAuthority, GameStoragePathSet> target = _targetProvider();
			if (target == null || target.Item1 == null || target.Item2 == null)
				throw new InvalidOperationException("Manual native mutation coordination could not resolve the active canonical target.");

			CollectionTargetMutationLease lease = _mutationLeaseManager.Acquire(target.Item1);
			bool handedOff = false;
			try
			{
				_validateAndReload(lease, target.Item1, target.Item2);
				_rebindNativeOperation(operation);
				handedOff = true;
				return lease;
			}
			finally
			{
				if (!handedOff && !lease.RequiresRecovery)
					lease.Dispose();
			}
		}
	}
}
