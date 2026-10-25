using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Mods;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Dispatches configured preparation profiles while keeping game protocols in their dedicated adapters.</summary>
	internal class CollectionGamePreparation
	{
		/// <summary>Creates the preparation profile declared for a manifest's catalog domain.</summary>
		internal static CollectionGamePreparation ForManifest(JObject root)
		{
			GameModeRuntimeConfiguration.EnsureLoaded();
			string domain = (string)(root["info"] as JObject)?["domainName"];
			GameModeRuntimePolicy policy = GameModeRuntimePolicies.All.Values.FirstOrDefault(value =>
				value.CollectionDomain != null && String.Equals(value.CollectionDomain, domain, StringComparison.OrdinalIgnoreCase));
			return Create(policy, root);
		}

		/// <summary>Creates the active game's configured preparation profile.</summary>
		internal static CollectionGamePreparation ForMode(IGameMode mode)
		{
			GameModeRuntimeConfiguration.EnsureLoaded();
			return Create(GameModeRuntimePolicies.For(mode.ModeId), null);
		}

		/// <summary>Selects protocol implementations by configured profile identity, independently of local game IDs.</summary>
		private static CollectionGamePreparation Create(GameModeRuntimePolicy policy, JObject root)
		{
			switch (policy?.CollectionAdapter)
			{
				case "module-settings-v1": return new BaldursGate3CollectionPreparation(root);
				case "component-settings-v1": return new Witcher3CollectionPreparation(root);
				default: return new CollectionGamePreparation();
			}
		}

		internal virtual bool HasOwnedMetadata => false;
		internal virtual bool RequiresCustomPlan(IGameMode mode) => false;
		internal virtual string PreparedIdentityFormat => null;
		/// <summary>Validates the native preparation path before generic archive planning.</summary>
		internal virtual void ValidatePreparation(ResolvedCollectionMemberPlan member, IGameMode mode, bool replacement) { }
		/// <summary>Preserves adapter-specific restrictions on scripted or selected-file installers.</summary>
		internal virtual void RequireSupportedInstaller(ResolvedCollectionMemberPlan member, IMod mod) { }
		/// <summary>Builds adapter-owned operations through the existing native installation executor.</summary>
		internal virtual IReadOnlyList<ScriptedInstallOperation> BuildOperations(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member, IMod mod,
			IGameMode mode, ModInstallContext context, CancellationToken token) => throw new NotSupportedException("This preparation profile does not supply custom operations.");
		/// <summary>Retains the selected package inventory for collection-owned metadata.</summary>
		internal virtual bool ConfigurePackageFiles(ResolvedCollectionPlan plan, IEnumerable<string> files) => false;
		/// <summary>Applies collection-owned settings after native file translation.</summary>
		internal virtual ModInstallationRecipeInput ApplySettings(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput translated, IGameMode mode) => translated;
		/// <summary>Maps captured installer operations through the selected deployment profile.</summary>
		internal virtual IReadOnlyList<ScriptedInstallOperation> MapCaptured(ResolvedCollectionMemberPlan member,
			IReadOnlyList<ScriptedInstallOperation> captured, IMod mod) => captured;
		/// <summary>Maps selected archive files through the selected deployment profile.</summary>
		internal virtual ModInstallationSimpleFileRecipe MapFiles(ResolvedCollectionMemberPlan member, ModInstallationSimpleFileRecipe recipe, IMod mod) => recipe;
		/// <summary>Returns an alternate package-relative comparison path for secondary-root protocols.</summary>
		internal virtual string ComparablePath(string path) => path;
	}
}
