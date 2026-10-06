using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>One installed Collection association exposed by the basic management application route.</summary>
	public sealed class CollectionManagementAssociation
	{
		internal CollectionManagementAssociation(CollectionTargetAssociation association, string displayName, string revisionLabel)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			DisplayName = String.IsNullOrWhiteSpace(displayName) ? association.Revision.Collection.StableId : displayName;
			RevisionLabel = String.IsNullOrWhiteSpace(revisionLabel) ? FormatRevision(association.Revision) : revisionLabel;
		}

		public CollectionTargetAssociation Association { get; }
		public Guid AssociationId { get { return Association.AssociationId; } }
		public string DisplayName { get; }
		public string RevisionLabel { get; }
		public CollectionAssociationState State { get { return Association.State; } }

		public override string ToString()
		{
			return DisplayName + " - " + RevisionLabel + " [" + State + "]";
		}

		private static string FormatRevision(CollectionRevisionIdentity revision)
		{
			if (revision.NexusRevisionNumber.HasValue)
				return "Revision " + revision.NexusRevisionNumber.Value;
			return "Local revision " + revision.StableRevisionId;
		}
	}

	/// <summary>
	/// Read-only C9 presentation of one installed Collection member and the provenance/customization that protects it.
	/// </summary>
	public sealed class CollectionManagementMemberPresentation
	{
		private readonly ReadOnlyCollection<UserOverride> _userOverrides;
		private readonly ReadOnlyCollection<CollectionDriftObservation> _driftObservations;

		internal CollectionManagementMemberPresentation(CollectionMemberBinding binding, NativeModProvenance provenance,
			int collectionAssociationCount, IEnumerable<UserOverride> userOverrides,
			IEnumerable<CollectionDriftObservation> driftObservations)
		{
			Binding = binding ?? throw new ArgumentNullException(nameof(binding));
			Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
			if (!Binding.NativeMod.Equals(Provenance.NativeMod))
				throw new ArgumentException("The member presentation provenance must describe the bound native mod.", nameof(provenance));
			if (collectionAssociationCount < 1)
				throw new ArgumentOutOfRangeException(nameof(collectionAssociationCount));

			List<UserOverride> overrides = (userOverrides ?? throw new ArgumentNullException(nameof(userOverrides))).ToList();
			List<CollectionDriftObservation> drift = (driftObservations ?? throw new ArgumentNullException(nameof(driftObservations))).ToList();
			if (overrides.Any(x => x == null || x.Requirement.AssociationId != binding.Association.AssociationId ||
				x.Requirement.MemberKey == null || !x.Requirement.MemberKey.Equals(binding.MemberKey)))
				throw new ArgumentException("Every member presentation override must belong to the exact Collection member binding.", nameof(userOverrides));
			if (drift.Any(x => x == null || x.Requirement.AssociationId != binding.Association.AssociationId ||
				x.Requirement.MemberKey == null || !x.Requirement.MemberKey.Equals(binding.MemberKey)))
				throw new ArgumentException("Every member presentation drift observation must belong to the exact Collection member binding.", nameof(driftObservations));

			CollectionAssociationCount = collectionAssociationCount;
			_userOverrides = new ReadOnlyCollection<UserOverride>(overrides);
			_driftObservations = new ReadOnlyCollection<CollectionDriftObservation>(drift);
		}

		public CollectionMemberBinding Binding { get; }
		public CollectionMemberKey MemberKey { get { return Binding.MemberKey; } }
		public NativeModInstanceIdentity NativeMod { get { return Binding.NativeMod; } }
		public NativeModProvenance Provenance { get; }
		public int CollectionAssociationCount { get; }
		public bool IsSharedAcrossCollections { get { return CollectionAssociationCount > 1; } }
		public ReadOnlyCollection<UserOverride> UserOverrides { get { return _userOverrides; } }
		public ReadOnlyCollection<CollectionDriftObservation> DriftObservations { get { return _driftObservations; } }
		public bool HasExplicitLocalDecision { get { return _userOverrides.Count > 0; } }
		public bool HasDetectedDrift { get { return _driftObservations.Count > 0; } }
	}

	/// <summary>Detailed C9 pin/provenance impact for one installed Collection member.</summary>
	public sealed class CollectionManagementMemberImpact
	{
		private readonly ReadOnlyCollection<CollectionMemberPinImpact> _pins;

		internal CollectionManagementMemberImpact(CollectionMemberBinding selectedBinding, NativeModProvenance provenance,
			IEnumerable<CollectionMemberPinImpact> pins)
		{
			SelectedBinding = selectedBinding ?? throw new ArgumentNullException(nameof(selectedBinding));
			Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
			if (!SelectedBinding.NativeMod.Equals(Provenance.NativeMod))
				throw new ArgumentException("The member-impact provenance must describe the selected native mod.", nameof(provenance));
			List<CollectionMemberPinImpact> copied = (pins ?? throw new ArgumentNullException(nameof(pins))).ToList();
			if (!copied.Any(x => x.Binding.Association.AssociationId == SelectedBinding.Association.AssociationId &&
				x.Binding.MemberKey.Equals(SelectedBinding.MemberKey)))
				throw new ArgumentException("The member-impact pin set must contain the selected Collection binding.", nameof(pins));
			_pins = new ReadOnlyCollection<CollectionMemberPinImpact>(copied);
		}

		public CollectionMemberBinding SelectedBinding { get; }
		public NativeModProvenance Provenance { get; }
		public IReadOnlyList<CollectionMemberPinImpact> Pins { get { return _pins; } }
		public int CollectionAssociationCount { get { return _pins.Select(x => x.Binding.Association.AssociationId).Distinct().Count(); } }
		public bool IsSharedAcrossCollections { get { return CollectionAssociationCount > 1; } }
		public bool StandaloneUseProtectsFromAutomaticRemoval { get { return Provenance.StandaloneUseProtectsFromAutomaticRemoval; } }
		public bool HasAnyOverride { get { return _pins.Any(x => x.HasExplicitLocalDecision); } }
		public bool HasAnyDrift { get { return _pins.Any(x => x.HasDetectedDrift); } }
	}

	/// <summary>
	/// Read-only durable presentation data for one installed Collection association.
	/// </summary>
	/// <remarks>
	/// The installed association remains authoritative for applied state. The retained manifest is re-normalized only to
	/// rebuild the read-only Collection details/member list after restart; it does not create a new preparation or mutation.
	/// </remarks>
	public sealed class CollectionManagementAssociationPresentation
	{
		internal CollectionManagementAssociationPresentation(CollectionManagementAssociation association,
			CollectionDefinition definition, CollectionRevision revision, NexusCollectionBundleImportResult retainedManifest,
			IEnumerable<CollectionManagementMemberPresentation> members, CollectionAssociationCustomization customization,
			string nexusGameDomain, string retainedSourceIssue)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			Definition = definition;
			Revision = revision;
			RetainedManifest = retainedManifest;
			List<CollectionManagementMemberPresentation> copiedMembers = (members ?? Enumerable.Empty<CollectionManagementMemberPresentation>()).ToList();
			if (copiedMembers.Any(x => x == null || x.Binding.Association.AssociationId != association.AssociationId))
				throw new ArgumentException("Every managed member presentation must belong to the exact installed Collection association.", nameof(members));
			Members = new ReadOnlyCollection<CollectionManagementMemberPresentation>(copiedMembers);
			BoundMemberKeys = new ReadOnlyCollection<CollectionMemberKey>(copiedMembers.Select(x => x.MemberKey).ToList());
			Customization = customization ?? new CollectionAssociationCustomization(association.Association,
				Enumerable.Empty<UserOverride>(), Enumerable.Empty<CollectionDriftObservation>());
			if (Customization.AssociationId != association.AssociationId)
				throw new ArgumentException("The managed Collection customization snapshot must belong to the presented association.", nameof(customization));
			NexusGameDomain = String.IsNullOrWhiteSpace(nexusGameDomain) ? null : nexusGameDomain.Trim().ToLowerInvariant();
			RetainedSourceIssue = retainedSourceIssue;
		}

		public CollectionManagementAssociation Association { get; }
		public CollectionDefinition Definition { get; }
		public CollectionRevision Revision { get; }
		public NexusCollectionBundleImportResult RetainedManifest { get; }
		public IReadOnlyList<CollectionManagementMemberPresentation> Members { get; }
		public IReadOnlyList<CollectionMemberKey> BoundMemberKeys { get; }
		public CollectionAssociationCustomization Customization { get; }
		public string NexusGameDomain { get; }
		public string RetainedSourceIssue { get; }
		public bool HasRetainedManifest { get { return RetainedManifest != null; } }
	}

	/// <summary>One safe Collection-defined launcher exposed for the current installed association set.</summary>
	public sealed class CollectionManagementTool
	{
		internal CollectionManagementTool(CollectionTargetAssociation association, string collectionDisplayName,
			CollectionLaunchTool tool, string executablePath, string workingDirectory, bool canLaunch)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			CollectionDisplayName = String.IsNullOrWhiteSpace(collectionDisplayName) ? association.Revision.Collection.StableId : collectionDisplayName;
			Tool = tool ?? throw new ArgumentNullException(nameof(tool));
			ExecutablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
			WorkingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
			CanLaunch = canLaunch;
		}

		public CollectionTargetAssociation Association { get; }
		public Guid AssociationId { get { return Association.AssociationId; } }
		public string CollectionDisplayName { get; }
		public CollectionLaunchTool Tool { get; }
		public string Name { get { return Tool.Name; } }
		public string ExecutablePath { get; }
		public string WorkingDirectory { get; }
		public bool CanLaunch { get; }
	}

	/// <summary>One persisted Local Collection capture available to the active target for explicit restore.</summary>
	public sealed class CollectionManagementLocalCapture
	{
		internal CollectionManagementLocalCapture(LocalCapture capture, string displayName, string revisionLabel)
		{
			Capture = capture ?? throw new ArgumentNullException(nameof(capture));
			DisplayName = String.IsNullOrWhiteSpace(displayName) ? capture.Revision.Collection.StableId : displayName;
			RevisionLabel = String.IsNullOrWhiteSpace(revisionLabel) ? "Local revision " + capture.Revision.StableRevisionId : revisionLabel;
		}

		public LocalCapture Capture { get; }
		public LocalCaptureIdentity CaptureIdentity { get { return Capture.Identity; } }
		public string DisplayName { get; }
		public string RevisionLabel { get; }
		public LocalCaptureCapability Capability { get { return Capture.Capability; } }

		public override string ToString()
		{
			return DisplayName + " - " + RevisionLabel + " [" + Capability + "]";
		}
	}

	/// <summary>Durable source binding for one interrupted Local Collection restore operation.</summary>
	public sealed class CollectionManagementLocalRestoreRecoverySource
	{
		internal CollectionManagementLocalRestoreRecoverySource(CollectionOperation operation, LocalCaptureIdentity captureIdentity,
			string displayName, string revisionLabel, string diagnostic)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			CaptureIdentity = captureIdentity;
			DisplayName = displayName ?? String.Empty;
			RevisionLabel = revisionLabel ?? String.Empty;
			Diagnostic = diagnostic ?? String.Empty;
		}

		public CollectionOperation Operation { get; }
		public CollectionOperationIdentity OperationIdentity { get { return Operation.Identity; } }
		public LocalCaptureIdentity CaptureIdentity { get; }
		public string DisplayName { get; }
		public string RevisionLabel { get; }
		public string Diagnostic { get; }
		public bool HasResolvedCapture { get { return CaptureIdentity != null && !String.IsNullOrWhiteSpace(DisplayName); } }

		public string SourceDisplayName
		{
			get
			{
				if (!HasResolvedCapture) return "Unknown Local Collection";
				return String.IsNullOrWhiteSpace(RevisionLabel) ? DisplayName : DisplayName + " - " + RevisionLabel;
			}
		}
	}

	/// <summary>One mutable Local Collection working copy cloned from an exact retained source revision.</summary>
	public sealed class CollectionManagementLocalWorkingCopy
	{
		internal CollectionManagementLocalWorkingCopy(CollectionLocalWorkingCopyRecord record, CollectionDefinition definition,
			CollectionDefinition sourceDefinition, CollectionRevision sourceRevision)
		{
			Record = record ?? throw new ArgumentNullException(nameof(record));
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			if (!Definition.Identity.Equals(Record.Collection))
				throw new ArgumentException("The Local working-copy definition must match the durable working-copy identity.", nameof(definition));
			SourceDefinition = sourceDefinition;
			SourceRevision = sourceRevision ?? throw new ArgumentNullException(nameof(sourceRevision));
			if (!SourceRevision.Identity.Equals(Record.SourceRevision))
				throw new ArgumentException("The Local working-copy source revision metadata must match the durable source identity.", nameof(sourceRevision));
		}

		public CollectionLocalWorkingCopyRecord Record { get; }
		public CollectionDefinition Definition { get; }
		public CollectionDefinition SourceDefinition { get; }
		public CollectionRevision SourceRevision { get; }
		public CollectionIdentity Collection { get { return Record.Collection; } }
		public string DisplayName { get { return String.IsNullOrWhiteSpace(Definition.DisplayName) ? Collection.StableId : Definition.DisplayName; } }
		public string SourceDisplayName { get { return SourceDefinition == null || String.IsNullOrWhiteSpace(SourceDefinition.DisplayName) ? Record.SourceRevision.Collection.StableId : SourceDefinition.DisplayName; } }
		public bool HasRetainedBundle { get { return !String.IsNullOrEmpty(Record.BaseSource.RawBundleArtifactId); } }

		public override string ToString()
		{
			string revision = SourceRevision.Identity.NexusRevisionNumber.HasValue
				? "Revision " + SourceRevision.Identity.NexusRevisionNumber.Value
				: (String.IsNullOrWhiteSpace(SourceRevision.RevisionLabel) ? "Local revision " + SourceRevision.Identity.StableRevisionId : SourceRevision.RevisionLabel);
			return DisplayName + " [working copy of " + SourceDisplayName + " - " + revision + "]";
		}
	}

	/// <summary>
	/// Application-level route for basic installed-Collection management over the existing C6.13/C6.14 coordinators.
	/// </summary>
	/// <remarks>
	/// This service exposes basic installed-association management, saved Local Collection restore, and bounded startup reconciliation.
	/// Native removal semantics remain owned by <see cref="CollectionUninstallEffectsCoordinator"/>; complete Local restore sequencing
	/// remains owned by <see cref="CollectionLocalRestoreApplicationService"/>.
	/// </remarks>
	public sealed class CollectionManagementApplicationService
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsStore _store;
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionUninstallEffectsCoordinator _uninstallCoordinator;
		private readonly CollectionInstalledMemberRemovalCoordinator _memberRemovalCoordinator;
		private readonly CollectionsLocalCaptureStore _localCaptureStore;
		private readonly CollectionsLocalWorkingCopyStore _localWorkingCopyStore;
		private readonly CollectionLocalWorkingCopyEditor _localWorkingCopyEditor;
		private readonly CollectionLocalRestoreMemberResumeCoordinator _localRestoreResumeCoordinator;
		private readonly CollectionLocalRestoreApplicationService _localRestoreWorkflow;
		private readonly CollectionReplacementRecoveryCoordinator _replacementRecovery;

		/// <summary>Creates the production management route for the active game/storage target.</summary>
		public CollectionManagementApplicationService(ServiceManager services, GameStorageService gameStorageService)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			if (_services.ModManager == null)
				throw new InvalidOperationException("Collection management requires the active native ModManager.");

			_store = new CollectionsStore(GetTargetPaths());
			_catalogStore = new CollectionsCatalogStore(_store);
			_associationStore = new CollectionsAssociationStore(_store);
			_operationStore = new CollectionsOperationStore(_store);
			_revisionSourceStore = new CollectionsRevisionSourceStore(_store);
			_uninstallCoordinator = new CollectionUninstallEffectsCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore);
			_memberRemovalCoordinator = new CollectionInstalledMemberRemovalCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore);
			_localCaptureStore = new CollectionsLocalCaptureStore(_store);
			_localWorkingCopyStore = new CollectionsLocalWorkingCopyStore(_store);
			_localWorkingCopyEditor = new CollectionLocalWorkingCopyEditor(_store);
			var artifactStore = new CollectionsRetainedArtifactStore(_store);
			var referenceStore = new CollectionsRetainedArtifactReferenceStore(_store);
			_localRestoreResumeCoordinator = new CollectionLocalRestoreMemberResumeCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore, _localCaptureStore, artifactStore, referenceStore);
			_localRestoreWorkflow = new CollectionLocalRestoreApplicationService(_services, _gameStorageService, _operationStore,
				_associationStore, _localCaptureStore, artifactStore, referenceStore);
			var planStore = new CollectionsResolvedPlanStore(_store);
			var manifestStore = new CollectionsNativeChildRecoveryManifestStore(artifactStore, referenceStore);
			_replacementRecovery = new CollectionReplacementRecoveryCoordinator(_services, _gameStorageService, _operationStore,
				planStore, _associationStore, artifactStore, referenceStore, manifestStore);
		}

		/// <summary>Returns installed Collection associations for the current canonical target.</summary>
		public IReadOnlyList<CollectionManagementAssociation> GetAssociations()
		{
			if (!_store.Exists)
				return new CollectionManagementAssociation[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var result = new List<CollectionManagementAssociation>();
			foreach (CollectionTargetAssociation association in _associationStore.GetAssociationsForTarget(target))
			{
				CollectionDefinition definition = _catalogStore.GetDefinition(association.Revision.Collection);
				CollectionRevision revision = _catalogStore.GetRevision(association.Revision);
				result.Add(new CollectionManagementAssociation(association,
					definition == null ? null : definition.DisplayName,
					revision == null ? null : revision.RevisionLabel));
			}
			return new ReadOnlyCollection<CollectionManagementAssociation>(result
				.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(x => x.RevisionLabel, StringComparer.CurrentCultureIgnoreCase).ToList());
		}

		/// <summary>Returns safe Collection-defined launchers for Applied/Modified associations on the current target.</summary>
		/// <remarks>
		/// The launcher set is derived from retained immutable manifests rather than persisted independently. Missing executables
		/// remain visible as disabled entries, matching the fact that a Collection may describe a tool supplied outside its own members.
		/// </remarks>
		public IReadOnlyList<CollectionManagementTool> GetCollectionTools()
		{
			if (!_store.Exists)
				return new CollectionManagementTool[0];

			CollectionTargetIdentity target = ResolveCurrentTarget();
			string gameRoot = GetCollectionToolGameRoot();
			var result = new List<CollectionManagementTool>();
			foreach (CollectionTargetAssociation association in _associationStore.GetAssociationsForTarget(target)
				.Where(x => x.State == CollectionAssociationState.Applied || x.State == CollectionAssociationState.Modified))
			{
				try
				{
					CollectionRevision revision = _catalogStore.GetRevision(association.Revision);
					if (revision == null)
						continue;
					CollectionRevisionSourceRecord source = _revisionSourceStore.GetSource(revision.Identity);
					if (source == null)
						continue;
					byte[] rawManifest = _revisionSourceStore.LoadManifest(revision.Identity, source.ManifestSource);
					NormalizedCollectionManifest manifest = new NexusCollectionManifestNormalizer().Normalize(rawManifest, revision).Manifest;
					CollectionDefinition definition = _catalogStore.GetDefinition(association.Revision.Collection);
					string collectionName = definition == null ? null : definition.DisplayName;
					foreach (CollectionLaunchTool tool in manifest.LaunchTools)
					{
						ProcessStartInfo startInfo = CollectionToolLaunchPolicy.CreateStartInfo(tool, gameRoot);
						string executable = startInfo.FileName;
						string workingDirectory = startInfo.WorkingDirectory;
						bool canLaunch = File.Exists(executable) && Directory.Exists(workingDirectory);
						result.Add(new CollectionManagementTool(association, collectionName, tool, executable, workingDirectory, canLaunch));
					}
				}
				catch (Exception ex)
				{
					Trace.TraceWarning("Could not reconstruct Collection tools for association {0}: {1}", association.AssociationId, ex.Message);
				}
			}

			return new ReadOnlyCollection<CollectionManagementTool>(result
				.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(x => x.CollectionDisplayName, StringComparer.CurrentCultureIgnoreCase).ToList());
		}

		/// <summary>Launches one currently authorized Collection tool after revalidating its durable association and exact retained definition.</summary>
		public void LaunchCollectionTool(CollectionManagementTool requestedTool)
		{
			if (requestedTool == null)
				throw new ArgumentNullException(nameof(requestedTool));

			CollectionTargetIdentity target = ResolveCurrentTarget();
			CollectionTargetAssociation association = _associationStore.GetAssociation(requestedTool.AssociationId);
			if (association == null || !association.Target.Equals(target) ||
				(association.State != CollectionAssociationState.Applied && association.State != CollectionAssociationState.Modified))
				throw new InvalidOperationException("The Collection tool is no longer owned by an applied Collection association on this target.");

			CollectionRevision revision = _catalogStore.GetRevision(association.Revision);
			CollectionRevisionSourceRecord source = revision == null ? null : _revisionSourceStore.GetSource(revision.Identity);
			if (revision == null || source == null)
				throw new InvalidOperationException("The exact retained Collection tool definition is no longer available.");
			byte[] rawManifest = _revisionSourceStore.LoadManifest(revision.Identity, source.ManifestSource);
			NormalizedCollectionManifest manifest = new NexusCollectionManifestNormalizer().Normalize(rawManifest, revision).Manifest;
			CollectionLaunchTool currentTool = manifest.LaunchTools.FirstOrDefault(x => x.Equals(requestedTool.Tool));
			if (currentTool == null)
				throw new InvalidOperationException("The Collection tool definition changed and must be reviewed again before launch.");

			string gameRoot = GetCollectionToolGameRoot();
			ProcessStartInfo startInfo = CollectionToolLaunchPolicy.CreateStartInfo(currentTool, gameRoot);
			string executable = startInfo.FileName;
			string workingDirectory = startInfo.WorkingDirectory;
			if (!File.Exists(executable))
				throw new FileNotFoundException("The Collection tool executable was not found.", executable);
			if (!Directory.Exists(workingDirectory))
				throw new DirectoryNotFoundException("The Collection tool working directory was not found: " + workingDirectory);

			if (Process.Start(startInfo) == null)
				throw new InvalidOperationException("Windows did not create a process for the selected Collection tool.");
		}

		/// <summary>
		/// Rehydrates one installed Collection association for read-only presentation from durable catalog/retained source data.
		/// </summary>
		/// <remarks>
		/// This method performs no provider/network access and no native mutation. Failure to reload the retained manifest is
		/// returned as a presentation issue so the durable association can still be displayed and managed.
		/// </remarks>
		public CollectionManagementAssociationPresentation GetAssociationPresentation(Guid associationId)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (!_store.Exists)
				return null;

			CollectionTargetIdentity target = ResolveCurrentTarget();
			CollectionsAssociationTargetSnapshot targetSnapshot = _associationStore.GetTargetSnapshot(target);
			CollectionTargetAssociation association = targetSnapshot.Associations
				.FirstOrDefault(x => x.AssociationId == associationId);
			if (association == null)
				return null;

			CollectionDefinition definition = _catalogStore.GetDefinition(association.Revision.Collection);
			CollectionRevision revision = _catalogStore.GetRevision(association.Revision);
			var managedAssociation = new CollectionManagementAssociation(association,
				definition == null ? null : definition.DisplayName,
				revision == null ? null : revision.RevisionLabel);

			NexusCollectionBundleImportResult retainedManifest = null;
			string retainedSourceIssue = null;
			if (revision == null)
			{
				retainedSourceIssue = "The installed Collection revision metadata is missing from the durable Collections catalog.";
			}
			else
			{
				try
				{
					CollectionRevisionSourceRecord source = _revisionSourceStore.GetSource(revision.Identity);
					if (source == null)
					{
						retainedSourceIssue = "The exact retained Collection manifest is not available for this installed revision.";
					}
					else
					{
						byte[] rawManifest = _revisionSourceStore.LoadManifest(revision.Identity, source.ManifestSource);
						NexusCollectionManifestNormalizationResult normalized = new NexusCollectionManifestNormalizer().Normalize(rawManifest, revision);
						retainedManifest = new NexusCollectionBundleImportResult(
							ToBundleInputKind(source.InputKind), source.BundleContentHash, source.BundleByteLength,
							source.ManifestEntryName, normalized);
					}
				}
				catch (Exception ex)
				{
					retainedSourceIssue = ex.Message;
				}
			}

			List<CollectionMemberBinding> bindings = targetSnapshot.Bindings
				.Where(x => x.Association.AssociationId == associationId)
				.OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal).ToList();
			List<UserOverride> overrides = targetSnapshot.Overrides
				.Where(x => x.Requirement.AssociationId == associationId).ToList();
			List<CollectionDriftObservation> drift = targetSnapshot.DriftObservations
				.Where(x => x.Requirement.AssociationId == associationId).ToList();
			var provenanceByNative = targetSnapshot.NativeModProvenance
				.ToDictionary(x => x.NativeMod, x => x);
			var associationCountsByNative = targetSnapshot.Bindings
				.GroupBy(x => x.NativeMod)
				.ToDictionary(x => x.Key, x => x.Select(y => y.Association.AssociationId).Distinct().Count());
			var memberPresentations = new List<CollectionManagementMemberPresentation>();
			foreach (CollectionMemberBinding binding in bindings)
			{
				NativeModProvenance provenance;
				if (!provenanceByNative.TryGetValue(binding.NativeMod, out provenance))
					provenance = new NativeModProvenance(binding.NativeMod, StandaloneModUse.Unknown);
				int associationCount;
				if (!associationCountsByNative.TryGetValue(binding.NativeMod, out associationCount))
					associationCount = 1;
				memberPresentations.Add(new CollectionManagementMemberPresentation(binding, provenance, associationCount,
					overrides.Where(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(binding.MemberKey)),
					drift.Where(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(binding.MemberKey))));
			}
			CollectionAssociationCustomization customization = new CollectionAssociationCustomization(association, overrides, drift);
			string gameDomain = _services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName;
			return new CollectionManagementAssociationPresentation(managedAssociation, definition, revision, retainedManifest,
				memberPresentations, customization, gameDomain, retainedSourceIssue);
		}

		/// <summary>Builds the read-only C10.10a verify/repair assessment for one installed Collection association.</summary>
		/// <remarks>
		/// The target authority is reloaded under the normal target lease before capture. This method never changes native state,
		/// Collection customization, or the association. Qualified repair remains a separate explicit command over this exact preview.
		/// </remarks>
		public async Task<CollectionVerifyRepairPlan> PreviewVerifyRepairAsync(Guid associationId, CancellationToken cancellationToken)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (!_store.Exists)
				return null;

			CollectionManagementAssociationPresentation presentation = GetAssociationPresentation(associationId);
			if (presentation == null)
				return null;
			if (!presentation.HasRetainedManifest)
				throw new InvalidOperationException(presentation.RetainedSourceIssue ??
					"The exact retained Collection manifest is required for verify/repair planning.");

			GameStoragePathSet paths = GetTargetPaths();
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			using (CollectionTargetMutationLease lease = await CollectionTargetMutationLeaseManager.Shared
				.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				new CollectionTargetOwnershipAuthorityValidator(_gameStorageService, _services).ValidateAndReload(lease, authority, paths);
				CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(authority.Target);
				CollectionTargetAssociation association = snapshot.Associations.SingleOrDefault(x => x.AssociationId == associationId);
				if (association == null)
					throw new InvalidOperationException("The Collection association changed while verify/repair state was being captured.");
				if (!association.Revision.Equals(presentation.Association.Association.Revision))
					throw new InvalidOperationException("The Collection association revision changed while verify/repair state was being captured.");

				var reader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
					_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, _associationStore);
				CollectionNativeStateIndex state = reader.Capture(authority.Target);
				List<CollectionMemberBinding> bindings = snapshot.Bindings.Where(x => x.Association.AssociationId == associationId).ToList();
				List<UserOverride> overrides = snapshot.Overrides.Where(x => x.Requirement.AssociationId == associationId).ToList();
				List<CollectionDriftObservation> drift = snapshot.DriftObservations.Where(x => x.Requirement.AssociationId == associationId).ToList();
				CollectionVerifyRepairPreparationResult preparation = new CollectionVerifyRepairPreparationService(_services, _store, _revisionSourceStore)
					.Prepare(association, presentation.RetainedManifest, state, bindings, overrides, drift, cancellationToken);
				IEnumerable<CollectionMemberBinding> planningBindings = preparation.IsComplete
					? (IEnumerable<CollectionMemberBinding>)preparation.EffectiveBindings : bindings;
				IEnumerable<CollectionMemberEffectPreview> previews = preparation.IsComplete
					? preparation.PreparedRecipes.Select(x => x.EffectPreview) : null;
				IReadOnlyDictionary<CollectionRequirementReference, CollectionRequirementState> currentMemberStates =
					ObserveCurrentVerifyMemberStates(planningBindings, drift);
				return new CollectionVerifyRepairPlanner().Plan(association, presentation.RetainedManifest.Manifest, state, planningBindings, overrides, drift,
					previews, preparation, currentMemberStates, preparation.IsComplete ? preparation.BindingUpdates : null);
			}
		}

		private IReadOnlyDictionary<CollectionRequirementReference, CollectionRequirementState> ObserveCurrentVerifyMemberStates(
			IEnumerable<CollectionMemberBinding> bindings, IEnumerable<CollectionDriftObservation> drift)
		{
			var bindingsByMember = (bindings ?? Enumerable.Empty<CollectionMemberBinding>())
				.GroupBy(x => x.MemberKey).ToDictionary(x => x.Key, x => x.Single());
			var result = new Dictionary<CollectionRequirementReference, CollectionRequirementState>();
			foreach (CollectionDriftObservation observation in (drift ?? Enumerable.Empty<CollectionDriftObservation>())
				.Where(x => x.Requirement.MemberKey != null && x.Requirement.Aspect == CollectionRequirementAspect.MemberEnabledState))
			{
				CollectionMemberBinding binding;
				if (!bindingsByMember.TryGetValue(observation.Requirement.MemberKey, out binding))
					continue;
				try
				{
					result[observation.Requirement] = ObserveManagedMemberState(binding, CollectionRequirementAspect.MemberEnabledState);
				}
				catch (InvalidOperationException)
				{
					// Fail closed: when current enabled state cannot be proven, preserve the persisted drift observation.
				}
			}
			return result;
		}

		/// <summary>
		/// Reconciles stale persisted drift after an explicit verify pass proved the currently supported state healthy.
		/// </summary>
		/// <remarks>
		/// This never changes native NMM state. It only removes drift observations whose exact requirement is now reported as
		/// satisfied by the supplied verify plan, after reloading authority and proving the native-state fingerprint did not change.
		/// </remarks>
		public async Task<bool> ReconcileVerifiedHealthyStateAsync(CollectionVerifyRepairPlan plan, CancellationToken cancellationToken)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (!plan.IsHealthyAtCurrentCoverage)
				throw new InvalidOperationException("Only a healthy verify/repair plan can reconcile stale Collection drift.");
			if (!plan.Association.Target.Equals(ResolveCurrentTarget()))
				throw new InvalidOperationException("The verify/repair plan does not belong to the current canonical target.");

			GameStoragePathSet paths = GetTargetPaths();
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			using (CollectionTargetMutationLease lease = await CollectionTargetMutationLeaseManager.Shared
				.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				new CollectionTargetOwnershipAuthorityValidator(_gameStorageService, _services).ValidateAndReload(lease, authority, paths);
				var reader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
					_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, _associationStore);
				CollectionNativeStateIndex state = reader.Capture(authority.Target);
				if (!state.Fingerprint.Equals(plan.StateFingerprint))
					throw new InvalidOperationException("Native state changed after verify/repair assessment; verify again before reconciling Collection drift.");

				CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(authority.Target);
				CollectionTargetAssociation association = snapshot.Associations.SingleOrDefault(x => x.AssociationId == plan.Association.AssociationId);
				if (association == null || !association.Revision.Equals(plan.Association.Revision) || association.State != plan.Association.State)
					throw new InvalidOperationException("The Collection association changed after verify/repair assessment.");
				if (association.State == CollectionAssociationState.Recovering)
					throw new InvalidOperationException("A recovering Collection association cannot be normalized by a healthy verify pass.");

				List<CollectionDriftObservation> drift = snapshot.DriftObservations
					.Where(x => x.Requirement.AssociationId == association.AssociationId).ToList();
				List<CollectionRequirementReference> satisfiedDrift = drift
					.Where(x => plan.Findings.Any(f => f.Kind == CollectionVerifyRepairFindingKind.Satisfied &&
						f.Requirement != null && f.Requirement.Equals(x.Requirement) &&
						f.ExpectedState != null && f.ObservedState != null && f.ExpectedState.Equals(f.ObservedState)))
					.Select(x => x.Requirement).Distinct().ToList();
				if (satisfiedDrift.Count != drift.Count)
					throw new InvalidOperationException("The healthy verify pass did not explicitly satisfy every persisted Collection drift observation.");

				int overrideCount = snapshot.Overrides.Count(x => x.Requirement.AssociationId == association.AssociationId);
				CollectionAssociationState finalState = overrideCount == 0
					? CollectionAssociationState.Applied : CollectionAssociationState.Modified;
				if (plan.BindingUpdates.Count > 0 && !plan.ExactEffectVerificationAvailable)
					throw new InvalidOperationException("A manually reinstalled Collection member cannot be rebound without exact effect verification.");
				if (satisfiedDrift.Count == 0 && plan.BindingUpdates.Count == 0 && association.State == finalState)
					return false;

				CollectionTargetAssociation finalAssociation = association.WithState(finalState);
				_associationStore.SaveVerifiedHealthyReconciliation(association, finalAssociation, plan.BindingUpdates, satisfiedDrift);
				return true;
			}
		}

		/// <summary>Executes the deliberately narrow C10.10b qualified repair subset for a previously verified exact plan.</summary>
		public Task<CollectionVerifyRepairExecutionResult> ExecuteQualifiedRepairAsync(CollectionVerifyRepairPlan plan,
			CancellationToken cancellationToken)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (!plan.Association.Target.Equals(ResolveCurrentTarget()))
				throw new InvalidOperationException("The verify/repair plan does not belong to the current canonical target.");
			var coordinator = new CollectionVerifyRepairExecutionCoordinator(_services, _gameStorageService, _store, _associationStore);
			return coordinator.ExecuteAsync(plan, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Returns persisted Local Collection captures belonging to the current canonical target.</summary>
		public IReadOnlyList<CollectionManagementLocalCapture> GetLocalCaptures()
		{
			if (!_store.Exists)
				return new CollectionManagementLocalCapture[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var result = new List<CollectionManagementLocalCapture>();
			foreach (LocalCapture capture in _localCaptureStore.GetCapturesForTarget(target))
			{
				CollectionDefinition definition = _catalogStore.GetDefinition(capture.Revision.Collection);
				CollectionRevision revision = _catalogStore.GetRevision(capture.Revision);
				result.Add(new CollectionManagementLocalCapture(capture, definition == null ? null : definition.DisplayName,
					revision == null ? null : revision.RevisionLabel));
			}
			return new ReadOnlyCollection<CollectionManagementLocalCapture>(result
				.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(x => x.RevisionLabel, StringComparer.CurrentCultureIgnoreCase).ToList());
		}

		/// <summary>Returns the exact sealed Local Collection sources bound to interrupted Local restore operations for the current target.</summary>
		/// <remarks>This is diagnostic/read-only state. The selected Local Collection combo never changes the durable restore intent.</remarks>
		public IReadOnlyList<CollectionManagementLocalRestoreRecoverySource> GetInterruptedLocalRestoreSources()
		{
			if (!_store.Exists)
				return new CollectionManagementLocalRestoreRecoverySource[0];

			CollectionTargetIdentity target = ResolveCurrentTarget();
			var artifactStore = new CollectionsRetainedArtifactStore(_store);
			var referenceStore = new CollectionsRetainedArtifactReferenceStore(_store);
			var result = new List<CollectionManagementLocalRestoreRecoverySource>();
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.RestoreLocalCapture).OrderBy(x => x.Identity.OperationId))
			{
				LocalCaptureIdentity captureIdentity = null;
				string displayName = null;
				string revisionLabel = null;
				string diagnostic = null;
				try
				{
					CollectionsRetainedArtifactReferenceRecord reference = referenceStore.GetReferenceForOwnerRole(
						CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.OperationId.ToString("D"), "local-restore-plan-v1");
					if (reference == null || !artifactStore.VerifyArtifact(reference.ArtifactId))
						throw new InvalidDataException("The durable Local restore intent is missing or failed retained-artifact verification.");
					byte[] bytes;
					using (Stream source = artifactStore.OpenRead(reference.ArtifactId))
					using (var buffer = new MemoryStream())
					{
						source.CopyTo(buffer);
						bytes = buffer.ToArray();
					}
					captureIdentity = CollectionLocalRestoreIntentCodec.ReadCaptureIdentity(bytes);
					LocalCapture capture = _localCaptureStore.GetCapture(captureIdentity);
					if (capture == null)
						throw new InvalidDataException("The durable Local restore intent references a Local Collection that is no longer persisted.");
					if (!capture.SourceTarget.Equals(operation.Target) || operation.Revision == null || !capture.Revision.Equals(operation.Revision))
						throw new InvalidDataException("The durable Local restore intent source does not match the interrupted operation revision or target.");
					CollectionDefinition definition = _catalogStore.GetDefinition(capture.Revision.Collection);
					CollectionRevision revision = _catalogStore.GetRevision(capture.Revision);
					displayName = definition == null || String.IsNullOrWhiteSpace(definition.DisplayName)
						? capture.Revision.Collection.StableId : definition.DisplayName;
					revisionLabel = revision == null || String.IsNullOrWhiteSpace(revision.RevisionLabel)
						? "Local revision " + capture.Revision.StableRevisionId : revision.RevisionLabel;
				}
				catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is IOException || ex is ArgumentException)
				{
					diagnostic = ex.Message;
				}
				result.Add(new CollectionManagementLocalRestoreRecoverySource(operation, captureIdentity, displayName, revisionLabel, diagnostic));
			}
			return new ReadOnlyCollection<CollectionManagementLocalRestoreRecoverySource>(result);
		}

		/// <summary>Returns mutable Local Collection working copies persisted in this game/storage Collections store.</summary>
		public IReadOnlyList<CollectionManagementLocalWorkingCopy> GetLocalWorkingCopies()
		{
			if (!_store.Exists)
				return new CollectionManagementLocalWorkingCopy[0];
			var result = new List<CollectionManagementLocalWorkingCopy>();
			foreach (CollectionLocalWorkingCopyRecord record in _localWorkingCopyStore.GetAll())
			{
				CollectionDefinition definition = _catalogStore.GetDefinition(record.Collection);
				CollectionDefinition sourceDefinition = _catalogStore.GetDefinition(record.SourceRevision.Collection);
				CollectionRevision sourceRevision = _catalogStore.GetRevision(record.SourceRevision);
				if (definition == null || sourceRevision == null)
					throw new CollectionsStoreSchemaException("A Local Collection working copy is missing required catalog metadata.");
				result.Add(new CollectionManagementLocalWorkingCopy(record, definition, sourceDefinition, sourceRevision));
			}
			return new ReadOnlyCollection<CollectionManagementLocalWorkingCopy>(result
				.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(x => x.Collection.StableId, StringComparer.Ordinal).ToList());
		}

		/// <summary>Clones one installed immutable Collection revision into a new NMM-owned mutable Local working copy.</summary>
		/// <remarks>This copies Collections recipe intent only; it never changes installed mods, associations, or native effects.</remarks>
		public CollectionManagementLocalWorkingCopy CloneAssociationToLocalWorkingCopy(Guid associationId, string displayName)
		{
			displayName = CollectionDomainValidation.RequireDisplayValue(displayName, nameof(displayName));
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (!_store.Exists)
				throw new InvalidOperationException("No durable Collections store exists for this target.");

			CollectionTargetIdentity target = ResolveCurrentTarget();
			CollectionTargetAssociation association = _associationStore.GetAssociationsForTarget(target)
				.SingleOrDefault(x => x.AssociationId == associationId);
			if (association == null)
				throw new InvalidOperationException("The selected installed Collection association is no longer present on this target.");

			CollectionRevision sourceRevision = _catalogStore.GetRevision(association.Revision);
			if (sourceRevision == null)
				throw new CollectionsStoreSchemaException("The installed Collection revision metadata is missing from the durable catalog.");
			CollectionRevisionSourceRecord source = _revisionSourceStore.GetSource(sourceRevision.Identity);
			if (source == null || String.IsNullOrEmpty(source.RawManifestArtifactId))
				throw new InvalidOperationException("The exact retained Collection recipe is unavailable, so this revision cannot be cloned safely.");
			CollectionDefinition sourceDefinition = _catalogStore.GetDefinition(sourceRevision.Collection);

			CollectionIdentity localIdentity = CollectionIdentity.FromLocal(Guid.NewGuid());
			var localDefinition = new CollectionDefinition(localIdentity, displayName,
				sourceDefinition == null ? null : sourceDefinition.AuthorDisplayName,
				sourceDefinition == null ? null : sourceDefinition.Summary);
			CollectionLocalWorkingCopyRecord record = _localWorkingCopyStore.CreateClone(localDefinition, sourceRevision, source);
			return new CollectionManagementLocalWorkingCopy(record, localDefinition, sourceDefinition, sourceRevision);
		}

		/// <summary>Loads the current editable Local working-copy recipe without changing native state.</summary>
		public CollectionLocalWorkingCopyEditSnapshot GetLocalWorkingCopyEditSnapshot(CollectionIdentity localCollection)
		{
			return _localWorkingCopyEditor.GetSnapshot(localCollection);
		}

		/// <summary>Saves Local working-copy metadata/member edits without changing installed/native state.</summary>
		public CollectionLocalWorkingCopyEditSnapshot SaveLocalWorkingCopyDraft(CollectionLocalWorkingCopyEditSnapshot snapshot,
			string displayName, string summary, IEnumerable<CollectionLocalWorkingCopyMemberDecision> decisions)
		{
			return _localWorkingCopyEditor.SaveDraft(snapshot, displayName, summary, decisions);
		}

		/// <summary>Seals the current mutable draft as a new immutable Local Collection revision.</summary>
		public CollectionRevision SaveLocalWorkingCopyRevision(CollectionIdentity localCollection, string revisionLabel, string notes)
		{
			return _localWorkingCopyEditor.SealRevision(localCollection, revisionLabel, notes);
		}

		/// <summary>Returns detailed pin/provenance impact for one exact installed Collection member.</summary>
		public CollectionManagementMemberImpact GetMemberImpact(Guid associationId, CollectionMemberKey memberKey)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (memberKey == null) throw new ArgumentNullException(nameof(memberKey));
			CollectionTargetIdentity target = ResolveCurrentTarget();
			CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(target);
			CollectionMemberBinding binding = snapshot.Bindings.SingleOrDefault(x =>
				x.Association.AssociationId == associationId && x.MemberKey.Equals(memberKey));
			if (binding == null)
				throw new InvalidOperationException("The selected Collection member is no longer bound to this installed association.");
			NativeModProvenance provenance = snapshot.NativeModProvenance.FirstOrDefault(x => x.NativeMod.Equals(binding.NativeMod));
			if (provenance == null)
				provenance = new NativeModProvenance(binding.NativeMod, StandaloneModUse.Unknown);
			var pinCoordinator = new CollectionPinOverrideCoordinator(_associationStore, target);
			return new CollectionManagementMemberImpact(binding, provenance, pinCoordinator.GetMemberPins(binding.NativeMod));
		}

		/// <summary>Builds the explicit reviewed C7.9 Local restore plan for one saved capture.</summary>
		public Task<CollectionLocalRestorePreview> PreviewLocalRestoreAsync(LocalCaptureIdentity captureIdentity,
			CancellationToken cancellationToken)
		{
			return _localRestoreWorkflow.PreviewAsync(captureIdentity, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Applies one exact user-reviewed Local restore through complete C7 final verification.</summary>
		public Task<CollectionLocalRestoreWorkflowResult> RestoreLocalCaptureAsync(CollectionLocalRestorePreview preview,
			CancellationToken cancellationToken)
		{
			return _localRestoreWorkflow.ApplyAsync(preview, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Adopts one exact current member drift observation as an explicit local Collection decision.</summary>
		/// <remarks>This changes only durable Collection intent/provenance; it never mutates the native mod or repairs files.</remarks>
		public CollectionOverrideDecisionResult AcceptMemberDrift(Guid associationId, CollectionMemberKey memberKey,
			Guid observationId, string note)
		{
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));
			if (observationId == Guid.Empty)
				throw new ArgumentException("A non-empty drift observation identifier is required.", nameof(observationId));

			CollectionTargetIdentity target = ResolveCurrentTarget();
			var coordinator = new CollectionPinOverrideCoordinator(_associationStore, target);
			CollectionAssociationCustomization customization = coordinator.GetCustomization(associationId);
			CollectionDriftObservation drift = customization.DriftObservations.SingleOrDefault(x =>
				x.ObservationId == observationId && x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(memberKey));
			if (drift == null)
				throw new InvalidOperationException("The selected Collection member drift observation is no longer current.");
			if (!_associationStore.GetBindings(associationId).Any(x => x.MemberKey.Equals(memberKey)))
				throw new InvalidOperationException("The selected Collection member is no longer bound to this installed association.");

			return coordinator.AcceptCurrentDrift(associationId, observationId, note);
		}

		/// <summary>Clears one exact member override without changing native state.</summary>
		/// <remarks>When reality still differs from the curator baseline, the coordinator immediately preserves that difference as drift.</remarks>
		public CollectionOverrideDecisionResult ClearMemberOverride(Guid associationId, CollectionMemberKey memberKey,
			Guid overrideId)
		{
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));
			if (overrideId == Guid.Empty)
				throw new ArgumentException("A non-empty override identifier is required.", nameof(overrideId));

			CollectionTargetIdentity target = ResolveCurrentTarget();
			var coordinator = new CollectionPinOverrideCoordinator(_associationStore, target);
			CollectionAssociationCustomization customization = coordinator.GetCustomization(associationId);
			UserOverride userOverride = customization.UserOverrides.SingleOrDefault(x =>
				x.OverrideId == overrideId && x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(memberKey));
			if (userOverride == null)
				throw new InvalidOperationException("The selected Collection member override is no longer current.");
			if (!_associationStore.GetBindings(associationId).Any(x => x.MemberKey.Equals(memberKey)))
				throw new InvalidOperationException("The selected Collection member is no longer bound to this installed association.");

			CollectionDriftObservation drift = customization.DriftObservations
				.SingleOrDefault(x => x.Requirement.Equals(userOverride.Requirement));
			CollectionRequirementState observedState = drift == null ? userOverride.UserChosenState : drift.ObservedState;
			return coordinator.ClearOverride(associationId, overrideId, observedState);
		}

		/// <summary>Resolves the exact live NMM mod bound to one installed Collection member for UI navigation.</summary>
		public IMod ResolveManagedMemberMod(Guid associationId, CollectionMemberKey memberKey)
		{
			CollectionMemberBinding binding = RequireManagedMemberBinding(associationId, memberKey);
			List<IMod> matches = FindLiveMemberMods(binding.NativeMod);
			if (matches.Count != 1)
				throw new InvalidOperationException(matches.Count == 0
					? "The selected Collection member is no longer present as one live NMM mod instance."
					: "The selected Collection member resolves to multiple live NMM mod instances and cannot be focused safely.");
			return matches[0];
		}

		/// <summary>Accepts one exact missing/disabled member observation as an intentional ignored member difference.</summary>
		public CollectionOverrideDecisionResult IgnoreMemberDifference(Guid associationId, CollectionMemberKey memberKey,
			Guid observationId)
		{
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));
			if (observationId == Guid.Empty)
				throw new ArgumentException("A non-empty drift observation identifier is required.", nameof(observationId));

			CollectionTargetIdentity target = ResolveCurrentTarget();
			var coordinator = new CollectionPinOverrideCoordinator(_associationStore, target);
			CollectionAssociationCustomization customization = coordinator.GetCustomization(associationId);
			CollectionDriftObservation drift = customization.DriftObservations.SingleOrDefault(x =>
				x.ObservationId == observationId && x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(memberKey));
			if (drift == null)
				throw new InvalidOperationException("The selected Collection member difference is no longer current.");
			if (!CollectionMemberRequirementStates.IsIgnorableMemberDifference(drift.Requirement.Aspect))
				throw new InvalidOperationException("Only member participation or enabled-state differences can be ignored from the installed-member action.");
			RequireManagedMemberBinding(associationId, memberKey);

			return coordinator.AcceptCurrentDrift(associationId, observationId,
				"Ignored from the installed Collection member management view.");
		}

		/// <summary>Stops ignoring one member participation/enabled-state override using freshly observed native state.</summary>
		public CollectionOverrideDecisionResult StopIgnoringMemberDifference(Guid associationId, CollectionMemberKey memberKey,
			Guid overrideId)
		{
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));
			if (overrideId == Guid.Empty)
				throw new ArgumentException("A non-empty override identifier is required.", nameof(overrideId));

			CollectionTargetIdentity target = ResolveCurrentTarget();
			var coordinator = new CollectionPinOverrideCoordinator(_associationStore, target);
			CollectionAssociationCustomization customization = coordinator.GetCustomization(associationId);
			UserOverride userOverride = customization.UserOverrides.SingleOrDefault(x =>
				x.OverrideId == overrideId && x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(memberKey));
			if (userOverride == null)
				throw new InvalidOperationException("The selected ignored Collection member difference is no longer current.");
			if (!CollectionMemberRequirementStates.IsIgnorableMemberDifference(userOverride.Requirement.Aspect))
				throw new InvalidOperationException("The selected local override is not an ignored member participation/enabled-state difference.");

			CollectionMemberBinding binding = RequireManagedMemberBinding(associationId, memberKey);
			CollectionRequirementState observedState = ObserveManagedMemberState(binding, userOverride.Requirement.Aspect);
			return coordinator.ClearOverride(associationId, overrideId, observedState);
		}

		private CollectionMemberBinding RequireManagedMemberBinding(Guid associationId, CollectionMemberKey memberKey)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));

			CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
			if (association == null || !association.Target.Equals(ResolveCurrentTarget()))
				throw new InvalidOperationException("The selected installed Collection association is no longer current for this target.");
			CollectionMemberBinding binding = _associationStore.GetBindings(associationId).SingleOrDefault(x => x.MemberKey.Equals(memberKey));
			if (binding == null)
				throw new InvalidOperationException("The selected Collection member is no longer bound to this installed association.");
			return binding;
		}

		private List<IMod> FindLiveMemberMods(NativeModInstanceIdentity nativeMod)
		{
			return _services.ModManager.InstallationLog.ActiveMods.Where(mod =>
			{
				if (mod == null)
					return false;
				try
				{
					return StringComparer.OrdinalIgnoreCase.Equals(_services.ModManager.InstallationLog.GetModKey(mod), nativeMod.NativeModKey);
				}
				catch
				{
					return false;
				}
			}).ToList();
		}

		private CollectionRequirementState ObserveManagedMemberState(CollectionMemberBinding binding, CollectionRequirementAspect aspect)
		{
			List<IMod> matches = FindLiveMemberMods(binding.NativeMod);
			if (matches.Count > 1)
				throw new InvalidOperationException("The selected Collection member resolves to multiple live NMM mod instances.");

			if (aspect == CollectionRequirementAspect.MemberParticipation)
				return matches.Count == 0 ? CollectionRequirementState.Absent() : CollectionMemberRequirementStates.Included();

			if (aspect != CollectionRequirementAspect.MemberEnabledState)
				throw new InvalidOperationException("Only member participation/enabled-state requirements can be observed by this management action.");
			if (matches.Count != 1)
				throw new InvalidOperationException("The member is no longer installed, so its enabled/disabled state cannot be established safely.");

			ModInstallMethod installMethod = _services.ModManager.InstallationLog.GetModInstallMethod(matches[0]);
			bool enabled = true;
			if (installMethod == ModInstallMethod.Virtual)
			{
				string fileName = Path.GetFileName(matches[0].Filename);
				enabled = _services.ModManager.VirtualModActivator != null &&
					_services.ModManager.VirtualModActivator.ActiveModList.Contains((fileName ?? String.Empty).ToLowerInvariant());
			}
			return CollectionMemberRequirementStates.Enabled(enabled);
		}


		/// <summary>Builds the exact safe-removal review for one currently bound optional installed Collection member.</summary>
		public Task<CollectionInstalledMemberRemovalPlan> PreviewOptionalMemberRemovalAsync(Guid associationId,
			CollectionMemberKey memberKey, CancellationToken cancellationToken)
		{
			RequireOptionalInstalledMember(associationId, memberKey);
			return _memberRemovalCoordinator.PreviewAsync(associationId, memberKey, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Executes one explicitly reviewed optional-member removal after exact native/Collection revalidation.</summary>
		public Task<CollectionInstalledMemberRemovalResult> RemoveOptionalMemberAsync(CollectionInstalledMemberRemovalPlan reviewedPlan,
			CancellationToken cancellationToken)
		{
			if (reviewedPlan == null)
				throw new ArgumentNullException(nameof(reviewedPlan));
			RequireOptionalInstalledMember(reviewedPlan.Association.AssociationId, reviewedPlan.MemberKey);
			return _memberRemovalCoordinator.ExecuteAsync(reviewedPlan, GetTargetPaths(), cancellationToken);
		}

		private NormalizedCollectionMember RequireOptionalInstalledMember(Guid associationId, CollectionMemberKey memberKey)
		{
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));
			CollectionManagementAssociationPresentation presentation = GetAssociationPresentation(associationId);
			if (presentation == null || !presentation.HasRetainedManifest)
				throw new InvalidOperationException("The exact retained Collection manifest is required before an installed member can be removed.");
			if (presentation.Association.State == CollectionAssociationState.Recovering ||
				presentation.Association.State == CollectionAssociationState.Incomplete)
				throw new InvalidOperationException("Only Applied or Modified installed Collections can remove an optional member.");
			NormalizedCollectionMember member = presentation.RetainedManifest.Manifest.Members.SingleOrDefault(x =>
				x != null && x.IdentityResolution.IsResolved && x.IdentityResolution.Key.Equals(memberKey));
			if (member == null)
				throw new InvalidOperationException("The selected installed member is not present in the exact retained Collection manifest.");
			if (member.Requirement != CollectionMemberRequirement.Optional)
				throw new InvalidOperationException("Required Collection members cannot be removed through optional-member management.");
			if (!presentation.BoundMemberKeys.Contains(memberKey))
				throw new InvalidOperationException("The selected optional Collection member is no longer part of the installed association.");
			CollectionMemberDependency dependent = presentation.RetainedManifest.Manifest.Dependencies.FirstOrDefault(x =>
				x.PrerequisiteMemberKey.Equals(memberKey) && presentation.BoundMemberKeys.Contains(x.DependentMemberKey));
			if (dependent != null)
				throw new InvalidOperationException("The selected optional Collection member is still a prerequisite of another installed member and cannot be removed independently.");
			return member;
		}

		/// <summary>Detaches Collection tracking while preserving every native mod/effect.</summary>
		public CollectionDetachResult Detach(Guid associationId)
		{
			CollectionTargetIdentity target = ResolveCurrentTarget();
			return new CollectionDetachCoordinator(_associationStore, target).Detach(associationId);
		}

		/// <summary>Builds the reviewed safe-effect-removal plan for one installed association.</summary>
		public Task<CollectionUninstallEffectsPlan> PreviewEffectRemovalAsync(Guid associationId, CancellationToken cancellationToken)
		{
			return _uninstallCoordinator.PreviewAsync(associationId, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Executes an explicitly reviewed safe-effect-removal plan after native revalidation.</summary>
		public Task<CollectionUninstallEffectsResult> RemoveEffectsAsync(CollectionUninstallEffectsPlan reviewedPlan,
			CancellationToken cancellationToken)
		{
			return RemoveEffectsAsync(reviewedPlan, cancellationToken, null);
		}

		/// <summary>Executes reviewed removal and cleans obsolete retention while preserving an open incoming Collection preview.</summary>
		public async Task<CollectionUninstallEffectsResult> RemoveEffectsAsync(CollectionUninstallEffectsPlan reviewedPlan,
			CancellationToken cancellationToken, CollectionIdentity retainedPreviewCollection)
		{
			CollectionUninstallEffectsResult result = await _uninstallCoordinator.ExecuteAsync(reviewedPlan,
				GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			if (result.IsSuccessful)
				await CleanupRetainedContentAsync(CancellationToken.None, retainedPreviewCollection).ConfigureAwait(false);
			return result;
		}

		/// <summary>
		/// Releases obsolete removal retention and retries unreferenced Collections content cleanup at an idle target boundary.
		/// </summary>
		/// <remarks>
		/// Cleanup failures never turn a committed removal into a failed uninstall. Locked blobs retain durable tombstones for
		/// the next startup retry. The normal native archive library and live installed effects are outside this cleanup route.
		/// </remarks>
		public Task CleanupRetainedContentAsync(CancellationToken cancellationToken, CollectionIdentity retainedPreviewCollection = null)
		{
			return CleanupRetainedContentAsync(cancellationToken, retainedPreviewCollection, Int32.MaxValue);
		}

		/// <summary>Runs retained-content cleanup with an explicit per-pass artifact limit.</summary>
		/// <remarks>Startup uses a bounded pass so stale retained content cannot monopolize the Collections page or target lease.</remarks>
		public async Task CleanupRetainedContentAsync(CancellationToken cancellationToken, CollectionIdentity retainedPreviewCollection,
			int maximumArtifacts)
		{
			if (maximumArtifacts <= 0)
				throw new ArgumentOutOfRangeException(nameof(maximumArtifacts));
			if (!_store.Exists)
				return;
			try
			{
				CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(GetTargetPaths());
				using (CollectionTargetMutationLease lease = await CollectionTargetMutationLeaseManager.Shared
					.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
				{
					await Task.Run(() =>
					{
						var cleanup = new CollectionsRetainedArtifactCleanupStore(_store);
						if (!cleanup.ReleaseRemovedCollectionContent(authority.Target, retainedPreviewCollection))
							return;
						var attempted = new HashSet<string>(StringComparer.Ordinal);
						int remaining = maximumArtifacts;
						foreach (string artifactId in cleanup.GetPendingTombstones(remaining))
						{
							CollectRetainedArtifact(cleanup, artifactId, attempted, cancellationToken);
							if (--remaining == 0)
								return;
						}
						while (remaining > 0)
						{
							cancellationToken.ThrowIfCancellationRequested();
							int batchSize = Math.Min(128, remaining);
							List<string> candidates = cleanup.GetCleanupCandidates(batchSize).Where(x => !attempted.Contains(x)).ToList();
							if (candidates.Count == 0)
								break;
							foreach (string artifactId in candidates)
							{
								CollectRetainedArtifact(cleanup, artifactId, attempted, cancellationToken);
								if (--remaining == 0)
									break;
							}
						}
					}, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Collection retained-content cleanup deferred: " + ex);
			}
		}

		/// <summary>Collects one tracked blob once per sweep while leaving failed physical deletions available for retry.</summary>
		private static void CollectRetainedArtifact(CollectionsRetainedArtifactCleanupStore cleanup, string artifactId,
			HashSet<string> attempted, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!attempted.Add(artifactId))
				return;
			try
			{
				cleanup.TryCollectUnreferencedArtifact(artifactId);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				Trace.TraceWarning("Collection retained artifact cleanup deferred for " + artifactId + ": " + ex);
			}
		}

		/// <summary>
		/// Repairs missing Nexus metadata for already-applied Collection members without changing installed effects or Collection provenance.
		/// </summary>
		public int RefreshAppliedNexusMetadata()
		{
			if (!_store.Exists)
				return 0;
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var bindings = new List<CollectionMemberBinding>();
			foreach (CollectionTargetAssociation association in _associationStore.GetAssociationsForTarget(target)
				.Where(x => x.State == CollectionAssociationState.Applied))
			{
				bindings.AddRange(_associationStore.GetBindings(association.AssociationId));
			}
			return new CollectionNexusMetadataHydrator(_services.ModManager).Enrich(bindings);
		}

		/// <summary>Inspects interrupted C8 replacement operations for startup routing without resuming or mutating native state.</summary>
		public IReadOnlyList<CollectionReplacementStartupInspection> InspectInterruptedReplacements()
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionReplacementStartupInspection[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var inspector = new CollectionReplacementOperationCoordinator(_operationStore,
				new CollectionsResolvedPlanStore(_store), new CollectionsRetainedArtifactStore(_store),
				new CollectionsRetainedArtifactReferenceStore(_store));
			return new ReadOnlyCollection<CollectionReplacementStartupInspection>(_operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.ReplaceCurrentManagedSetup)
				.Select(inspector.InspectInterrupted).ToList());
		}

		/// <summary>Runs C8.7 recovery for one interrupted replacement using its exact already-reviewed runtime plan.</summary>
		public Task<CollectionReplacementRecoveryResult> RecoverInterruptedReplacementAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan reviewedPlan, CancellationToken cancellationToken)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				throw new InvalidOperationException("No durable Collections store exists for replacement recovery.");
			return _replacementRecovery.RecoverAsync(operationIdentity, reviewedPlan, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Rehydrates and resumes incomplete C10 UpdateRevision operations for the current target.</summary>
		public async Task<IReadOnlyList<CollectionRevisionUpdateWorkflowResult>> ReconcileInterruptedRevisionUpdatesAsync(
			CancellationToken cancellationToken)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionRevisionUpdateWorkflowResult[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionRevisionUpdateWorkflowResult>();
			CollectionRevisionUpdateApplicationService workflow = null;
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.UpdateRevision).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (workflow == null) workflow = new CollectionRevisionUpdateApplicationService(_services, _gameStorageService);
				CollectionRevisionUpdateWorkflowResult result = await workflow.ResumeAsync(operation.Identity, cancellationToken).ConfigureAwait(false);
				results.Add(result);
				if (!result.IsCommitted && result.Status != CollectionRevisionUpdateWorkflowStatus.AwaitingInput &&
					result.Status != CollectionRevisionUpdateWorkflowStatus.ReadyForReview)
					break;
			}
			return new ReadOnlyCollection<CollectionRevisionUpdateWorkflowResult>(results);
		}

		/// <summary>Rehydrates and resumes incomplete C10.10 qualified repairs from their exact durable reviewed scope.</summary>
		public async Task<IReadOnlyList<CollectionVerifyRepairRecoveryResult>> ReconcileInterruptedVerifyRepairsAsync(
			CancellationToken cancellationToken)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionVerifyRepairRecoveryResult[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionVerifyRepairRecoveryResult>();
			var coordinator = new CollectionVerifyRepairExecutionCoordinator(_services, _gameStorageService, _store, _associationStore);
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.VerifyRepair).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					CollectionVerifyRepairReviewedIntent intent = coordinator.LoadReviewedIntent(operation.Identity);
					CollectionVerifyRepairPlan currentPlan = await PreviewVerifyRepairAsync(intent.AssociationId, cancellationToken).ConfigureAwait(false);
					if (currentPlan == null)
						throw new InvalidOperationException("The installed Collection association required by the interrupted repair no longer exists.");
					CollectionVerifyRepairExecutionResult execution = await coordinator.ResumeAsync(operation.Identity, currentPlan,
						GetTargetPaths(), cancellationToken).ConfigureAwait(false);
					results.Add(new CollectionVerifyRepairRecoveryResult(operation.Identity,
						CollectionVerifyRepairRecoveryStatus.Committed, execution, "The interrupted qualified repair was resumed and verified."));
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					coordinator.MarkRecoveryRequired(operation.Identity);
					results.Add(new CollectionVerifyRepairRecoveryResult(operation.Identity,
						CollectionVerifyRepairRecoveryStatus.RecoveryRequired, null, ex.Message));
					break;
				}
			}
			return new ReadOnlyCollection<CollectionVerifyRepairRecoveryResult>(results);
		}

		/// <summary>Reconciles persisted Local restore operations through every remaining C7 phase and aggregate final verification.</summary>
		public async Task<IReadOnlyList<CollectionLocalRestoreWorkflowResult>> ReconcileInterruptedLocalRestoresAsync(
			CancellationToken cancellationToken)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionLocalRestoreWorkflowResult[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionLocalRestoreWorkflowResult>();
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.RestoreLocalCapture).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionLocalRestoreWorkflowResult result = await _localRestoreWorkflow
					.ResumeAsync(operation.Identity, GetTargetPaths(), cancellationToken).ConfigureAwait(false);
				results.Add(result);
				if (!result.IsSuccessful)
					break;
			}
			return new ReadOnlyCollection<CollectionLocalRestoreWorkflowResult>(results);
		}

		/// <summary>Reconciles and resumes only persisted RestoreLocalCapture member phases for the current target.</summary>
		public async Task<IReadOnlyList<CollectionLocalRestoreMemberResumeResult>> ReconcileInterruptedLocalRestoreMembersAsync(
			CancellationToken cancellationToken)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionLocalRestoreMemberResumeResult[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionLocalRestoreMemberResumeResult>();
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.RestoreLocalCapture).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				results.Add(await _localRestoreResumeCoordinator.ResumeAsync(operation.Identity,
					GetTargetPaths(), cancellationToken).ConfigureAwait(false));
			}
			return new ReadOnlyCollection<CollectionLocalRestoreMemberResumeResult>(results);
		}


		/// <summary>Reconciles interrupted C9 installed-member removals from authoritative native state.</summary>
		public async Task<IReadOnlyList<CollectionInstalledMemberRemovalResult>> ReconcileInterruptedMemberRemovalsAsync(
			CancellationToken cancellationToken)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionInstalledMemberRemovalResult[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionInstalledMemberRemovalResult>();
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.RemoveCollectionMemberEffects).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				results.Add(await _memberRemovalCoordinator.ReconcileInterruptedAsync(operation.Identity,
					GetTargetPaths(), cancellationToken).ConfigureAwait(false));
			}
			return new ReadOnlyCollection<CollectionInstalledMemberRemovalResult>(results);
		}

		/// <summary>Reconciles only interrupted safe-effect-removal operations for the current target.</summary>
		public async Task<IReadOnlyList<CollectionUninstallEffectsResult>> ReconcileInterruptedEffectRemovalAsync(
			CancellationToken cancellationToken)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return new CollectionUninstallEffectsResult[0];
			CollectionTargetIdentity target = ResolveCurrentTarget();
			var results = new List<CollectionUninstallEffectsResult>();
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(target)
				.Where(x => x.Kind == CollectionOperationKind.UninstallCollectionEffects).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				results.Add(await _uninstallCoordinator.ReconcileInterruptedAsync(operation.Identity,
					GetTargetPaths(), cancellationToken).ConfigureAwait(false));
			}
			return new ReadOnlyCollection<CollectionUninstallEffectsResult>(results);
		}

		private GameStoragePathSet GetTargetPaths()
		{
			return _gameStorageService.FromGameMode(_services.ModManager.GameMode);
		}

		private CollectionTargetIdentity ResolveCurrentTarget()
		{
			return new CollectionTargetIdentityResolver(_gameStorageService).Resolve(GetTargetPaths()).Target;
		}

		private string GetCollectionToolGameRoot()
		{
			string root = _services.ModManager.GameMode.GameModeEnvironmentInfo.ExecutablePath ??
				_services.ModManager.GameMode.GameModeEnvironmentInfo.SecondaryInstallationPath ??
				_services.ModManager.GameMode.GameModeEnvironmentInfo.InstallationPath;
			if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
				throw new InvalidOperationException("The active game does not expose a canonical game-root path for Collection tools.");
			return Path.GetFullPath(root);
		}

		private static NexusCollectionBundleInputKind ToBundleInputKind(CollectionRevisionSourceInputKind inputKind)
		{
			switch (inputKind)
			{
				case CollectionRevisionSourceInputKind.RawManifest:
					return NexusCollectionBundleInputKind.RawManifest;
				case CollectionRevisionSourceInputKind.Archive:
				case CollectionRevisionSourceInputKind.LocalWorkingCopy:
					return NexusCollectionBundleInputKind.Archive;
				default:
					throw new InvalidOperationException("The retained Collection revision has an unsupported source kind.");
			}
		}
	}
}
