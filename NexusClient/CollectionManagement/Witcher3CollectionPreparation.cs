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
	/// <summary>Keeps Witcher folder order, menu merges and native-adapter requirements out of generic preparation.</summary>
	internal sealed class Witcher3CollectionPreparation : CollectionGamePreparation
	{
		private readonly JObject _root;
		private readonly CollectionWitcher3Data _data;
		/// <summary>Reads retained Witcher extension data once per manifest.</summary>
		internal Witcher3CollectionPreparation(JObject root) { _root = root; _data = root == null ? null : CollectionWitcher3Data.Parse(root); }
		internal override string PreparedIdentityFormat => "nmm-ce.witcher3-prepared/1";
		internal override bool RequiresCustomPlan(IGameMode mode) => GameModeRuntimePolicies.For(mode.ModeId).CollectionAdapter == "component-settings-v1";
		/// <inheritdoc />
		internal override void ValidatePreparation(ResolvedCollectionMemberPlan member, IGameMode mode, bool replacement)
		{
			if (!RequiresCustomPlan(mode))
			{
				if (_root["loadOrder"] != null || _root["mergedData"] != null || !String.IsNullOrEmpty((string)_root["mods"]?[member.SourceOrdinal]?["details"]?["type"]))
					throw new NotSupportedException("This Collection's Witcher-specific order, merge data and package types require the Witcher 3 Remastered native adapter.");
				return;
			}
			if (member.HasVortexBinaryPatches || member.HasVortexFileOverrides)
				throw new NotSupportedException("Witcher Collection binary patches and file-override recipes require their own selected-output preparation.");
			if (replacement)
				throw new NotSupportedException("Witcher Collection settings are not projected through the replacement environment; use the additive installation workflow.");
		}
		/// <inheritdoc />
		internal override void RequireSupportedInstaller(ResolvedCollectionMemberPlan member, IMod mod)
		{
			if (member.HasVortexFileList || member.HasVortexFomodSelection || mod.HasInstallScript)
				throw new NotSupportedException("This Witcher Collection package requires scripted or selected-file installation; its folder order and menu effects cannot be reproduced by the basic Witcher adapter.");
		}
		/// <inheritdoc />
		internal override IReadOnlyList<ScriptedInstallOperation> BuildOperations(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			IMod mod, IGameMode mode, ModInstallContext context, CancellationToken token)
		{
			if (!(mode is DataDrivenWitcher3RemasteredGameMode) || _data == null)
				throw new InvalidDataException("Witcher Collection preparation requires the dedicated native adapter and retained Witcher source.");
			return CollectionWitcher3InstallPlanner.Build(plan, member, mod, context, _root, _data, token);
		}
	}
}
