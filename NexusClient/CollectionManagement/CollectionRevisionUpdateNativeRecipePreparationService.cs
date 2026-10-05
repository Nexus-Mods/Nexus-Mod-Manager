using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Concrete C10.3 bridge from an exact verified candidate archive to the existing C5/C6 native recipe preparer.
	/// It may register/materialize an exact archive in NMM's mod library, but it never activates/reinstalls/deactivates a mod.
	/// </summary>
	public sealed class CollectionRevisionUpdateNativeRecipePreparationService : ICollectionRevisionUpdateNativeRecipePreparationService
	{
		private readonly ServiceManager _services;
		private readonly CollectionsRetainedArtifactStore _retainedArtifactStore;
		private readonly CollectionNativeRecipePreparer _nativeRecipePreparer;

		public CollectionRevisionUpdateNativeRecipePreparationService(ServiceManager services, CollectionsStore store)
			: this(services, new CollectionsRetainedArtifactStore(store), new CollectionNativeRecipePreparer(store))
		{
		}

		internal CollectionRevisionUpdateNativeRecipePreparationService(ServiceManager services,
			CollectionsRetainedArtifactStore retainedArtifactStore, CollectionNativeRecipePreparer nativeRecipePreparer)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_retainedArtifactStore = retainedArtifactStore ?? throw new ArgumentNullException(nameof(retainedArtifactStore));
			_nativeRecipePreparer = nativeRecipePreparer ?? throw new ArgumentNullException(nameof(nativeRecipePreparer));
			if (_services.ModManager == null) throw new InvalidOperationException("Revision-update native preparation requires the active ModManager.");
		}

		public PreparedCollectionNativeRecipe Prepare(CollectionRevisionUpdatePlan updatePlan,
			CollectionRevisionUpdateMemberPlan updateMember, CollectionVerifiedArchive verifiedArchive,
			CollectionNativeStateIndex currentState, CancellationToken cancellationToken)
		{
			return PrepareCore(updatePlan, updateMember, verifiedArchive, currentState, cancellationToken, true);
		}

		/// <summary>Re-prepares one candidate against the latest verified C10 execution safe boundary.</summary>
		internal PreparedCollectionNativeRecipe PrepareAtExecutionBoundary(CollectionRevisionUpdatePlan updatePlan,
			CollectionRevisionUpdateMemberPlan updateMember, CollectionVerifiedArchive verifiedArchive,
			CollectionNativeStateIndex currentState, CancellationToken cancellationToken)
		{
			return PrepareCore(updatePlan, updateMember, verifiedArchive, currentState, cancellationToken, false);
		}

		private PreparedCollectionNativeRecipe PrepareCore(CollectionRevisionUpdatePlan updatePlan,
			CollectionRevisionUpdateMemberPlan updateMember, CollectionVerifiedArchive verifiedArchive,
			CollectionNativeStateIndex currentState, CancellationToken cancellationToken, bool requireApprovedStateFingerprint)
		{
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			if (updateMember == null) throw new ArgumentNullException(nameof(updateMember));
			if (verifiedArchive == null) throw new ArgumentNullException(nameof(verifiedArchive));
			if (currentState == null) throw new ArgumentNullException(nameof(currentState));
			if (updateMember.NewMember == null) throw new ArgumentException("A removed member has no candidate native recipe to prepare.", nameof(updateMember));
			if (!updatePlan.NewPlan.Target.Equals(currentState.Target))
				throw new InvalidOperationException("Candidate native preparation requires the exact revision-update target.");
			if (requireApprovedStateFingerprint && !updatePlan.ObservedStateFingerprint.Equals(currentState.Fingerprint))
				throw new InvalidOperationException("Candidate native preparation requires the exact current-state snapshot approved by the revision update.");
			if (!verifiedArchive.Request.PlanIdentity.Equals(updatePlan.NewPlan.Identity) ||
				!verifiedArchive.Request.MemberKey.Equals(updateMember.MemberKey) ||
				!verifiedArchive.Request.SelectedArtifact.Equals(updateMember.NewMember.ArtifactChoice.SelectedArtifact) ||
				!verifiedArchive.Request.RecipeIdentity.Equals(updateMember.NewMember.RecipeIdentity))
				throw new ArgumentException("The verified archive does not belong to the exact candidate member plan.", nameof(verifiedArchive));

			IMod managedMod = ResolveManagedMod(updateMember, verifiedArchive, cancellationToken);
			ModInstallContext installContext = ResolveInstallContext(updateMember, currentState);
			bool skipReadme = _services.ModManager.EnvironmentInfo.Settings.SkipReadmeFiles;
			return _nativeRecipePreparer.PrepareExact(updatePlan.NewPlan, updateMember.NewMember, verifiedArchive, managedMod,
				_services.ModManager.GameMode, _services.ModManager.EnvironmentInfo, installContext, currentState, skipReadme,
				_services.PluginManager, _services.ModManager.ActiveMods.ToList(), cancellationToken);
		}

		private ModInstallContext ResolveInstallContext(CollectionRevisionUpdateMemberPlan updateMember,
			CollectionNativeStateIndex currentState)
		{
			CollectionNativeModState existing = null;
			if (updateMember.Binding != null)
			{
				if (!currentState.Mods.TryGetValue(updateMember.Binding.NativeMod, out existing))
					throw new InvalidOperationException("The approved old member binding no longer resolves to current native state.");
			}
			else
			{
				List<CollectionNativeModState> candidates = FindCurrentArtifactCandidates(updateMember.NewMember, currentState);
				if (candidates.Count > 1)
					throw new InvalidOperationException("The candidate member matches multiple current native mod instances; install context cannot be inferred safely.");
				if (candidates.Count == 1) existing = candidates[0];
			}

			ModInstallRoot requestedRoot;
			if (updateMember.NewMember.RequiresGameRootInstall)
			{
				if (!_services.ModManager.GameMode.SupportsGameRootModInstall)
					throw new NotSupportedException("The candidate Collection member requires native game-root installation, but this game mode does not support it.");
				requestedRoot = ModInstallRoot.GameRoot;
			}
			else if (existing != null)
			{
				requestedRoot = existing.InstallRoot;
			}
			else
			{
				requestedRoot = ModInstallRoot.Default;
			}

			if (existing != null)
				return new ModInstallContext(existing.InstallMethod, requestedRoot);
			return _services.ModManager.CapturePreferredInstallContext(requestedRoot);
		}

		private static List<CollectionNativeModState> FindCurrentArtifactCandidates(ResolvedCollectionMemberPlan member,
			CollectionNativeStateIndex currentState)
		{
			string gameDomain;
			long modId;
			long fileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out gameDomain, out modId, out fileId))
				return new List<CollectionNativeModState>();
			string mod = modId.ToString(CultureInfo.InvariantCulture);
			string file = fileId.ToString(CultureInfo.InvariantCulture);
			return currentState.Mods.Values.Where(x => StringComparer.Ordinal.Equals(x.NexusModId, mod) &&
				StringComparer.Ordinal.Equals(x.NexusFileId, file)).ToList();
		}

		private IMod ResolveManagedMod(CollectionRevisionUpdateMemberPlan updateMember, CollectionVerifiedArchive archive,
			CancellationToken cancellationToken)
		{
			IMod bound = ResolveBoundActiveMod(updateMember);
			if (bound != null && CollectionArchiveContentMatcher.MatchesFile(
				CollectionArchiveContentMatcher.GetManagedArchivePath(bound), archive.Artifact, cancellationToken))
				return bound;

			ResolvedCollectionMemberPlan member = updateMember.NewMember;
			string memberName = member.DisplayName ?? member.MemberKey.Value;
			if (CollectionBundledArtifactIdentity.IsBundle(member.ArtifactChoice.SelectedArtifact))
			{
				List<IMod> exactManaged = CollectionArchiveContentMatcher.FindExactManagedMods(_services.ModManager, archive.Artifact, cancellationToken);
				if (exactManaged.Count == 1) return exactManaged[0];
				if (exactManaged.Count > 1)
					throw new InvalidOperationException("Multiple native managed archives contain the exact candidate bundle bytes.");
				IMod repaired = ModManagerCollectionManagedArchiveSource.TryRegisterExactLibraryArchive(
					_services.ModManager, archive.Artifact, cancellationToken);
				if (repaired != null) return repaired;
				repaired = ModManagerCollectionManagedArchiveSource.MaterializeAndRegisterRetainedArchive(
					_services.ModManager, _retainedArtifactStore, archive.Artifact, memberName, cancellationToken);
				if (repaired != null) return repaired;
				throw new InvalidOperationException("The exact candidate bundle archive could not be materialized and registered in NMM's mod library.");
			}

			string domain;
			long nexusModId;
			long nexusFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out domain, out nexusModId, out nexusFileId))
				throw new NotSupportedException("The candidate Collection artifact scheme has no characterized native recipe input mapping.");
			string currentDomain = _services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName;
			if (!StringComparer.OrdinalIgnoreCase.Equals(domain, currentDomain))
				throw new InvalidOperationException("The active native repository game does not match the candidate Collection artifact domain.");

			string modId = nexusModId.ToString(CultureInfo.InvariantCulture);
			string fileId = nexusFileId.ToString(CultureInfo.InvariantCulture);
			List<IMod> identityCandidates = _services.ModManager.ManagedMods.Where(x =>
				ModManagerCollectionManagedArchiveSource.MatchesRepositoryFileIdentity(x, _services.ModManager.SortOrderService, modId, fileId)).ToList();
			List<IMod> exactIdentityCandidates = identityCandidates.Where(x => CollectionArchiveContentMatcher.MatchesFile(
				CollectionArchiveContentMatcher.GetManagedArchivePath(x), archive.Artifact, cancellationToken)).ToList();
			if (exactIdentityCandidates.Count == 1)
			{
				ModManagerCollectionManagedArchiveSource.ConfirmVerifiedArchiveRepositoryFileIdentity(_services.ModManager,
					exactIdentityCandidates[0], modId, fileId, archive.Artifact.ByteLength, archive.Artifact.ContentHash, cancellationToken);
				return exactIdentityCandidates[0];
			}
			if (exactIdentityCandidates.Count > 1)
				throw new InvalidOperationException("Multiple native managed archives match both the candidate Nexus identity and exact verified bytes.");

			List<IMod> exactContentCandidates = _services.ModManager.ManagedMods.Where(x =>
				ModManagerCollectionManagedArchiveSource.IsMetadataCompatibleForVerifiedContent(x, modId, fileId) &&
				CollectionArchiveContentMatcher.MatchesFile(CollectionArchiveContentMatcher.GetManagedArchivePath(x), archive.Artifact, cancellationToken)).ToList();
			if (exactContentCandidates.Count != 0)
			{
				IMod recovered = ModManagerCollectionManagedArchiveSource.SelectDeterministicEquivalentManagedMod(exactContentCandidates);
				ModManagerCollectionManagedArchiveSource.ConfirmVerifiedArchiveRepositoryFileIdentity(_services.ModManager,
					recovered, modId, fileId, archive.Artifact.ByteLength, archive.Artifact.ContentHash, cancellationToken);
				return recovered;
			}

			IMod registered = ModManagerCollectionManagedArchiveSource.TryRegisterExactLibraryArchive(
				_services.ModManager, archive.Artifact, cancellationToken);
			if (registered == null)
				registered = ModManagerCollectionManagedArchiveSource.MaterializeAndRegisterRetainedArchive(
					_services.ModManager, _retainedArtifactStore, archive.Artifact, memberName, cancellationToken);
			if (registered != null)
			{
				ModManagerCollectionManagedArchiveSource.ConfirmVerifiedArchiveRepositoryFileIdentity(_services.ModManager,
					registered, modId, fileId, archive.Artifact.ByteLength, archive.Artifact.ContentHash, cancellationToken);
				return registered;
			}
			throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
				"Candidate Collection member '{0}' ({1}/{2}) has exact verified bytes, but NMM could not register those bytes in the mod library.",
				memberName, modId, fileId));
		}

		private IMod ResolveBoundActiveMod(CollectionRevisionUpdateMemberPlan updateMember)
		{
			if (updateMember.Binding == null) return null;
			List<IMod> matches = _services.ModManager.InstallationLog.ActiveMods.Where(x => x != null &&
				StringComparer.OrdinalIgnoreCase.Equals(_services.ModManager.InstallationLog.GetModKey(x), updateMember.Binding.NativeMod.NativeModKey)).ToList();
			if (matches.Count > 1)
				throw new InvalidOperationException("The approved old member binding resolves to multiple active native mods.");
			return matches.Count == 1 ? matches[0] : null;
		}
	}
}
