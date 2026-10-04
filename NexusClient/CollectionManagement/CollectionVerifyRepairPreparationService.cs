using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.OnlineServices.NexusMods.Collections;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Exact current-revision preparation used only by C10 verify/repair.</summary>
	public sealed class CollectionVerifyRepairPreparationResult
	{
		private readonly ReadOnlyCollection<PreparedCollectionNativeRecipe> _preparedRecipes;

		internal CollectionVerifyRepairPreparationResult(ResolvedCollectionPlan plan,
			IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes, string issue)
		{
			Plan = plan;
			_preparedRecipes = new ReadOnlyCollection<PreparedCollectionNativeRecipe>((preparedRecipes ?? Enumerable.Empty<PreparedCollectionNativeRecipe>()).ToList());
			Issue = issue;
		}

		public ResolvedCollectionPlan Plan { get; }
		public ReadOnlyCollection<PreparedCollectionNativeRecipe> PreparedRecipes { get { return _preparedRecipes; } }
		public string Issue { get; }
		public bool IsComplete { get { return Plan != null && String.IsNullOrEmpty(Issue); } }
	}

	/// <summary>
	/// Reconstructs exact prepared native recipes for the currently associated revision from retained manifest data and exact
	/// managed archive bytes. This service never mutates the game, member bindings, or Collection state.
	/// </summary>
	public sealed class CollectionVerifyRepairPreparationService
	{
		private readonly ServiceManager _services;
		private readonly CollectionsStore _store;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		public CollectionVerifyRepairPreparationService(ServiceManager services, CollectionsStore store,
			CollectionsRevisionSourceStore revisionSourceStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_artifactStore = new CollectionsRetainedArtifactStore(store);
			_referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
		}

		public CollectionVerifyRepairPreparationResult Prepare(CollectionTargetAssociation association,
			Nexus.Client.OnlineServices.NexusMods.Collections.NexusCollectionBundleImportResult retainedManifest,
			CollectionNativeStateIndex state, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<UserOverride> overrides, IEnumerable<CollectionDriftObservation> drift,
			CancellationToken cancellationToken)
		{
			if (association == null) throw new ArgumentNullException(nameof(association));
			if (retainedManifest == null) throw new ArgumentNullException(nameof(retainedManifest));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (!association.Revision.Equals(retainedManifest.Manifest.Revision) || !association.Target.Equals(state.Target))
				throw new ArgumentException("Verify/repair preparation inputs must describe one exact installed revision and target.");

			List<CollectionMemberBinding> bindingList = (bindings ?? throw new ArgumentNullException(nameof(bindings)))
				.Where(x => x.Association.AssociationId == association.AssociationId).ToList();
			List<UserOverride> overrideList = (overrides ?? throw new ArgumentNullException(nameof(overrides)))
				.Where(x => x.Requirement.AssociationId == association.AssociationId).ToList();
			List<CollectionDriftObservation> driftList = (drift ?? throw new ArgumentNullException(nameof(drift)))
				.Where(x => x.Requirement.AssociationId == association.AssociationId).ToList();

			var repository = _services.ModRepository as NexusModsApiRepository;
			if (repository == null)
				return new CollectionVerifyRepairPreparationResult(null, null,
					"Exact verify/repair archive identity requires the active Nexus Mods repository implementation.");

			CollectionEffectiveSelection effective;
			CollectionNexusSourcePolicyResolution sourcePolicyResolution;
			try
			{
				effective = BuildEffectiveSelection(retainedManifest.CapabilityReport, bindingList, overrideList, driftList);
				sourcePolicyResolution = new CollectionNexusSourcePolicyResolver(_revisionSourceStore, repository)
					.ResolveInstalled(effective, bindingList, state);
				effective = sourcePolicyResolution.Selection;
			}
			catch (Exception ex)
			{
				return new CollectionVerifyRepairPreparationResult(null, null,
					"The installed revision cannot be reconstructed as one exact supported selection: " + ex.Message);
			}
			if (effective.CapabilityReport.Status != CollectionCompatibilityStatus.Supported)
				return new CollectionVerifyRepairPreparationResult(null, null,
					"The installed revision's effective member selection is not fully supported by the current native recipe adapters.");

			List<ResolvedCollectionMemberPlan> selected = CreateSelectedMembers(effective.Manifest, sourcePolicyResolution.ArtifactChoices);
			var resolvedPlan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), association.Target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, effective.CapabilityReport, selected);

			var prepared = new List<PreparedCollectionNativeRecipe>();
			var nativePreparer = new CollectionNativeRecipePreparer(_store, _revisionSourceStore, _artifactStore, _referenceStore);
			var adopter = new CollectionVerifiedArchiveAdopter(new ModManagerCollectionManagedArchiveSource(_services.ModManager),
				new NexusCollectionArchiveIdentityVerifier(repository), _artifactStore, _referenceStore);

			try
			{
				foreach (CollectionMemberBinding binding in bindingList.OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal))
				{
					cancellationToken.ThrowIfCancellationRequested();
					ResolvedCollectionMemberPlan member = resolvedPlan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(binding.MemberKey));
					if (member == null)
						return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
							"A current member binding is intentionally outside the reconstructed selected closure; exact automatic effect repair is blocked.");
					CollectionNativeModState native;
					if (!state.Mods.TryGetValue(binding.NativeMod, out native))
						return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
							"A bound native member is missing, so its original install method/root and exact prepared effects cannot be reconstructed automatically.");
					IMod liveMod = ResolveLiveMod(binding.NativeMod.NativeModKey);
					CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), resolvedPlan, binding.MemberKey);
					CollectionVerifiedArchive archive = adopter.TryAdopt(request, cancellationToken);
					if (archive == null)
						return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
							"The exact archive bytes for a bound Collection member are not currently available for verified repair preparation.");
					try
					{
						var context = new ModInstallContext(native.InstallMethod, native.InstallRoot);
						PreparedCollectionNativeRecipe recipe = nativePreparer.PrepareExact(resolvedPlan, member, archive, liveMod,
							_services.ModManager.GameMode, _services.ModManager.EnvironmentInfo, context, state,
							_services.ModManager.EnvironmentInfo.Settings.SkipReadmeFiles, _services.PluginManager, cancellationToken);
						if (!recipe.EffectPreview.IsComplete)
							return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
								"A bound member's native recipe cannot be represented as complete typed effects for automatic verify/repair.");
						prepared.Add(recipe);
					}
					finally
					{
						_referenceStore.ReleaseReference(archive.Reference.ReferenceId);
					}
				}
			}
			catch (Exception ex)
			{
				return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
					"Exact verify/repair preparation failed closed: " + ex.Message);
			}
			return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared, null);
		}

		internal static List<ResolvedCollectionMemberPlan> CreateSelectedMembers(NormalizedCollectionManifest manifest,
			IReadOnlyDictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> artifactChoices)
		{
			if (manifest == null) throw new ArgumentNullException(nameof(manifest));
			if (artifactChoices == null) throw new ArgumentNullException(nameof(artifactChoices));
			var selected = new List<ResolvedCollectionMemberPlan>();
			var consumed = new HashSet<CollectionMemberKey>();
			foreach (NormalizedCollectionMember member in manifest.Members.Where(x => x.IsSelected))
			{
				CollectionResolvedArtifactChoice choice;
				if (member.IdentityResolution.IsResolved && artifactChoices.TryGetValue(member.IdentityResolution.Key, out choice))
				{
					if (choice == null || !member.Artifact.Equals(choice.RequestedArtifact))
						throw new InvalidOperationException("An installed source-policy artifact choice no longer matches its normalized requested artifact.");
					consumed.Add(member.IdentityResolution.Key);
				}
				else
				{
					choice = CollectionResolvedArtifactChoice.Exact(member.Artifact);
				}
				selected.Add(new ResolvedCollectionMemberPlan(member, choice));
			}
			if (consumed.Count != artifactChoices.Count)
				throw new InvalidOperationException("An installed source-policy artifact choice does not belong to the reconstructed selected closure.");
			return selected;
		}

		private static CollectionEffectiveSelection BuildEffectiveSelection(CollectionCapabilityReport capability,
			IList<CollectionMemberBinding> bindings, IList<UserOverride> overrides, IList<CollectionDriftObservation> drift)
		{
			var decisions = new List<CollectionOptionalMemberSelection>();
			foreach (NormalizedCollectionMember member in capability.Manifest.Members)
			{
				if (member.Requirement != CollectionMemberRequirement.Optional || !member.IdentityResolution.IsResolved)
					continue;
				CollectionMemberKey key = member.IdentityResolution.Key;
				bool selected = bindings.Any(x => x.MemberKey.Equals(key));
				UserOverride local = overrides.SingleOrDefault(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(key) &&
					x.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation);
				CollectionDriftObservation observed = drift.SingleOrDefault(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(key) &&
					x.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation);
				CollectionRequirementState expected = local == null ? (observed == null ? null : observed.ExpectedState) : local.UserChosenState;
				if (expected != null)
					selected = expected.Kind == CollectionRequirementStateKind.Present;
				decisions.Add(new CollectionOptionalMemberSelection(key, selected ? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected));
			}
			return new CollectionEffectiveSelectionBuilder().Build(capability, decisions);
		}

		private IMod ResolveLiveMod(string nativeModKey)
		{
			List<IMod> matches = _services.ModManager.InstallationLog.ActiveMods.Where(x => x != null &&
				StringComparer.OrdinalIgnoreCase.Equals(_services.ModManager.InstallationLog.GetModKey(x), nativeModKey)).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException("A bound Collection member no longer resolves to exactly one live native mod.");
			return matches[0];
		}
	}
}
