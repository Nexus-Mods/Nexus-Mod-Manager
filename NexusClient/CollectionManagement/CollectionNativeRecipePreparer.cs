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
			CollectionMemberEffectPreview effectPreview, IEnumerable<string> retainedArtifactIds)
		{
			Member = member ?? throw new ArgumentNullException(nameof(member));
			PreparedNativeIdentity = preparedNativeIdentity ?? throw new ArgumentNullException(nameof(preparedNativeIdentity));
			RecipeInput = recipeInput ?? throw new ArgumentNullException(nameof(recipeInput));
			EffectPreview = effectPreview ?? throw new ArgumentNullException(nameof(effectPreview));
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

		/// <summary>Gets the retained immutable inputs required to reproduce this preparation after restart.</summary>
		public ReadOnlyCollection<string> RetainedArtifactIds { get { return _retainedArtifactIds; } }
	}

	/// <summary>
	/// Translates characterized Collection recipe source into validated native C5 recipe input without executing native mutation.
	/// </summary>
	/// <remarks>
	/// C6.15.9 intentionally supports only the fixture-characterized ordinary/basic exact-layout path. Vortex hashes, choices,
	/// patches, fileOverrides and other unsupported source behavior remain blocked by the existing normalizer/capability gate.
	/// </remarks>
	public sealed class CollectionNativeRecipePreparer
	{
		private const string PreparedIdentityFormat = "nmm-ce.collections.prepared-native-recipe/1";
		private const int HashBufferSize = 81920;
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
		/// Prepares the characterized ordinary/basic exact-layout member path into explicit native file operations and exact effects.
		/// </summary>
		public PreparedCollectionNativeRecipe PrepareBasicSimpleExact(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive verifiedArchive, IMod mod, IGameMode gameMode,
			ModInstallContext installContext, CollectionNativeStateIndex currentState, bool skipReadmeFiles,
			IPluginManager pluginManager = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			ValidateInputs(plan, member, verifiedArchive, mod, gameMode, installContext, currentState);
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
			ValidateManagedModArchive(mod, verifiedArchive.Artifact, cancellationToken);

			BasicInstallPlanResult basicResult = _basicInstallPlanBuilder.Build(mod, gameMode, installContext, skipReadmeFiles);
			if (!basicResult.IsSupported)
			{
				throw new NotSupportedException(String.Format("The Collection member cannot use the characterized basic/simple adapter: {0} ({1}).",
					basicResult.Message, basicResult.UnsupportedReason));
			}

			// Re-check after archive enumeration/planning so a mutable library archive cannot change unnoticed during preparation.
			ValidateManagedModArchive(mod, verifiedArchive.Artifact, cancellationToken);

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
			return new PreparedCollectionNativeRecipe(member, preparedIdentity, translated, effectPreview,
				new[] { sourceRecord.RawManifestArtifactId, verifiedArchive.Artifact.ArtifactId });
		}

		private static void ValidateInputs(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			CollectionVerifiedArchive verifiedArchive, IMod mod, IGameMode gameMode, ModInstallContext installContext,
			CollectionNativeStateIndex currentState)
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
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new NotSupportedException("C6.15.9 native recipe preparation is additive-only.");
			if (!plan.Target.Equals(currentState.Target) || !plan.CurrentStateFingerprint.Equals(currentState.Fingerprint))
				throw new InvalidOperationException("Native recipe preparation requires the exact C6.1 state snapshot already bound to the resolved plan.");

			ResolvedCollectionMemberPlan selected = plan.SelectedMembers.SingleOrDefault(x => x.MemberKey.Equals(member.MemberKey));
			if (selected == null || selected.SourceOrdinal != member.SourceOrdinal ||
				!selected.RecipeIdentity.Equals(member.RecipeIdentity) || !selected.ArtifactChoice.Equals(member.ArtifactChoice))
				throw new ArgumentException("The member being prepared must belong to the exact selected resolved plan.", nameof(member));
			if (member.ArtifactChoice.Kind != CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact)
				throw new NotSupportedException("The initial C6.15.9 preparation capability accepts exact requested artifacts only.");

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
				!Equals(sourceMember.Artifact, member.ArtifactChoice.RequestedArtifact) ||
				!Equals(sourceMember.RecipeIdentity, member.RecipeIdentity))
				throw new InvalidDataException("The retained Collection source ordinal does not reproduce the exact resolved member artifact and recipe identity.");

			if (normalized.CapabilityReport.ManifestIssues.Count != 0)
				throw new NotSupportedException("The retained Collection source contains manifest-level behavior outside the characterized C6.15.9 preparation capability.");
			CollectionMemberCapabilityReport sourceReport = normalized.CapabilityReport.MemberReports
				.SingleOrDefault(x => x.Member.SourceOrdinal == member.SourceOrdinal);
			if (sourceReport == null || sourceReport.Status != CollectionCompatibilityStatus.Supported)
				throw new NotSupportedException("The retained Collection member source contains behavior outside the characterized basic/simple preparation capability.");
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
			if (!_artifactStore.VerifyArtifact(artifact.ArtifactId, cancellationToken))
				throw new InvalidDataException("The retained verified archive failed its integrity check before recipe preparation.");
		}

		private static void ValidateManagedModArchive(IMod mod, CollectionsRetainedArtifact expectedArtifact,
			CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(mod.Filename))
				throw new InvalidDataException("The managed mod has no archive path for exact recipe preparation.");
			string path = Path.GetFullPath(mod.Filename);
			if (!File.Exists(path))
				throw new FileNotFoundException("The managed mod archive required for recipe preparation is missing.", path);
			FileInfo info = new FileInfo(path);
			if (info.Length != expectedArtifact.ByteLength)
				throw new InvalidDataException("The managed mod archive length does not match the verified immutable Collection archive.");

			string hash;
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, HashBufferSize, FileOptions.SequentialScan))
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] buffer = new byte[HashBufferSize];
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha256.TransformBlock(buffer, 0, read, buffer, 0);
				}
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				hash = BitConverter.ToString(sha256.Hash).Replace("-", String.Empty).ToLowerInvariant();
			}
			if (!StringComparer.Ordinal.Equals(hash, expectedArtifact.ContentHash.Value))
				throw new InvalidDataException("The managed mod archive bytes do not match the verified immutable Collection archive.");
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
