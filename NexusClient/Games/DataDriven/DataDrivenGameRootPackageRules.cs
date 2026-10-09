using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>Freezes validated JSON package rules at the GameMode boundary.</summary>
	internal static class DataDrivenGameRootPackageRules
	{
		/// <summary>Copies optional definition rules into immutable runtime rules, preserving empty legacy definitions.</summary>
		internal static IReadOnlyList<GameRootPackageRule> Create(GameModeDefinition definition)
		{
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			List<GameModeGameRootPackageRuleDefinition> definitions = definition.ModInstall == null
				? null : definition.ModInstall.GameRootPackageRules;
			List<GameRootPackageRule> rules = (definitions ?? new List<GameModeGameRootPackageRuleDefinition>()).Select(rule =>
				new GameRootPackageRule(rule.Id, rule.RequiredFiles,
					(rule.XmlChecks ?? new List<GameModeGameRootPackageXmlCheckDefinition>()).Select(check =>
						new GameRootPackageXmlCheck(check.File, check.ElementPath)), rule.AllowSingleWrapperFolder)).ToList();
			return new ReadOnlyCollection<GameRootPackageRule>(rules);
		}
	}
}
