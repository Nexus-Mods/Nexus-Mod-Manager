using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Captures the native destination and optional exact archive base chosen before Collection review.</summary>
	internal sealed class CollectionInstallDestination
	{
		/// <summary>Creates one immutable destination without changing the provider's recipe identity.</summary>
		public CollectionInstallDestination(ModInstallContext installContext, string gameRootArchiveBaseDirectory = null)
		{
			InstallContext = installContext ?? throw new ArgumentNullException(nameof(installContext));
			GameRootArchiveBaseDirectory = gameRootArchiveBaseDirectory;
		}

		public ModInstallContext InstallContext { get; }
		public string GameRootArchiveBaseDirectory { get; }
	}

	/// <summary>Resolves new Collection destinations from explicit metadata, verified package rules, then native defaults.</summary>
	internal static class CollectionInstallDestinationResolver
	{
		/// <summary>Checks for optional rules without changing legacy games or ordinary native installation.</summary>
		internal static bool HasPackageRules(IGameMode gameMode)
		{
			var provider = gameMode as IGameRootPackageRuleProvider;
			return gameMode != null && gameMode.SupportsGameRootModInstall && provider != null &&
				provider.GameRootPackageRules != null && provider.GameRootPackageRules.Count > 0;
		}

		/// <summary>Resolves one already verified managed archive while retaining its selected Virtual or Direct method.</summary>
		internal static CollectionInstallDestination Resolve(IGameMode gameMode, ResolvedCollectionMemberPlan member,
			IMod verifiedMod, ModInstallContext fallback, CancellationToken cancellationToken)
		{
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			if (member == null) throw new ArgumentNullException(nameof(member));
			if (verifiedMod == null) throw new ArgumentNullException(nameof(verifiedMod));
			if (fallback == null) throw new ArgumentNullException(nameof(fallback));
			if (member.RequiresGameRootInstall)
			{
				if (!gameMode.SupportsGameRootModInstall)
					throw new NotSupportedException("This Collection member requires game-root installation, which this game mode does not support.");
				return new CollectionInstallDestination(new ModInstallContext(fallback.Method, ModInstallRoot.GameRoot));
			}

			GameRootPackageMatch match = GameRootPackageMatcher.Match(gameMode, verifiedMod, cancellationToken);
			if (match == null) return new CollectionInstallDestination(fallback);
			// Replicated and scripted recipes already define exact destinations; only basic archive mapping needs a wrapper base.
			string archiveBase = member.HasVortexFileList || member.HasVortexFomodSelection ? null : match.ArchiveBaseDirectory;
			return new CollectionInstallDestination(new ModInstallContext(fallback.Method, ModInstallRoot.GameRoot), archiveBase);
		}

		/// <summary>Creates matching with exact archive destination checks only for games which opt into package rules.</summary>
		internal static CollectionMemberMatchEngine CreateMatchEngine(ModManager modManager)
		{
			if (modManager == null) throw new ArgumentNullException(nameof(modManager));
			if (!HasPackageRules(modManager.GameMode)) return new CollectionMemberMatchEngine();
			return new CollectionMemberMatchEngine((member, archive, cancellationToken) => RequiresVerifiedGameRoot(modManager, archive, cancellationToken));
		}

		/// <summary>Reads only managed archive bytes which match the already sealed acquisition artifact.</summary>
		private static bool RequiresVerifiedGameRoot(ModManager modManager, CollectionVerifiedArchive archive, CancellationToken cancellationToken)
		{
			List<IMod> exact = CollectionArchiveContentMatcher.FindExactManagedMods(modManager, archive.Artifact, cancellationToken);
			if (exact.Count == 0)
				throw new InvalidDataException("The verified archive needed to check the installed Collection member's destination is no longer available in the mod library.");
			IMod mod = ModManagerCollectionManagedArchiveSource.SelectDeterministicEquivalentManagedMod(exact);
			GameRootPackageMatch match = GameRootPackageMatcher.Match(modManager.GameMode, mod, cancellationToken);
			if (!CollectionArchiveContentMatcher.MatchesFile(CollectionArchiveContentMatcher.GetManagedArchivePath(mod), archive.Artifact, cancellationToken))
				throw new InvalidDataException("The managed archive changed while checking its Collection installation destination.");
			return match != null;
		}
	}
}
