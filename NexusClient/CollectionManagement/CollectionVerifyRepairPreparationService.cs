using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
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
		private readonly ReadOnlyCollection<CollectionMemberBinding> _effectiveBindings;
		private readonly ReadOnlyCollection<CollectionMemberBinding> _bindingUpdates;

		internal CollectionVerifyRepairPreparationResult(ResolvedCollectionPlan plan,
			IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes, string issue,
			IEnumerable<CollectionMemberBinding> effectiveBindings = null,
			IEnumerable<CollectionMemberBinding> bindingUpdates = null)
		{
			Plan = plan;
			_preparedRecipes = new ReadOnlyCollection<PreparedCollectionNativeRecipe>((preparedRecipes ?? Enumerable.Empty<PreparedCollectionNativeRecipe>()).ToList());
			_effectiveBindings = new ReadOnlyCollection<CollectionMemberBinding>((effectiveBindings ?? Enumerable.Empty<CollectionMemberBinding>()).ToList());
			_bindingUpdates = new ReadOnlyCollection<CollectionMemberBinding>((bindingUpdates ?? Enumerable.Empty<CollectionMemberBinding>()).ToList());
			Issue = issue;
		}

		public ResolvedCollectionPlan Plan { get; }
		public ReadOnlyCollection<PreparedCollectionNativeRecipe> PreparedRecipes { get { return _preparedRecipes; } }
		public ReadOnlyCollection<CollectionMemberBinding> EffectiveBindings { get { return _effectiveBindings; } }
		public ReadOnlyCollection<CollectionMemberBinding> BindingUpdates { get { return _bindingUpdates; } }
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
			CancellationToken cancellationToken, CollectionVerifyRepairReviewedIntent reviewedIntent = null)
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
			var effectiveBindings = new List<CollectionMemberBinding>();
			var bindingUpdates = new List<CollectionMemberBinding>();
			var nativePreparer = new CollectionNativeRecipePreparer(_store, _revisionSourceStore, _artifactStore, _referenceStore);
			var adopter = new CollectionVerifiedArchiveAdopter(new ModManagerCollectionManagedArchiveSource(_services.ModManager),
				new NexusCollectionArchiveIdentityVerifier(repository), _artifactStore, _referenceStore);

			try
			{
				var preparationInputs = new List<VerifyRepairPreparationInput>();
				foreach (CollectionMemberBinding binding in bindingList.OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal))
				{
					ResolvedCollectionMemberPlan member = resolvedPlan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(binding.MemberKey));
					if (member == null)
						return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
							"A current member binding is intentionally outside the reconstructed selected closure; exact automatic effect repair is blocked.",
							effectiveBindings, bindingUpdates);

					CollectionMemberBinding effectiveBinding = binding;
					CollectionNativeModState native;
					bool isBindingUpdate = !state.Mods.TryGetValue(binding.NativeMod, out native);
					IMod liveMod = null;
					ModInstallContext installContext = null;
					if (isBindingUpdate)
					{
						string rebindIssue;
						if (!TryResolveExactReinstalledBinding(association, member, binding, state, out effectiveBinding, out native, out rebindIssue))
						{
							if (HasExactInstalledCandidate(member, state))
								return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared, rebindIssue, effectiveBindings, bindingUpdates);
							EnsureNoOtherInstalledVersion(member, state);
							liveMod = ResolveMissingManagedMod(_services.ModManager, member);
							installContext = new CollectionsOperationStore(_store).GetVerifiedMemberInstallContext(association, binding);
							if (installContext == null)
								return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
									"NMM cannot prove the missing mod's previous install method and folder. Reinstall its exact version from the Mods tab, then run Verify / Repair again.", effectiveBindings, bindingUpdates);
							isBindingUpdate = false;
						}
						else bindingUpdates.Add(effectiveBinding);
					}
					effectiveBindings.Add(effectiveBinding);

					if (liveMod == null) liveMod = ResolveLiveMod(effectiveBinding.NativeMod.NativeModKey);
					if (installContext == null) installContext = new ModInstallContext(native.InstallMethod, native.InstallRoot);
					bool contributesMerge = !member.HasVortexFileList && !member.HasVortexFomodSelection &&
						CollectionNativeRecipePreparer.IsDeterministicModFileMergeContributor(_services.ModManager.GameMode, liveMod);
					preparationInputs.Add(new VerifyRepairPreparationInput(effectiveBinding, member, native, liveMod,
						contributesMerge, isBindingUpdate, installContext));
				}

				IList<IMod> activeMods = _services.ModManager.ActiveMods.ToList();
				CollectionMemberKey mergeOwner = ResolveDeterministicMergeOwner(state, preparationInputs, activeMods);
				if (preparationInputs.Count(x => x.ContributesDeterministicMerge) > 1 && mergeOwner == null)
					return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
						"The shared legacy merged file does not have one current bound contributor owner, so exact multi-member merge repair cannot reproduce the reviewed ownership safely.",
						effectiveBindings, bindingUpdates);

				foreach (VerifyRepairPreparationInput input in preparationInputs)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if ((input.IsBindingUpdate || input.Native == null) && (!CanRebindManualReinstallExactly(input) || (input.Native == null && input.ContributesDeterministicMerge)))
						return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
							input.Native == null
								? "NMM cannot restore this missing mod automatically because its installer or shared-file recipe needs review. Install its exact version from the Mods tab, then run Verify / Repair again."
								: "A manually reinstalled Collection member can only be rebound automatically when its native recipe is basic and deterministic. Scripted installers, Vortex-selected file sets, file overrides, and binary patches require an explicit Collection repair path.",
							effectiveBindings, bindingUpdates);

					CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), resolvedPlan, input.Binding.MemberKey);
					CollectionVerifiedArchive archive = adopter.TryAdopt(request, cancellationToken);
					if (archive == null)
						return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
							"The exact archive bytes for a bound Collection member are not currently available for verified repair preparation.",
							effectiveBindings, bindingUpdates);
					try
					{
						string managedArchivePath = CollectionArchiveContentMatcher.GetManagedArchivePath(input.LiveMod);
						if (!CollectionArchiveContentMatcher.MatchesFile(managedArchivePath, archive.Artifact, cancellationToken))
							return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
								"The mod archive does not match the exact version retained for this Collection. Add the correct archive in the Mods tab, then run Verify / Repair again.",
								effectiveBindings, bindingUpdates);
						CollectionVerifyRepairPreparedRecipeReview frozen = reviewedIntent == null ? null : reviewedIntent.PreparedRecipes.SingleOrDefault(x => x.MemberKey.Equals(input.Member.MemberKey));
						CollectionInstallDestination destination = frozen == null
							? CollectionInstallDestinationResolver.Resolve(_services.ModManager.GameMode, input.Member, input.LiveMod, input.InstallContext, cancellationToken)
							: new CollectionInstallDestination(new ModInstallContext(frozen.InstallMethod, frozen.InstallRoot), frozen.GameRootArchiveBaseDirectory);
						ModInstallContext context = destination.InstallContext;
						bool includeMergeOutput = !input.ContributesDeterministicMerge || mergeOwner == null || input.Member.MemberKey.Equals(mergeOwner);
						PreparedCollectionNativeRecipe recipe = nativePreparer.PrepareExact(resolvedPlan, input.Member, archive, input.LiveMod,
							_services.ModManager.GameMode, _services.ModManager.EnvironmentInfo, context, state,
							_services.ModManager.EnvironmentInfo.Settings.SkipReadmeFiles, _services.PluginManager, activeMods, includeMergeOutput, cancellationToken, destination.GameRootArchiveBaseDirectory);
						recipe = frozen == null
							? CollectionInstallRootCorrection.Prepare(recipe, state, input.Native, _services.ModManager.DeploymentManager, _services.ModManager.VirtualModActivator, cancellationToken)
							: CollectionInstallRootCorrection.Attach(recipe, frozen.InstallRootCorrection);
						if (!recipe.EffectPreview.IsComplete)
							return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared,
								"A bound member's native recipe cannot be represented as complete typed effects for automatic verify/repair.",
								effectiveBindings, bindingUpdates);
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
					"Exact verify/repair preparation failed closed: " + ex.Message, effectiveBindings, bindingUpdates);
			}
			try
			{
				if (reviewedIntent == null) PrepareFolderCorrectionDestinations(prepared, state, effectiveBindings, resolvedPlan, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared, ex.Message, effectiveBindings, bindingUpdates);
			}
			return new CollectionVerifyRepairPreparationResult(resolvedPlan, prepared, null, effectiveBindings, bindingUpdates);
		}

		/// <summary>Reviews new-folder conflicts and retains an already verified higher-priority Collection winner.</summary>
		private void PrepareFolderCorrectionDestinations(IList<PreparedCollectionNativeRecipe> recipes, CollectionNativeStateIndex state,
			IList<CollectionMemberBinding> bindings, ResolvedCollectionPlan plan, CancellationToken cancellationToken)
		{
			for (int index = 0; index < recipes.Count; index++)
			{
				PreparedCollectionNativeRecipe recipe = recipes[index];
				CollectionInstallRootCorrection correction = recipe.EffectPreview.InstallRootCorrection;
				if (correction == null) continue;
				var destinations = new List<CollectionInstallRootDestination>();
				foreach (CollectionPlannedFileEffect effect in recipe.EffectPreview.Files)
				{
					cancellationToken.ThrowIfCancellationRequested();
					CollectionNativeFileState file;
					state.Files.TryGetValue(effect.Target, out file);
					string currentOwner = file == null ? null : file.EffectiveOwnerKey;
					CollectionNativeFileContentEvidence before = CollectionInstallRootCorrection.CaptureFile(effect.Target,
						_services.ModManager.DeploymentManager.GetDeploymentPath(effect.Target));
					bool preserve = false;
					CollectionMemberBinding currentBinding = bindings.SingleOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.NativeMod.NativeModKey, currentOwner));
					if (currentBinding != null && !currentBinding.MemberKey.Equals(recipe.Member.MemberKey))
					{
						PreparedCollectionNativeRecipe currentRecipe = recipes.Single(x => x.Member.MemberKey.Equals(currentBinding.MemberKey));
						CollectionPlannedFileEffect currentEffect = currentRecipe.EffectPreview.Files.SingleOrDefault(x => x.Target.Equals(effect.Target));
						if (currentEffect != null)
						{
							bool cycle;
							CollectionMemberKey winner = CollectionConflictImpactPlanner.ResolveFileWinner(
								new[] { recipe.Member.MemberKey, currentBinding.MemberKey }, plan, out cycle);
							if (cycle || winner == null) throw new InvalidOperationException("Two Collection mods use the same game-folder file without an agreed winner: " + effect.Target.RelativePath);
							preserve = winner.Equals(currentBinding.MemberKey);
							if (preserve && (!before.Existed || !currentEffect.HasExactContentIdentity || before.ByteLength != currentEffect.ExpectedByteLength.Value ||
								!Equals(before.ContentHash, currentEffect.ExpectedContentHash)))
								throw new InvalidOperationException("Repair the Collection mod that supplies this shared file before moving the package: " + effect.Target.RelativePath);
						}
					}
					destinations.Add(new CollectionInstallRootDestination(before, currentOwner, preserve));
				}
				recipes[index] = CollectionInstallRootCorrection.Attach(recipe,
					new CollectionInstallRootCorrection(correction.NativeModKey, correction.InstallMethod, correction.Files, destinations));
			}
		}

		/// <summary>
		/// Resolves one stale member binding to a unique currently installed exact Nexus artifact without publishing it.
		/// The caller must still prove the retained recipe/effects against this candidate before any binding update is persisted.
		/// </summary>
		internal static bool TryResolveExactReinstalledBinding(CollectionTargetAssociation association,
			ResolvedCollectionMemberPlan member, CollectionMemberBinding persistedBinding, CollectionNativeStateIndex state,
			out CollectionMemberBinding effectiveBinding, out CollectionNativeModState native, out string issue)
		{
			if (association == null) throw new ArgumentNullException(nameof(association));
			if (member == null) throw new ArgumentNullException(nameof(member));
			if (persistedBinding == null) throw new ArgumentNullException(nameof(persistedBinding));
			if (state == null) throw new ArgumentNullException(nameof(state));

			effectiveBinding = persistedBinding;
			native = null;
			issue = null;
			if (state.Mods.TryGetValue(persistedBinding.NativeMod, out native))
				return true;

			string domain;
			long modId;
			long fileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out domain, out modId, out fileId) ||
				String.IsNullOrWhiteSpace(domain) || modId <= 0 || fileId <= 0)
			{
				issue = "A bound native member is missing and this artifact type cannot be safely rebound to a manual reinstall.";
				return false;
			}

			List<CollectionNativeModState> candidates = state.Mods.Values.Where(x =>
			{
				long nativeModId;
				long nativeFileId;
				return Int64.TryParse(x.NexusModId, out nativeModId) && Int64.TryParse(x.NexusFileId, out nativeFileId) &&
					nativeModId == modId && nativeFileId == fileId;
			}).ToList();
			if (candidates.Count != 1)
			{
				issue = candidates.Count == 0
					? "The previously bound Collection member is missing and no currently installed native mod has the exact required Nexus mod/file identity."
					: "The previously bound Collection member is missing and multiple installed native mods have the exact required Nexus mod/file identity; automatic rebinding is ambiguous.";
				return false;
			}

			native = candidates[0];
			if (member.RequiresGameRootInstall && native.InstallRoot != ModInstallRoot.GameRoot)
			{
				issue = "The exact manually reinstalled Nexus artifact uses the wrong install root for this Collection member.";
				return false;
			}

			ReadOnlyCollection<CollectionMemberBinding> existingBindings;
			if (state.BindingsByNativeMod.TryGetValue(native.Identity, out existingBindings))
			{
				if (existingBindings.Any(x => !x.VerifiedRecipe.Equals(persistedBinding.VerifiedRecipe)))
				{
					issue = "The exact manually reinstalled Nexus artifact is already associated with a different verified Collection recipe.";
					return false;
				}
				if (existingBindings.Any(x => x.Association.AssociationId == association.AssociationId &&
					x.MemberKey != null && !x.MemberKey.Equals(persistedBinding.MemberKey)))
				{
					issue = "The exact manually reinstalled Nexus artifact is already bound to another member of this Collection association.";
					return false;
				}
			}

			effectiveBinding = new CollectionMemberBinding(association, persistedBinding.MemberKey, native.Identity,
				persistedBinding.VerifiedRecipe, CollectionMemberBindingKind.AdoptedExisting);
			return true;
		}

		private CollectionMemberKey ResolveDeterministicMergeOwner(CollectionNativeStateIndex state,
			IList<VerifyRepairPreparationInput> inputs, IList<IMod> activeMods)
		{
			List<VerifyRepairPreparationInput> contributors = inputs.Where(x => x.ContributesDeterministicMerge).ToList();
			if (contributors.Count == 0) return null;
			if (contributors.Count == 1) return contributors[0].Member.MemberKey;
			var provider = _services.ModManager.GameMode as IDeterministicModFileMergePlanProvider;
			if (provider == null || activeMods == null) return null;

			ModDeploymentTarget target = null;
			foreach (VerifyRepairPreparationInput contributor in contributors)
			{
				DeterministicModFileMergePlan mergePlan =
					provider.GetDeterministicModFileMergePlan(activeMods, contributor.LiveMod);
				if (mergePlan == null) return null;
				ModDeploymentTarget contributorTarget = ModDeploymentTargetResolver.Resolve(_services.ModManager.GameMode,
					contributor.LiveMod, mergePlan.DestinationPath, contributor.InstallContext.InstallRoot);
				if (target == null) target = contributorTarget;
				else if (!target.Equals(contributorTarget)) return null;
			}

			CollectionNativeFileState file;
			if (target == null || !state.Files.TryGetValue(target, out file) || file == null || String.IsNullOrWhiteSpace(file.EffectiveOwnerKey))
				return null;
			List<VerifyRepairPreparationInput> owners = contributors.Where(x =>
				StringComparer.OrdinalIgnoreCase.Equals(x.Binding.NativeMod.NativeModKey, file.EffectiveOwnerKey)).ToList();
			return owners.Count == 1 ? owners[0].Member.MemberKey : null;
		}

		private static bool CanRebindManualReinstallExactly(VerifyRepairPreparationInput input)
		{
			return input != null && !input.LiveMod.HasInstallScript && (input.Native == null || !input.Native.HasInstallScript) && !input.Member.HasVortexFomodSelection &&
				!input.Member.HasVortexFileList && !input.Member.HasVortexFileOverrides && !input.Member.HasVortexBinaryPatches;
		}

		private sealed class VerifyRepairPreparationInput
		{
			public VerifyRepairPreparationInput(CollectionMemberBinding binding, ResolvedCollectionMemberPlan member,
				CollectionNativeModState native, IMod liveMod, bool contributesDeterministicMerge, bool isBindingUpdate, ModInstallContext installContext)
			{
				Binding = binding ?? throw new ArgumentNullException(nameof(binding));
				Member = member ?? throw new ArgumentNullException(nameof(member));
				Native = native;
				InstallContext = installContext ?? throw new ArgumentNullException(nameof(installContext));
				LiveMod = liveMod ?? throw new ArgumentNullException(nameof(liveMod));
				ContributesDeterministicMerge = contributesDeterministicMerge;
				IsBindingUpdate = isBindingUpdate;
			}

			public CollectionMemberBinding Binding { get; }
			public ResolvedCollectionMemberPlan Member { get; }
			public CollectionNativeModState Native { get; }
			public ModInstallContext InstallContext { get; }
			public IMod LiveMod { get; }
			public bool ContributesDeterministicMerge { get; }
			public bool IsBindingUpdate { get; }
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

		/// <summary>Checks for any exact installed candidate before treating an absent binding as a missing member.</summary>
		internal static bool HasExactInstalledCandidate(ResolvedCollectionMemberPlan member, CollectionNativeStateIndex state)
		{
			string domain; long modId; long fileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out domain, out modId, out fileId))
				return true; // Non-Nexus rebinding is not characterized by this repair path.
			return state.Mods.Values.Any(x => x.NexusModId == modId.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
				x.NexusFileId == fileId.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		/// <summary>Rejects missing-member restoration that would silently install alongside another version of the same Nexus mod.</summary>
		internal static void EnsureNoOtherInstalledVersion(ResolvedCollectionMemberPlan member, CollectionNativeStateIndex state)
		{
			string domain; long modId; long fileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out domain, out modId, out fileId))
				throw new InvalidOperationException("The missing member has no exact Nexus mod/file identity.");
			if (state.Mods.Values.Any(x => x.NexusModId == modId.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
				x.NexusFileId != fileId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
				throw new InvalidOperationException("Another version of this mod is installed. Review that version in the Mods tab before repairing the Collection.");
		}

		/// <summary>Resolves an exact archived Nexus version for missing-member preparation; byte identity is separately verified.</summary>
		internal static IMod ResolveMissingManagedMod(ModManager manager, ResolvedCollectionMemberPlan member)
		{
			string domain; long modId; long fileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out domain, out modId, out fileId) ||
				manager.ModRepository == null || !StringComparer.OrdinalIgnoreCase.Equals(domain, manager.ModRepository.GameDomainName))
				throw new InvalidOperationException("The missing mod does not have an exact supported Nexus archive identity for this game.");
			List<IMod> matches = manager.ManagedMods.Where(x => ModManagerCollectionManagedArchiveSource.MatchesRepositoryFileIdentity(
				x, manager.SortOrderService, modId.ToString(System.Globalization.CultureInfo.InvariantCulture),
				fileId.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException(matches.Count == 0
					? "The missing mod's exact archive is not available in NMM. Add that version in the Mods tab, then run Verify / Repair again."
					: "More than one archive matches the missing mod's version. Resolve the duplicate archives in the Mods tab, then run Verify / Repair again.");
			return matches[0];
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
