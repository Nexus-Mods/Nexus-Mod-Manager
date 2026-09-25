using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods.Formats.FOMod;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes one user-requested product-level save-current-setup operation.</summary>
	public sealed class CollectionSaveCurrentSetupRequest
	{
		public CollectionSaveCurrentSetupRequest(string displayName, LocalCaptureCapability requestedCapability)
		{
			DisplayName = CollectionDomainValidation.RequireDisplayValue(displayName, nameof(displayName));
			if (!Enum.IsDefined(typeof(LocalCaptureCapability), requestedCapability) || requestedCapability == LocalCaptureCapability.Unknown)
				throw new ArgumentOutOfRangeException(nameof(requestedCapability));
			RequestedCapability = requestedCapability;
		}

		public string DisplayName { get; }
		public LocalCaptureCapability RequestedCapability { get; }
	}

	/// <summary>Classifies the product-level outcome of saving the current setup.</summary>
	public enum CollectionSaveCurrentSetupState
	{
		NotSealed = 1,
		Saved = 2
	}

	/// <summary>Immutable result exposed to the UI after one C7.8 save-current-setup operation.</summary>
	public sealed class CollectionSaveCurrentSetupResult
	{
		private readonly ReadOnlyCollection<CollectionCaptureSealIssue> _issues;

		internal CollectionSaveCurrentSetupResult(CollectionSaveCurrentSetupState state,
			LocalCaptureCapability requestedCapability, CollectionDefinition definition, CollectionRevision revision,
			LocalCapture capture, string packageArtifactId, IEnumerable<CollectionCaptureSealIssue> issues)
		{
			State = state;
			RequestedCapability = requestedCapability;
			Definition = definition;
			Revision = revision;
			Capture = capture;
			PackageArtifactId = packageArtifactId;
			_issues = new ReadOnlyCollection<CollectionCaptureSealIssue>((issues ?? Enumerable.Empty<CollectionCaptureSealIssue>()).ToList());
		}

		public CollectionSaveCurrentSetupState State { get; }
		public LocalCaptureCapability RequestedCapability { get; }
		public CollectionDefinition Definition { get; }
		public CollectionRevision Revision { get; }
		public LocalCapture Capture { get; }
		public string PackageArtifactId { get; }
		public ReadOnlyCollection<CollectionCaptureSealIssue> Issues { get { return _issues; } }
		public bool IsSaved { get { return State == CollectionSaveCurrentSetupState.Saved && Capture != null; } }
		public LocalCaptureCapability? SavedCapability { get { return Capture == null ? (LocalCaptureCapability?)null : Capture.Capability; } }
	}

	/// <summary>
	/// C7.8 product-level application service that saves the active NMM-managed setup as one sealed Local Collection.
	/// </summary>
	/// <remarks>
	/// Heavy snapshot hashing and retained-content copies are always scheduled away from the WinForms UI thread. The service
	/// captures and persists Collections metadata only; it never mutates native deployment, plugins, INI/game values or profiles.
	/// </remarks>
	public sealed class CollectionLocalCaptureApplicationService
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;

		public CollectionLocalCaptureApplicationService(ServiceManager services, GameStorageService gameStorageService)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			if (_services.ModManager == null)
				throw new InvalidOperationException("Saving a Local Collection requires the active native ModManager.");
			if (_services.ModRepository == null)
				throw new InvalidOperationException("Saving a Local Collection requires the active mod repository identity service.");
		}

		/// <summary>
		/// Saves the current setup without running capture/hash/copy work on the caller's WinForms synchronization context.
		/// </summary>
		public Task<CollectionSaveCurrentSetupResult> SaveCurrentSetupAsync(CollectionSaveCurrentSetupRequest request,
			CancellationToken cancellationToken)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			return Task.Run(() => SaveCurrentSetup(request, cancellationToken), cancellationToken);
		}

		internal CollectionSaveCurrentSetupResult SaveCurrentSetup(CollectionSaveCurrentSetupRequest request,
			CancellationToken cancellationToken)
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));
			cancellationToken.ThrowIfCancellationRequested();

			GameStoragePathSet paths = _gameStorageService.FromGameMode(_services.ModManager.GameMode);
			var store = new CollectionsStore(paths);
			EnsureStoreAvailable(store);
			var associationStore = new CollectionsAssociationStore(store);
			var artifactStore = new CollectionsRetainedArtifactStore(store);
			var referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
			var localCaptureStore = new CollectionsLocalCaptureStore(store);
			CollectionTargetIdentity target = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths).Target;

			CollectionIdentity collectionIdentity = CollectionIdentity.FromLocal(Guid.NewGuid());
			CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromLocal(collectionIdentity, Guid.NewGuid());
			LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.NewGuid());
			var definition = new CollectionDefinition(collectionIdentity, request.DisplayName, null,
				"Saved from the current NMM-managed setup.");

			bool persisted = false;
			try
			{
				var nativeStateReader = new NativeStateCaptureReader(_services.ModManager.InstallationLog,
					_services.ModManager.VirtualModActivator, _services.ModManager.DeploymentManager,
					_services.PluginManager, _services.ModManager.GameMode);
				var installedIdentityReader = new CollectionInstalledIdentityCaptureReader(nativeStateReader,
					associationStore, _services.ModRepository.GameDomainName);
				var ownerPayloadCapture = new CollectionOwnerPayloadCaptureService(nativeStateReader, artifactStore, referenceStore);
				var scriptedReplayCapture = new CollectionScriptedReplayCaptureService(nativeStateReader,
					installedIdentityReader, artifactStore, referenceStore);
				var nativeEffectCapture = new CollectionNativeEffectCaptureReader(nativeStateReader);
				FOModFormat fomodFormat = _services.ModManager.ModFormats.OfType<FOModFormat>().FirstOrDefault();
				var userMetadataCapture = new CollectionUserMetadataCaptureService(nativeStateReader, installedIdentityReader,
					_services.ModManager.SortOrderService, fomodFormat == null ? null : fomodFormat.UserMetadataReader,
					artifactStore, referenceStore);
				var nativeIndexReader = new CollectionNativeStateReader(_services.ModManager.InstallationLog,
					_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, associationStore);

				NativeStateCaptureSnapshot nativeState = nativeStateReader.Capture();
				CollectionNativeStateIndex capturedIndex = nativeIndexReader.Capture(target, nativeState);
				CollectionInstalledIdentitySnapshot installedIdentities = installedIdentityReader.Capture(target, nativeState);
				CollectionRevision revision = new CollectionRevision(revisionIdentity, "Captured setup", null,
					installedIdentities.Mods.Count);
				IReadOnlyList<LocalCaptureNativeRecordMapping> mappings = CreateNativeMappings(captureIdentity, target,
					installedIdentities.Mods);

				CollectionOwnerPayloadSnapshot ownerPayloads = ownerPayloadCapture.Capture(target, captureIdentity,
					nativeState, cancellationToken);
				CollectionScriptedReplaySnapshot scriptedReplay = scriptedReplayCapture.Capture(target, captureIdentity,
					nativeState, installedIdentities, cancellationToken);
				CollectionNativeEffectSnapshot nativeEffects = nativeEffectCapture.Capture(target, nativeState);
				CollectionUserMetadataSnapshot userMetadata = userMetadataCapture.Capture(target, captureIdentity,
					nativeState, installedIdentities, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();

				CollectionNativeStateIndex sealingIndex = nativeIndexReader.Capture(target);
				var scope = new LocalCaptureScope(LocalCaptureScope.CurrentVersion, Enum.GetValues(typeof(LocalCaptureScopeArea))
					.Cast<LocalCaptureScopeArea>().Where(x => x != LocalCaptureScopeArea.Unknown));
				var sealRequest = new CollectionCaptureSealRequest(captureIdentity, revisionIdentity, target,
					capturedIndex.Fingerprint, sealingIndex.Fingerprint, scope, request.RequestedCapability,
					installedIdentities, ownerPayloads, scriptedReplay, nativeEffects, userMetadata,
					new CollectionCapturedArchiveArtifact[0], new LocalCaptureExclusion[0], mappings);
				var sealer = new CollectionCaptureSealer(artifactStore, referenceStore);
				CollectionCaptureSealResult sealResult = sealer.Seal(sealRequest, cancellationToken);
				if (!sealResult.IsSealed)
					return new CollectionSaveCurrentSetupResult(CollectionSaveCurrentSetupState.NotSealed,
						request.RequestedCapability, null, null, null, null, sealResult.Issues);

				byte[] packageBytes = CollectionLocalCapturePackageCodec.Serialize(sealResult);
				CollectionLocalCapturePackageInspection packageInspection = CollectionLocalCapturePackageCodec.Inspect(packageBytes);
				ValidatePackageIdentity(sealResult.SealedCapture.Capture, packageInspection);
				CollectionsRetainedArtifact packageArtifact;
				using (var packageStream = new MemoryStream(packageBytes, false))
					packageArtifact = artifactStore.Publish(packageStream, cancellationToken);
				referenceStore.AcquireExclusiveRoleReference(packageArtifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(),
					CollectionsLocalCaptureStore.PackageReferenceRole);
				localCaptureStore.Save(definition, revision, sealResult.SealedCapture.Capture,
					CollectionLocalCapturePackageCodec.CurrentFormatVersion, packageArtifact.ArtifactId);
				persisted = true;

				return new CollectionSaveCurrentSetupResult(CollectionSaveCurrentSetupState.Saved,
					request.RequestedCapability, definition, revision, sealResult.SealedCapture.Capture,
					packageArtifact.ArtifactId, sealResult.Issues);
			}
			finally
			{
				if (!persisted)
				{
					try
					{
						referenceStore.ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind.Capture,
							captureIdentity.ToString());
					}
					catch
					{
						// Preserve the primary capture/sealing failure; unreferenced cleanup is a separate recovery boundary.
					}
				}
			}
		}

		private static void EnsureStoreAvailable(CollectionsStore store)
		{
			if (!store.Exists)
			{
				try
				{
					store.CreateNew();
					return;
				}
				catch (IOException)
				{
					if (!store.Exists)
						throw;
				}
			}
			store.OpenExisting();
		}

		private static IReadOnlyList<LocalCaptureNativeRecordMapping> CreateNativeMappings(LocalCaptureIdentity captureIdentity,
			CollectionTargetIdentity target, IEnumerable<CollectionInstalledModIdentity> mods)
		{
			var result = new List<LocalCaptureNativeRecordMapping>();
			foreach (CollectionInstalledModIdentity mod in mods.OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase))
			{
				Guid memberId = CreateMemberIdentity(captureIdentity, mod.NativeSnapshotKey);
				result.Add(new LocalCaptureNativeRecordMapping(CollectionMemberKey.FromLocal(memberId),
					new NativeModInstanceIdentity(target, mod.NativeSnapshotKey)));
			}
			return result;
		}

		private static Guid CreateMemberIdentity(LocalCaptureIdentity captureIdentity, string nativeSnapshotKey)
		{
			string payload = "nmmce-local-capture-member-v1\0" + captureIdentity + "\0" +
				(nativeSnapshotKey ?? String.Empty).ToLowerInvariant();
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
				byte[] guidBytes = new byte[16];
				Buffer.BlockCopy(digest, 0, guidBytes, 0, guidBytes.Length);
				Guid result = new Guid(guidBytes);
				if (result == Guid.Empty)
					throw new InvalidOperationException("The deterministic Local Collection member identity unexpectedly resolved to an empty GUID.");
				return result;
			}
		}

		private static void ValidatePackageIdentity(LocalCapture capture, CollectionLocalCapturePackageInspection inspection)
		{
			if (!capture.Identity.Equals(inspection.CaptureIdentity) || capture.Capability != inspection.Capability ||
				capture.SchemaVersion != inspection.CaptureSchemaVersion || capture.CapabilityVersion != inspection.CapabilityVersion ||
				!capture.SourceTarget.Equals(inspection.SourceTarget))
				throw new InvalidDataException("The serialized Local Collection package header does not match the sealed capture contract.");
		}
	}
}
