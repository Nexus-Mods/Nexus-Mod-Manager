using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one exact native recipe/effect preparation independently from the provider-side Collection recipe identity.
	/// </summary>
	public sealed class PreparedCollectionNativeRecipeIdentity : IEquatable<PreparedCollectionNativeRecipeIdentity>
	{
		private PreparedCollectionNativeRecipeIdentity(string fingerprint)
		{
			Fingerprint = CollectionIdentityValidation.RequireOpaqueToken(fingerprint, nameof(fingerprint));
		}

		/// <summary>Gets the deterministic prepared-native recipe fingerprint.</summary>
		public string Fingerprint { get; }

		/// <summary>Creates an identity from an already calculated opaque fingerprint.</summary>
		public static PreparedCollectionNativeRecipeIdentity FromFingerprint(string fingerprint)
		{
			return new PreparedCollectionNativeRecipeIdentity(fingerprint);
		}

		/// <inheritdoc />
		public bool Equals(PreparedCollectionNativeRecipeIdentity other)
		{
			return !ReferenceEquals(other, null) && StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as PreparedCollectionNativeRecipeIdentity);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return StringComparer.Ordinal.GetHashCode(Fingerprint ?? String.Empty);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Fingerprint;
		}
	}

	/// <summary>
	/// Immutable output of translating one exact retained Collection member source into a bounded native C5 recipe and C6 effect preview.
	/// </summary>
	public sealed class PreparedCollectionNativeRecipe
	{
		private readonly ReadOnlyCollection<string> _retainedArtifactIds;

		internal PreparedCollectionNativeRecipe(ResolvedCollectionMemberPlan member,
			PreparedCollectionNativeRecipeIdentity preparedNativeIdentity, ModInstallationRecipeInput recipeInput,
			CollectionMemberEffectPreview effectPreview, bool skipReadmeFiles, IEnumerable<string> retainedArtifactIds)
		{
			Member = member ?? throw new ArgumentNullException(nameof(member));
			PreparedNativeIdentity = preparedNativeIdentity ?? throw new ArgumentNullException(nameof(preparedNativeIdentity));
			RecipeInput = recipeInput ?? throw new ArgumentNullException(nameof(recipeInput));
			EffectPreview = effectPreview ?? throw new ArgumentNullException(nameof(effectPreview));
			SkipReadmeFiles = skipReadmeFiles;
			if (!recipeInput.HasNativePlan)
				throw new ArgumentException("A prepared Collection native recipe requires translated C5 operations.", nameof(recipeInput));
			if (!effectPreview.IsComplete)
				throw new ArgumentException("A prepared Collection native recipe requires a complete exact effect preview.", nameof(effectPreview));
			if (!StringComparer.Ordinal.Equals(member.RecipeIdentity.Fingerprint, recipeInput.RecipeFingerprint) ||
				!member.RecipeIdentity.Equals(effectPreview.RecipeIdentity))
				throw new ArgumentException("Prepared native recipe identities must remain bound to the exact provider recipe.");

			List<string> artifacts = (retainedArtifactIds ?? throw new ArgumentNullException(nameof(retainedArtifactIds))).ToList();
			if (artifacts.Count == 0 || artifacts.Any(String.IsNullOrWhiteSpace) || artifacts.Distinct(StringComparer.Ordinal).Count() != artifacts.Count)
				throw new ArgumentException("Prepared native recipe retained inputs must contain unique stable artifact identities.", nameof(retainedArtifactIds));
			_retainedArtifactIds = new ReadOnlyCollection<string>(artifacts);
		}

		/// <summary>Gets the exact resolved Collection member represented by this preparation.</summary>
		public ResolvedCollectionMemberPlan Member { get; }

		/// <summary>Gets the provider-side recipe identity retained for provenance.</summary>
		public CollectionRecipeIdentity ProviderRecipeIdentity { get { return Member.RecipeIdentity; } }

		/// <summary>Gets the deterministic identity of the concrete NMM-native output preparation.</summary>
		public PreparedCollectionNativeRecipeIdentity PreparedNativeIdentity { get; }

		/// <summary>Gets the exact native method/root captured during preparation.</summary>
		public ModInstallContext InstallContext { get { return RecipeInput.InstallContext; } }

		/// <summary>Gets the C5 adapter identifier used by this prepared recipe.</summary>
		public string AdapterId { get { return RecipeInput.Validation.AdapterId; } }

		/// <summary>Gets the C5 adapter version used by this prepared recipe.</summary>
		public int AdapterVersion { get { return RecipeInput.Validation.AdapterVersion; } }

		/// <summary>Gets the immutable C5 validation metadata.</summary>
		public ModInstallationRecipeValidation Validation { get { return RecipeInput.Validation; } }

		/// <summary>Gets the translated C5 recipe input ready for one later C6.15.6 identity rebind.</summary>
		public ModInstallationRecipeInput RecipeInput { get; }

		/// <summary>Gets the exact C6 effect preview corresponding to the translated C5 operations.</summary>
		public CollectionMemberEffectPreview EffectPreview { get; }

		/// <summary>Gets the exact readme-suppression setting used while preparing the native recipe.</summary>
		public bool SkipReadmeFiles { get; }

		/// <summary>Gets the retained immutable inputs required to reproduce this preparation after restart.</summary>
		public ReadOnlyCollection<string> RetainedArtifactIds { get { return _retainedArtifactIds; } }
	}

	/// <summary>
	/// Translates characterized Collection recipe source into validated native C5 recipe input without executing native mutation.
	/// </summary>
	/// <remarks>
	/// Supports the fixture-characterized ordinary/basic exact-layout path and exact Vortex FOMOD choices which can be
	/// translated by NMM's native XML/FOMOD adapter into a pure one-to-one file plan. Hash/file-list recipes, patches,
	/// fileOverrides and non-file FOMOD effects remain blocked by the existing capability/preparation gates.
	/// </remarks>
	public sealed class CollectionNativeRecipePreparer
	{
		private const string PreparedIdentityFormat = "nmm-ce.collections.prepared-native-recipe/1";
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly NexusCollectionManifestNormalizer _normalizer;
		private readonly BasicInstallPlanBuilder _basicInstallPlanBuilder;
		private readonly ModInstallationSimpleFileRecipeAdapter _simpleFileAdapter;
		private readonly CollectionMemberEffectPreviewBuilder _effectPreviewBuilder;

		/// <summary>Creates the initial Collection-to-native recipe preparation service over the existing C4/C5/C6 seams.</summary>
		public CollectionNativeRecipePreparer(CollectionsStore store)
			: this(new CollectionsCatalogStore(store), new CollectionsRevisionSourceStore(store),
				new CollectionsRetainedArtifactStore(store), new CollectionsRetainedArtifactReferenceStore(store), new NexusCollectionManifestNormalizer(),
				new BasicInstallPlanBuilder(), new ModInstallationSimpleFileRecipeAdapter(),
				new CollectionMemberEffectPreviewBuilder())
		{
		}

		/// <summary>Creates a recipe preparer which shares the additive workflow's retained-artifact verification caches.</summary>
		internal CollectionNativeRecipePreparer(CollectionsStore store, CollectionsRevisionSourceStore revisionSourceStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(new CollectionsCatalogStore(store), revisionSourceStore, artifactStore, referenceStore,
				new NexusCollectionManifestNormalizer(), new BasicInstallPlanBuilder(), new ModInstallationSimpleFileRecipeAdapter(),
				new CollectionMemberEffectPreviewBuilder())
		{
		}

		internal CollectionNativeRecipePreparer(CollectionsCatalogStore catalogStore,
			CollectionsRevisionSourceStore revisionSourceStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, NexusCollectionManifestNormalizer normalizer, BasicInstallPlanBuilder basicInstallPlanBuilder,
			ModInstallationSimpleFileRecipeAdapter simpleFileAdapter, CollectionMemberEffectPreviewBuilder effectPreviewBuilder)
		{
			_catalogStore = catalogStore ?? throw new ArgumentNullException(nameof(catalogStore));
			_revisionSourceStore = revisionSourceStore ?? throw new ArgumentNullException(nameof(revisionSourceStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
			_basicInstallPlanBuilder = basicInstallPlanBuilder ?? throw new ArgumentNullException(nameof(basicInstallPlanBuilder));
			_simpleFileAdapter = simpleFileAdapter ?? throw new ArgumentNullException(nameof(simpleFileAdapter));
			_effectPreviewBuilder = effectPreviewBuilder ?? throw new ArgumentNullException(nameof(effectPreviewBuilder));
		}

		/// <summary>
		/// Prepares one exact characterized Collection member using either the ordinary native simple-file path or the bounded
		/// bounded characterized Vortex-FOMOD translation path.
		/// </summary>
		public PreparedCollectionNativeRecipe PrepareExact(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive verifiedArchive, IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, ModInstallContext installContext, CollectionNativeStateIndex currentState,
			bool skipReadmeFiles, IPluginManager pluginManager = null,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (member == null)
				throw new ArgumentNullException(nameof(member));
			if (!member.HasVortexFomodSelection)
			{
				return PrepareBasicSimpleExactCore(plan, member, verifiedArchive, mod, gameMode, installContext, currentState,
					skipReadmeFiles, pluginManager, false, null, cancellationToken);
			}

			return PrepareVortexFomodExact(plan, member, verifiedArchive, mod, gameMode, environmentInfo, installContext,
				currentState, skipReadmeFiles, pluginManager, false, null, cancellationToken);
		}

		/// <summary>
		/// Re-prepares one exact incoming replacement recipe against the explicitly observed C8.5 condition environment.
		/// This path is read-only and never authorizes or submits native installation.
		/// </summary>
		public PreparedCollectionNativeRecipe PrepareReplacementExact(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive verifiedArchive, IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, ModInstallContext installContext, CollectionNativeStateIndex currentState,
			CollectionReplacementEnvironmentProjection conditionEnvironment, bool skipReadmeFiles, IPluginManager pluginManager = null,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (member == null) throw new ArgumentNullException(nameof(member));
			if (conditionEnvironment == null) throw new ArgumentNullException(nameof(conditionEnvironment));
			if (!member.HasVortexFomodSelection)
			{
				return PrepareBasicSimpleExactCore(plan, member, verifiedArchive, mod, gameMode, installContext, currentState,
					skipReadmeFiles, pluginManager, true, conditionEnvironment, cancellationToken);
			}

			return PrepareVortexFomodExact(plan, member, verifiedArchive, mod, gameMode, environmentInfo, installContext,
				currentState, skipReadmeFiles, pluginManager, true, conditionEnvironment, cancellationToken);
		}

		/// <summary>
		/// Prepares the characterized ordinary/basic exact-layout member path into explicit native file operations and exact effects.
		/// </summary>
		public PreparedCollectionNativeRecipe PrepareBasicSimpleExact(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive verifiedArchive, IMod mod, IGameMode gameMode,
			ModInstallContext installContext, CollectionNativeStateIndex currentState, bool skipReadmeFiles,
			IPluginManager pluginManager = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			return PrepareBasicSimpleExactCore(plan, member, verifiedArchive, mod, gameMode, installContext, currentState,
				skipReadmeFiles, pluginManager, false, null, cancellationToken);
		}

		private PreparedCollectionNativeRecipe PrepareBasicSimpleExactCore(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive verifiedArchive, IMod mod, IGameMode gameMode,
			ModInstallContext installContext, CollectionNativeStateIndex currentState, bool skipReadmeFiles,
			IPluginManager pluginManager, bool replacement, CollectionReplacementEnvironmentProjection conditionEnvironment,
			CancellationToken cancellationToken)
		{
			ValidateInputs(plan, member, verifiedArchive, mod, gameMode, installContext, currentState, replacement, conditionEnvironment);
			if (member.HasVortexFomodSelection)
				throw new NotSupportedException("Vortex FOMOD choices require the exact FOMOD-aware native preparation path.");
			cancellationToken.ThrowIfCancellationRequested();

			CollectionRevision revision = _catalogStore.GetRevision(plan.Revision);
			if (revision == null)
				throw new InvalidOperationException("The exact Collection revision must remain persisted during native recipe preparation.");

			byte[] rawManifestBytes = _revisionSourceStore.LoadManifest(plan.Revision, plan.ManifestSource);
			NexusCollectionManifestNormalizationResult normalized = _normalizer.Normalize(rawManifestBytes, revision);
			ValidateRetainedSourceMember(plan, member, normalized);

			CollectionRevisionSourceRecord sourceRecord = _revisionSourceStore.GetSource(plan.Revision);
			if (sourceRecord == null || String.IsNullOrEmpty(sourceRecord.RawManifestArtifactId))
				throw new InvalidDataException("The retained Collection revision source is missing its immutable manifest artifact binding.");

			ValidateVerifiedArchive(verifiedArchive, cancellationToken);
			ValidateManagedModArchive(mod, verifiedArchive, cancellationToken);
			ValidateCharacterizedInstallRootBehavior(member, mod);

			BasicInstallPlanResult basicResult = _basicInstallPlanBuilder.Build(mod, gameMode, installContext, skipReadmeFiles);
			if (!basicResult.IsSupported)
			{
				throw new NotSupportedException(String.Format("The Collection member cannot use the characterized basic/simple adapter: {0} ({1}).",
					basicResult.Message, basicResult.UnsupportedReason));
			}

			// Re-check after archive enumeration/planning so a mutable library archive cannot change unnoticed during preparation.
			ValidateManagedModArchive(mod, verifiedArchive, cancellationToken);

			ModInstallationSimpleFileRecipe simpleRecipe = basicResult.Plan.CreateSimpleFileRecipe();
			ModInstallationRecipeValidation validation = CreateValidation(verifiedArchive.Artifact, installContext, simpleRecipe);
			var fingerprint = new ModOperationFingerprint(plan.Target.Fingerprint, installContext, member.RecipeIdentity.Fingerprint);
			ModOperationIdentity preparationOperation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint);
			var untranslated = new ModInstallationRecipeInput(preparationOperation, validation);
			ModInstallationRecipeInput translated = _simpleFileAdapter.Translate(untranslated, simpleRecipe);
			CollectionMemberEffectPreview effectPreview = _effectPreviewBuilder.Build(member, translated, gameMode, mod, pluginManager);
			if (!effectPreview.IsComplete)
				throw new NotSupportedException("The translated basic/simple Collection recipe does not have a complete characterized C6 effect preview.");

			PreparedCollectionNativeRecipeIdentity preparedIdentity = BuildPreparedIdentity(plan, member,
				verifiedArchive.Artifact, basicResult.Plan, translated, effectPreview, skipReadmeFiles);
			return new PreparedCollectionNativeRecipe(member, preparedIdentity, translated, effectPreview, skipReadmeFiles,
				new[] { sourceRecord.RawManifestArtifactId, verifiedArchive.Artifact.ArtifactId });
		}

		private PreparedCollectionNativeRecipe PrepareVortexFomodExact(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive verifiedArchive, IMod mod, IGameMode gameMode,
			IEnvironmentInfo environmentInfo, ModInstallContext installContext, CollectionNativeStateIndex currentState,
			bool skipReadmeFiles, IPluginManager pluginManager, bool replacement,
			CollectionReplacementEnvironmentProjection conditionEnvironment, CancellationToken cancellationToken)
		{
			ValidateInputs(plan, member, verifiedArchive, mod, gameMode, installContext, currentState, replacement, conditionEnvironment);
			if (environmentInfo == null)
				throw new ArgumentNullException(nameof(environmentInfo));
			if (!member.HasVortexFomodSelection)
				throw new ArgumentException("The FOMOD-aware preparation path requires characterized Vortex installer choices.", nameof(member));
			if (member.InstallRootBehavior != CollectionMemberInstallRootBehavior.Default)
				throw new NotSupportedException("Vortex FOMOD choice replay combined with a game-root mod type has no characterized native installer-priority translation.");
			cancellationToken.ThrowIfCancellationRequested();

			CollectionRevision revision = _catalogStore.GetRevision(plan.Revision);
			if (revision == null)
				throw new InvalidOperationException("The exact Collection revision must remain persisted during native recipe preparation.");

			byte[] rawManifestBytes = _revisionSourceStore.LoadManifest(plan.Revision, plan.ManifestSource);
			NexusCollectionManifestNormalizationResult normalized = _normalizer.Normalize(rawManifestBytes, revision);
			ValidateRetainedSourceMember(plan, member, normalized);

			CollectionRevisionSourceRecord sourceRecord = _revisionSourceStore.GetSource(plan.Revision);
			if (sourceRecord == null || String.IsNullOrEmpty(sourceRecord.RawManifestArtifactId))
				throw new InvalidDataException("The retained Collection revision source is missing its immutable manifest artifact binding.");

			ValidateVerifiedArchive(verifiedArchive, cancellationToken);
			ValidateManagedModArchive(mod, verifiedArchive, cancellationToken);
			ValidateCharacterizedInstallRootBehavior(member, mod);

			IModInstallationFomodRecipePlanningAdapter fomodAdapter = mod.InstallScript == null
				? null
				: mod.InstallScript.Type as IModInstallationFomodRecipePlanningAdapter;
			if (!mod.HasInstallScript || fomodAdapter == null)
				throw new NotSupportedException("The Collection member records Vortex FOMOD choices, but the verified archive does not expose NMM's native exact FOMOD planning adapter.");

			ModInstallationFomodSelectionRecipe fomodRecipe;
			IReadOnlyList<ModInstallationRecipePath> fomodPaths;
			ModInstallationRecipeInput fomodTranslated;
			try
			{
				Version scriptVersion = fomodAdapter.GetScriptVersion(mod);
				fomodRecipe = CreateFomodRecipe(member.VortexFomodSelection, scriptVersion);
				fomodPaths = conditionEnvironment == null
					? fomodAdapter.GetValidationPaths(mod, gameMode, environmentInfo, pluginManager, fomodRecipe)
					: fomodAdapter.GetValidationPaths(mod, gameMode, environmentInfo, pluginManager, fomodRecipe, conditionEnvironment);
				ModInstallationRecipeValidation fomodValidation = CreateFomodValidation(verifiedArchive.Artifact, installContext,
					fomodAdapter, fomodPaths);
				var fingerprint = new ModOperationFingerprint(plan.Target.Fingerprint, installContext, member.RecipeIdentity.Fingerprint);
				ModOperationIdentity operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint);
				fomodTranslated = conditionEnvironment == null
					? fomodAdapter.Translate(new ModInstallationRecipeInput(operation, fomodValidation), mod,
						gameMode, environmentInfo, pluginManager, fomodRecipe)
					: fomodAdapter.Translate(new ModInstallationRecipeInput(operation, fomodValidation), mod,
						gameMode, environmentInfo, pluginManager, fomodRecipe, conditionEnvironment);
			}
			catch (DependencyException ex)
			{
				throw new NotSupportedException("The exact FOMOD selection has an unmet native installer prerequisite: " + ex.Message, ex);
			}

			// Re-check after the actual native FOMOD definition has been enumerated and evaluated.
			ValidateManagedModArchive(mod, verifiedArchive, cancellationToken);

			ModInstallationSimpleFileRecipe frozenRecipe = FreezePureFileFomodPlan(fomodTranslated, gameMode, normalized.Manifest);
			ModInstallationRecipeValidation simpleValidation = CreateValidation(verifiedArchive.Artifact, installContext, frozenRecipe);
			var finalFingerprint = new ModOperationFingerprint(plan.Target.Fingerprint, installContext, member.RecipeIdentity.Fingerprint);
			ModOperationIdentity finalOperation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, finalFingerprint);
			ModInstallationRecipeInput translated = _simpleFileAdapter.Translate(
				new ModInstallationRecipeInput(finalOperation, simpleValidation), frozenRecipe);
			CollectionMemberEffectPreview effectPreview = _effectPreviewBuilder.Build(member, translated, gameMode, mod, pluginManager);
			if (!effectPreview.IsComplete)
				throw new NotSupportedException("The translated Vortex FOMOD selection does not have a complete characterized C6 effect preview.");

			PreparedCollectionNativeRecipeIdentity preparedIdentity = BuildPreparedFomodIdentity(plan, member,
				verifiedArchive.Artifact, fomodAdapter, fomodRecipe, frozenRecipe, translated, effectPreview, skipReadmeFiles);
			return new PreparedCollectionNativeRecipe(member, preparedIdentity, translated, effectPreview, skipReadmeFiles,
				new[] { sourceRecord.RawManifestArtifactId, verifiedArchive.Artifact.ArtifactId });
		}

		private static void ValidateInputs(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			CollectionVerifiedArchive verifiedArchive, IMod mod, IGameMode gameMode, ModInstallContext installContext,
			CollectionNativeStateIndex currentState, bool replacement, CollectionReplacementEnvironmentProjection conditionEnvironment)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			if (member == null)
				throw new ArgumentNullException(nameof(member));
			if (verifiedArchive == null)
				throw new ArgumentNullException(nameof(verifiedArchive));
			if (mod == null)
				throw new ArgumentNullException(nameof(mod));
			if (gameMode == null)
				throw new ArgumentNullException(nameof(gameMode));
			if (installContext == null)
				throw new ArgumentNullException(nameof(installContext));
			if (currentState == null)
				throw new ArgumentNullException(nameof(currentState));
			if (replacement)
			{
				if (plan.Policy.Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
					throw new NotSupportedException("Replacement recipe re-preparation requires the explicit replacement policy.");
				if (conditionEnvironment == null || !conditionEnvironment.Diff.PlanIdentity.Equals(plan.Identity) ||
					!conditionEnvironment.Diff.Target.Equals(plan.Target))
					throw new InvalidOperationException("Replacement recipe re-preparation requires the exact C8 replacement environment for this plan and target.");
				if (!conditionEnvironment.IsReadyForSupportedConditions)
					throw new NotSupportedException("Replacement recipe re-preparation cannot evaluate against an unprovable condition environment.");
				if (!plan.Target.Equals(currentState.Target))
					throw new InvalidOperationException("Replacement recipe re-preparation requires current native state from the reviewed target.");
			}
			else
			{
				if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
					throw new NotSupportedException("C6.15.9 native recipe preparation is additive-only unless the explicit C8 replacement re-preparation path is used.");
				if (!plan.Target.Equals(currentState.Target) || !plan.CurrentStateFingerprint.Equals(currentState.Fingerprint))
					throw new InvalidOperationException("Native recipe preparation requires the exact C6.1 state snapshot already bound to the resolved plan.");
			}

			ResolvedCollectionMemberPlan selected = plan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(member.MemberKey));
			if (selected == null || selected.SourceOrdinal != member.SourceOrdinal ||
				selected.InstallRootBehavior != member.InstallRootBehavior ||
				!Equals(selected.VortexFomodSelection, member.VortexFomodSelection) ||
				!selected.RecipeIdentity.Equals(member.RecipeIdentity) || !selected.ArtifactChoice.Equals(member.ArtifactChoice))
				throw new ArgumentException("The member being prepared must belong to the exact selected resolved plan.", nameof(member));
			if (member.ArtifactChoice.Kind != CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact)
				throw new NotSupportedException("The initial C6.15.9 preparation capability accepts exact requested artifacts only.");
			if (member.RequiresGameRootInstall && installContext.InstallRoot != ModInstallRoot.GameRoot)
				throw new ArgumentException("The characterized Vortex member type requires NMM's native game-root install context.", nameof(installContext));
			if (member.RequiresGameRootInstall && !gameMode.SupportsGameRootModInstall)
				throw new NotSupportedException("The current game mode does not support NMM's native game-root installation mode required by this Collection member.");

			CollectionAcquisitionRequest request = verifiedArchive.Request;
			if (!request.PlanIdentity.Equals(plan.Identity) || !request.Revision.Equals(plan.Revision) ||
				!request.Target.Equals(plan.Target) || !request.MemberKey.Equals(member.MemberKey) ||
				!request.SelectedArtifact.Equals(member.ArtifactChoice.SelectedArtifact) ||
				!request.RecipeIdentity.Equals(member.RecipeIdentity))
				throw new ArgumentException("The verified archive does not belong to the exact resolved member plan being prepared.", nameof(verifiedArchive));
		}

		private static void ValidateRetainedSourceMember(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			NexusCollectionManifestNormalizationResult normalized)
		{
			if (normalized == null || normalized.Manifest == null)
				throw new InvalidDataException("The retained Collection recipe source could not be normalized.");
			if (!normalized.Manifest.Revision.Equals(plan.Revision) || !normalized.Manifest.Source.Equals(plan.ManifestSource))
				throw new InvalidDataException("The retained Collection recipe source no longer matches the resolved plan provenance.");

			NormalizedCollectionMember sourceMember = normalized.Manifest.Members.SingleOrDefault(x => x.SourceOrdinal == member.SourceOrdinal);
			if (sourceMember == null || !sourceMember.IdentityResolution.IsResolved ||
				!sourceMember.IdentityResolution.Key.Equals(member.MemberKey) ||
				sourceMember.InstallRootBehavior != member.InstallRootBehavior ||
				!Equals(sourceMember.VortexFomodSelection, member.VortexFomodSelection) ||
				!Equals(sourceMember.Artifact, member.ArtifactChoice.RequestedArtifact) ||
				!Equals(sourceMember.RecipeIdentity, member.RecipeIdentity))
				throw new InvalidDataException("The retained Collection source ordinal does not reproduce the exact resolved member artifact and recipe identity.");

			if (normalized.CapabilityReport.ManifestIssues.Count != 0)
				throw new NotSupportedException("The retained Collection source contains manifest-level behavior outside the characterized C6.15.9 preparation capability.");
			CollectionMemberCapabilityReport sourceReport = normalized.CapabilityReport.MemberReports
				.SingleOrDefault(x => x.Member.SourceOrdinal == member.SourceOrdinal);
			CollectionMemberCapabilityReport reviewedReport = plan.CapabilityReport.MemberReports
				.SingleOrDefault(x => x.Member.SourceOrdinal == member.SourceOrdinal);
			if (sourceReport == null ||
				(sourceReport.Status != CollectionCompatibilityStatus.Supported &&
				 !IsReviewedPreferExactResolution(member, sourceReport, reviewedReport)))
				throw new NotSupportedException("The retained Collection member source contains behavior outside the characterized native recipe preparation capability.");
		}

		private static bool IsReviewedPreferExactResolution(ResolvedCollectionMemberPlan member,
			CollectionMemberCapabilityReport sourceReport, CollectionMemberCapabilityReport reviewedReport)
		{
			if (member == null || sourceReport == null || reviewedReport == null ||
				reviewedReport.Status != CollectionCompatibilityStatus.Supported ||
				member.ArtifactChoice.Kind != CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact ||
				!member.ArtifactChoice.RequestedArtifact.Equals(member.ArtifactChoice.SelectedArtifact) ||
				sourceReport.Status != CollectionCompatibilityStatus.ActionRequired || sourceReport.Issues.Count == 0)
				return false;

			foreach (CollectionCapabilityIssue issue in sourceReport.Issues)
			{
				if (!StringComparer.Ordinal.Equals(issue.Code, CollectionNexusPreferExactPolicyResolver.PreferIssueCode))
					return false;
			}

			return true;
		}

		private void ValidateVerifiedArchive(CollectionVerifiedArchive verifiedArchive, CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReference(verifiedArchive.Reference.ReferenceId);
			if (reference == null || !reference.Equals(verifiedArchive.Reference) ||
				reference.OwnerKind != CollectionsRetainedArtifactOwnerKind.Download ||
				!StringComparer.Ordinal.Equals(reference.OwnerId, verifiedArchive.Request.RequestId.ToString("D")) ||
				!StringComparer.Ordinal.Equals(reference.ArtifactId, verifiedArchive.Artifact.ArtifactId))
				throw new InvalidDataException("The verified Collection archive no longer has its exact durable acquisition reference.");

			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(verifiedArchive.Artifact.ArtifactId);
			if (artifact == null || !artifact.Equals(verifiedArchive.Artifact))
				throw new InvalidDataException("The verified Collection archive no longer matches retained artifact metadata.");
			if (artifact.ContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new NotSupportedException("Native recipe preparation requires a SHA-256 verified immutable archive.");
			CollectionContentHash expectedHash = verifiedArchive.Request.SelectedArtifact.ExpectedContentHash;
			if (expectedHash != null && !expectedHash.Equals(artifact.ContentHash))
				throw new InvalidDataException("The retained verified archive no longer matches the manifest-selected expected content hash.");
			if (CanDeferRepeatedPrepareVerification(verifiedArchive))
			{
				// The retained bytes were cryptographically/provider verified by an earlier acquisition. Repeated Download/Prepare
				// does not consume those payload bytes, so validate sealed metadata/path/length here and leave the full digest
				// check to GetReview/apply/recovery. This avoids rereading multi-gigabyte blobs merely to redraw a review.
				using (Stream retained = _artifactStore.OpenRead(artifact.ArtifactId))
				{
					cancellationToken.ThrowIfCancellationRequested();
				}
				return;
			}

			if (!_artifactStore.VerifyArtifact(artifact.ArtifactId, cancellationToken))
				throw new InvalidDataException("The retained verified archive failed its integrity check before recipe preparation.");
		}

		private void ValidateManagedModArchive(IMod mod, CollectionVerifiedArchive verifiedArchive,
			CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifact expectedArtifact = verifiedArchive.Artifact;
			string path = !String.IsNullOrWhiteSpace(mod.ModArchivePath) ? mod.ModArchivePath : mod.Filename;
			if (String.IsNullOrWhiteSpace(path))
				throw new InvalidDataException("The managed mod has no archive path for exact recipe preparation.");
			path = Path.GetFullPath(path);
			if (!File.Exists(path))
				throw new FileNotFoundException("The managed mod archive required for recipe preparation is missing.", path);

			var info = new FileInfo(path);
			if (info.Length != expectedArtifact.ByteLength)
				throw new InvalidDataException("The managed mod archive length no longer matches the previously verified Collection archive.");

			// Adoption just streamed this exact source into retained storage while calculating SHA-256. Reuse that process-local
			// proof while size/write-time are unchanged; the native installer still performs the final SHA-256 before mutation.
			if (_artifactStore.IsPublishedSourceCurrent(path, expectedArtifact))
				return;

			// For a provider artifact already verified by a previous Prepare, C6.2/ResolveManagedMod has already selected the
			// exact Nexus mod/file identity. Re-hashing the whole archive here makes review O(total archive bytes) even though
			// the native installer's final SHA-256 gate will reject any changed bytes before mutation. Keep Prepare cheap and
			// defer the cryptographic recheck to that mutation boundary.
			if (CanDeferRepeatedPrepareVerification(verifiedArchive))
				return;

			if (!CollectionArchiveContentMatcher.MatchesFile(path, expectedArtifact, cancellationToken))
				throw new InvalidDataException("The managed mod archive bytes do not match the verified immutable Collection archive.");
		}

		private static bool CanDeferRepeatedPrepareVerification(CollectionVerifiedArchive verifiedArchive)
		{
			if (verifiedArchive == null ||
				verifiedArchive.SourceKind != CollectionVerifiedArchiveSourceKind.RetainedContent ||
				verifiedArchive.VerificationBasis != CollectionArchiveVerificationBasis.ExistingVerifiedReference)
				return false;

			string domain;
			long modId;
			long fileId;
			return NexusCollectionModFileArtifactIdentity.TryParse(verifiedArchive.Request.SelectedArtifact,
				out domain, out modId, out fileId);
		}

		private static void ValidateCharacterizedInstallRootBehavior(ResolvedCollectionMemberPlan member, IMod mod)
		{
			if (member.InstallRootBehavior != CollectionMemberInstallRootBehavior.VortexDInputGameRoot)
				return;

			List<string> archiveEntries = mod.GetFileList() ?? new List<string>();
			List<string> files = archiveEntries
				.Where(path => !String.IsNullOrWhiteSpace(path) && !IsArchiveDirectoryEntry(path))
				.Select(NormalizePortableArchivePath)
				.ToList();
			List<string> dinputMarkers = files
				.Where(path => GetPortableFileName(path).Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase))
				.ToList();

			// With no dinput8.dll there is no dinput-specific Vortex subtree rule to reproduce. The member
			// stays on NMM's already-characterized native GameRoot installation path.
			if (dinputMarkers.Count == 0)
				return;
			if (dinputMarkers.Count != 1)
			{
				throw new NotSupportedException(
					"The Vortex dinput member contains more than one dinput8.dll candidate, so its installer base directory is ambiguous.");
			}

			string baseDirectory = GetPortableDirectoryName(dinputMarkers[0]);
			if (String.IsNullOrEmpty(baseDirectory))
				return;

			// NMM's existing root installer can exactly reproduce Vortex's dinput subtree behavior when
			// dinput8.dll sits in one common archive wrapper. Deeper/split layouts remain fail-closed.
			if (baseDirectory.IndexOf('\\') >= 0)
			{
				throw new NotSupportedException(
					"The Vortex dinput member uses a nested dinput8.dll base directory that NMM's native root installer cannot reproduce exactly.");
			}

			string prefix = baseDirectory + "\\";
			if (files.Any(path => !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
			{
				throw new NotSupportedException(
					"The Vortex dinput member contains files outside the dinput8.dll base directory; NMM will not guess which archive entries Vortex would omit.");
			}
		}

		private static bool IsArchiveDirectoryEntry(string path)
		{
			return path.EndsWith("\\", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal);
		}

		private static string NormalizePortableArchivePath(string path)
		{
			return path.Replace('/', '\\').TrimStart('\\');
		}

		private static string GetPortableFileName(string path)
		{
			int index = path.LastIndexOf('\\');
			return index < 0 ? path : path.Substring(index + 1);
		}

		private static string GetPortableDirectoryName(string path)
		{
			int index = path.LastIndexOf('\\');
			return index <= 0 ? String.Empty : path.Substring(0, index);
		}

		private static ModInstallationFomodSelectionRecipe CreateFomodRecipe(CollectionVortexFomodSelection selection,
			Version scriptVersion)
		{
			if (selection == null)
				throw new ArgumentNullException(nameof(selection));
			var steps = new List<ModInstallationFomodStepSelection>();
			for (int stepIndex = 0; stepIndex < selection.Steps.Count; stepIndex++)
			{
				CollectionVortexFomodStepSelection sourceStep = selection.Steps[stepIndex];
				var groups = new List<ModInstallationFomodGroupSelection>();
				for (int groupIndex = 0; groupIndex < sourceStep.Groups.Count; groupIndex++)
				{
					CollectionVortexFomodGroupSelection sourceGroup = sourceStep.Groups[groupIndex];
					groups.Add(new ModInstallationFomodGroupSelection(groupIndex, sourceGroup.Name,
						sourceGroup.Choices.Select(choice => new ModInstallationFomodOptionSelection(choice.Index, choice.Name))));
				}
				steps.Add(new ModInstallationFomodStepSelection(stepIndex, sourceStep.Name, groups));
			}
			return new ModInstallationFomodSelectionRecipe(scriptVersion, steps);
		}

		private static ModInstallationRecipeValidation CreateFomodValidation(CollectionsRetainedArtifact artifact,
			ModInstallContext installContext, IModInstallationFomodRecipePlanningAdapter adapter,
			IEnumerable<ModInstallationRecipePath> paths)
		{
			return new ModInstallationRecipeValidation(adapter.AdapterId, adapter.AdapterVersion, installContext,
				new ModInstallationRecipeExpectedContent(artifact.ContentHash.Value, artifact.ByteLength),
				new[] { new ModInstallationRecipeCapability(adapter.CapabilityId, adapter.CapabilityVersion) },
				paths ?? throw new ArgumentNullException(nameof(paths)));
		}

		private static ModInstallationSimpleFileRecipe FreezePureFileFomodPlan(ModInstallationRecipeInput translated,
			IGameMode gameMode, NormalizedCollectionManifest manifest)
		{
			if (translated == null || !translated.HasNativePlan)
				throw new InvalidDataException("The native FOMOD adapter did not produce an executable operation plan.");
			if (manifest == null)
				throw new ArgumentNullException(nameof(manifest));

			var pluginFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (InstallModFileOperation file in translated.NativeOperations.OfType<InstallModFileOperation>())
			{
				if (!IsGamePluginPath(file.DestinationPath, gameMode))
					continue;

				string pluginName = GetPortableFileName(NormalizePortableArchivePath(file.DestinationPath));
				if (!ManifestDeclaresPluginState(manifest, pluginName))
				{
					throw new NotSupportedException(String.Format(
						"FOMOD selection installs plugin '{0}', but collection.json does not explicitly declare its final state in the top-level plugins section.",
						pluginName));
				}
				pluginFiles.Add(pluginName);
			}

			var mappings = new List<ModInstallationSimpleFileMapping>();
			foreach (ScriptedInstallOperation operation in translated.NativeOperations)
			{
				InstallModFileOperation file = operation as InstallModFileOperation;
				if (file != null)
				{
					if (file.DeploymentDecision != null)
						throw new NotSupportedException("A characterized Collection FOMOD plan cannot already contain mutable deployment decisions.");
					mappings.Add(new ModInstallationSimpleFileMapping(file.SourcePath, file.DestinationPath));
					continue;
				}

				SetPluginActivationOperation activation = operation as SetPluginActivationOperation;
				if (activation != null)
				{
					// Vortex applies the Collection's top-level plugin state after its member installs. For this
					// characterized slice, the selected FOMOD may therefore install a plugin file, while the
					// durable Collection review remains authoritative for that plugin's final active state.
					if (activation.RequireActivatablePlugin && IsGamePluginPath(activation.PluginPath, gameMode))
					{
						string pluginName = GetPortableFileName(NormalizePortableArchivePath(activation.PluginPath));
						if (!pluginFiles.Contains(pluginName) || !ManifestDeclaresPluginState(manifest, pluginName))
						{
							throw new NotSupportedException(
								"The FOMOD emits plugin-state intent that cannot be bound to an explicitly declared Collection plugin file.");
						}
						continue;
					}

					// Guarded activations for non-plugin files are no-ops. Any unguarded plugin-state operation
					// remains outside the characterized durable simple-file contract.
					if (!activation.RequireActivatablePlugin)
					{
						throw new NotSupportedException(
							"The characterized Collection FOMOD path does not support unconditional installer-local plugin-state operations.");
					}
					continue;
				}

				throw new NotSupportedException(String.Format(
					"Native FOMOD operation type '{0}' cannot be frozen into the characterized durable simple-file recipe contract.",
					operation.GetType().FullName));
			}

			try
			{
				return new ModInstallationSimpleFileRecipe(mappings);
			}
			catch (ArgumentException ex)
			{
				throw new NotSupportedException(
					"The characterized Collection FOMOD path supports only one-to-one file mappings without source replication or destination collisions.", ex);
			}
		}

		private static bool ManifestDeclaresPluginState(NormalizedCollectionManifest manifest, string pluginName)
		{
			return manifest != null && manifest.HasPluginStateSection && !String.IsNullOrWhiteSpace(pluginName) &&
				manifest.PluginStates.Any(state => StringComparer.OrdinalIgnoreCase.Equals(state.PluginName, pluginName));
		}

		private static bool IsGamePluginPath(string path, IGameMode gameMode)
		{
			if (String.IsNullOrWhiteSpace(path) || gameMode == null || gameMode.PluginExtensions == null)
				return false;
			string extension = Path.GetExtension(path);
			return !String.IsNullOrEmpty(extension) && gameMode.PluginExtensions.Any(candidate =>
				String.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
		}

		private static ModInstallationRecipeValidation CreateValidation(CollectionsRetainedArtifact artifact,
			ModInstallContext installContext, ModInstallationSimpleFileRecipe simpleRecipe)
		{
			var paths = new List<ModInstallationRecipePath>();
			foreach (ModInstallationSimpleFileMapping mapping in simpleRecipe.Mappings)
			{
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, mapping.SourcePath));
				paths.Add(new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, mapping.DestinationPath));
			}

			return new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion, installContext,
				new ModInstallationRecipeExpectedContent(artifact.ContentHash.Value, artifact.ByteLength),
				new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId,
					ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) }, paths);
		}

		private static PreparedCollectionNativeRecipeIdentity BuildPreparedFomodIdentity(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionsRetainedArtifact artifact, IModInstallationFomodRecipePlanningAdapter fomodAdapter,
			ModInstallationFomodSelectionRecipe fomodRecipe, ModInstallationSimpleFileRecipe frozenRecipe,
			ModInstallationRecipeInput translated, CollectionMemberEffectPreview effectPreview, bool skipReadmeFiles)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(PreparedIdentityFormat);
				writer.Write("vortex-fomod/1");
				writer.Write(plan.Target.Fingerprint);
				writer.Write(plan.CurrentStateFingerprint.FormatVersion);
				writer.Write(plan.CurrentStateFingerprint.Value);
				writer.Write((int)member.MemberKey.Kind);
				writer.Write(member.MemberKey.Value);
				writer.Write(member.SourceOrdinal);
				writer.Write(member.RecipeIdentity.Fingerprint);
				writer.Write(member.ArtifactChoice.SelectedArtifact.Scheme);
				writer.Write(member.ArtifactChoice.SelectedArtifact.StableId);
				writer.Write(artifact.ContentHash.Value);
				writer.Write(artifact.ByteLength);
				writer.Write((int)translated.InstallContext.Method);
				writer.Write((int)translated.InstallContext.InstallRoot);
				writer.Write(skipReadmeFiles);
				writer.Write(fomodAdapter.AdapterId);
				writer.Write(fomodAdapter.AdapterVersion);
				writer.Write(fomodAdapter.CapabilityId);
				writer.Write(fomodAdapter.CapabilityVersion);
				writer.Write(translated.Validation.AdapterId);
				writer.Write(translated.Validation.AdapterVersion);
				writer.Write(translated.Validation.Capabilities.Count);
				foreach (ModInstallationRecipeCapability capability in translated.Validation.Capabilities)
				{
					writer.Write(capability.CapabilityId);
					writer.Write(capability.Version);
				}
				writer.Write(fomodRecipe.ScriptVersion.ToString());
				writer.Write(fomodRecipe.Steps.Count);
				foreach (ModInstallationFomodStepSelection step in fomodRecipe.Steps)
				{
					writer.Write(step.StepIndex);
					writer.Write(step.StepName ?? String.Empty);
					writer.Write(step.Groups.Count);
					foreach (ModInstallationFomodGroupSelection group in step.Groups)
					{
						writer.Write(group.GroupIndex);
						writer.Write(group.GroupName ?? String.Empty);
						writer.Write(group.SelectedOptions.Count);
						foreach (ModInstallationFomodOptionSelection option in group.SelectedOptions)
						{
							writer.Write(option.OptionIndex);
							writer.Write(option.OptionName ?? String.Empty);
						}
					}
				}

				writer.Write(frozenRecipe.Mappings.Count);
				foreach (ModInstallationSimpleFileMapping mapping in frozenRecipe.Mappings)
				{
					writer.Write(mapping.SourcePath);
					writer.Write(mapping.DestinationPath);
				}

				WriteEffectPreview(writer, effectPreview);
				writer.Flush();

				using (SHA256 sha256 = SHA256.Create())
				{
					string digest = BitConverter.ToString(sha256.ComputeHash(stream.ToArray())).Replace("-", String.Empty).ToLowerInvariant();
					return PreparedCollectionNativeRecipeIdentity.FromFingerprint("sha256:" + digest);
				}
			}
		}

		private static void WriteEffectPreview(BinaryWriter writer, CollectionMemberEffectPreview effectPreview)
		{
			writer.Write(effectPreview.Files.Count);
			foreach (CollectionPlannedFileEffect file in effectPreview.Files)
			{
				writer.Write((int)file.Target.Root);
				writer.Write(file.Target.RelativePath);
			}
			writer.Write(effectPreview.PluginEffects.Count);
			foreach (CollectionPlannedPluginEffect plugin in effectPreview.PluginEffects)
			{
				writer.Write((int)plugin.Kind);
				writer.Write(plugin.Active.HasValue);
				if (plugin.Active.HasValue)
					writer.Write(plugin.Active.Value);
				writer.Write(plugin.AbsoluteIndex.HasValue);
				if (plugin.AbsoluteIndex.HasValue)
					writer.Write(plugin.AbsoluteIndex.Value);
				writer.Write(plugin.PluginPaths.Count);
				foreach (string path in plugin.PluginPaths)
					writer.Write(path);
			}
		}

		private static PreparedCollectionNativeRecipeIdentity BuildPreparedIdentity(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionsRetainedArtifact artifact, BasicInstallPlan basicPlan,
			ModInstallationRecipeInput translated, CollectionMemberEffectPreview effectPreview, bool skipReadmeFiles)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(PreparedIdentityFormat);
				writer.Write(plan.Target.Fingerprint);
				writer.Write(plan.CurrentStateFingerprint.FormatVersion);
				writer.Write(plan.CurrentStateFingerprint.Value);
				writer.Write((int)member.MemberKey.Kind);
				writer.Write(member.MemberKey.Value);
				writer.Write(member.SourceOrdinal);
				writer.Write(member.RecipeIdentity.Fingerprint);
				writer.Write(member.ArtifactChoice.SelectedArtifact.Scheme);
				writer.Write(member.ArtifactChoice.SelectedArtifact.StableId);
				writer.Write(artifact.ContentHash.Value);
				writer.Write(artifact.ByteLength);
				writer.Write((int)translated.InstallContext.Method);
				writer.Write((int)translated.InstallContext.InstallRoot);
				writer.Write(skipReadmeFiles);
				writer.Write(translated.Validation.AdapterId);
				writer.Write(translated.Validation.AdapterVersion);
				writer.Write(translated.Validation.Capabilities.Count);
				foreach (ModInstallationRecipeCapability capability in translated.Validation.Capabilities)
				{
					writer.Write(capability.CapabilityId);
					writer.Write(capability.Version);
				}

				writer.Write(basicPlan.Files.Count);
				foreach (BasicInstallPlanFile file in basicPlan.Files)
				{
					writer.Write(file.SourcePath);
					writer.Write(file.DestinationPath);
					writer.Write(file.GameInstallPath);
					writer.Write(file.VirtualStoragePath ?? String.Empty);
					writer.Write((int)file.DeploymentTarget.Root);
					writer.Write(file.DeploymentTarget.RelativePath);
				}

				WriteEffectPreview(writer, effectPreview);
				writer.Flush();

				using (SHA256 sha256 = SHA256.Create())
				{
					string digest = BitConverter.ToString(sha256.ComputeHash(stream.ToArray())).Replace("-", String.Empty).ToLowerInvariant();
					return PreparedCollectionNativeRecipeIdentity.FromFingerprint("sha256:" + digest);
				}
			}
		}
	}
}
