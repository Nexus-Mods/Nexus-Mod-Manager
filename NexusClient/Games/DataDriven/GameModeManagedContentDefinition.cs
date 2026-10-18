using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>Versioned optional managed-content capabilities; absent capabilities remain unsupported.</summary>
	public sealed class GameModeManagedContentDefinition
	{
		public int SchemaVersion { get; set; }
		public GameModeInstallLayoutDefinition InstallLayout { get; set; }
		public GameModeComponentOrderingDefinition ComponentOrdering { get; set; }
	}

	/// <summary>Optional INI order observation and explicit reviewed sequential editing declaration.</summary>
	public sealed class GameModeComponentOrderingDefinition
	{
		public string Provider { get; set; }
		public int Revision { get; set; }
		public string FileRoot { get; set; }
		public string RelativePath { get; set; }
		public string ComponentKind { get; set; }
		public string ComponentPrefix { get; set; }
		public string PriorityKey { get; set; }
		public string EnabledKey { get; set; }
		public string Editing { get; set; }
	}

	/// <summary>Declarative configuration for the built-in, read-only folder-layout provider.</summary>
	public sealed class GameModeInstallLayoutDefinition
	{
		public string Provider { get; set; }
		public int Revision { get; set; }
		public int MaximumWrapperSegments { get; set; }
		public string[] IgnoredFileNames { get; set; }
		public string[] RejectedFileSuffixes { get; set; }
		public List<GameModeInstallLayoutRuleDefinition> Rules { get; set; }
	}

	/// <summary>One literal component or raw-content shape mapped to the existing native Data root.</summary>
	public sealed class GameModeInstallLayoutRuleDefinition
	{
		public string Id { get; set; }
		public string Kind { get; set; }
		public string SourceRoot { get; set; }
		public string ComponentPrefix { get; set; }
		public string ContentFolder { get; set; }
		public string TargetRoot { get; set; }
		public string TargetFolder { get; set; }
		public string ComponentKind { get; set; }
		public int MaximumComponentNameLength { get; set; }
	}

	/// <summary>Resolves typed definitions into the same immutable provider used for validation and native planning.</summary>
	public static class DataDrivenManagedContentRules
	{
		/// <summary>Resolves optional order-file metadata and rejects ambiguous associations with configured component identities.</summary>
		public static IniOrderDocumentRule CreateOrdering(GameModeManagedContentDefinition definition)
		{
			GameModeComponentOrderingDefinition order = definition?.ComponentOrdering;
			if (order == null) return null;
			ManagedContentLayoutRule[] matches = Create(definition).Rules.Where(rule =>
				String.Equals(rule.ComponentKind, order.ComponentKind, StringComparison.Ordinal)).ToArray();
			if (matches.Length == 0) throw new ArgumentException("componentOrdering.componentKind must identify a configured layout component kind.");
			if (matches.Any(rule => !String.Equals(rule.TargetFolder, matches[0].TargetFolder, StringComparison.Ordinal) ||
				!String.Equals(rule.ComponentPrefix, order.ComponentPrefix, StringComparison.Ordinal)))
				throw new ArgumentException("componentOrdering requires one target folder and an identical component prefix for the selected component kind.");
			return new IniOrderDocumentRule(order.Provider, order.Revision, order.FileRoot, order.RelativePath,
				order.ComponentKind, matches[0].TargetFolder, order.ComponentPrefix, order.PriorityKey, order.EnabledKey, order.Editing);
		}

		/// <summary>Retains Witcher's fixed Documents/mods.settings read policy while selecting the same shared observer.</summary>
		public static IniOrderDocumentRule CreateWitcher3Ordering(GameModeManagedContentDefinition definition)
		{
			IniOrderDocumentRule order = CreateOrdering(definition);
			if (order != null && order.Editing != IniOrderDocumentRule.EditProviderId)
				throw new ArgumentException("Remastered order editing requires reviewed-sequential-ini-v1.");
			if (order != null && (order.FileRoot != "Documents" || order.RelativePath != @"The Witcher 3\mods.settings" ||
				order.ComponentKind != "mod-folder" || order.TargetFolder != "Mods" || order.Format.ComponentPrefix != "mod" ||
				order.Format.PriorityKey != "Priority" || order.Format.EnabledKey != "Enabled"))
				throw new ArgumentException("The Witcher adapter requires its fixed Documents/The Witcher 3/mods.settings Mods/mod*/Priority/Enabled observation policy.");
			return order;
		}

		/// <summary>Rejects unsupported capability versions/providers and snapshots validated rules.</summary>
		public static ManagedContentLayoutPlanner Create(GameModeManagedContentDefinition definition)
		{
			if (definition == null || definition.SchemaVersion != 1) throw new ArgumentException("managedContent.schemaVersion must be 1.");
			GameModeInstallLayoutDefinition layout = definition.InstallLayout;
			if (layout == null) throw new ArgumentException("managedContent.installLayout is required for this capability version.");
			var rules = new List<ManagedContentLayoutRule>();
			foreach (GameModeInstallLayoutRuleDefinition rule in layout.Rules ?? Enumerable.Empty<GameModeInstallLayoutRuleDefinition>())
			{
				if (rule == null) throw new ArgumentException("installLayout.rules cannot contain null.");
				try
				{
					rules.Add(new ManagedContentLayoutRule(rule.Id, rule.Kind, rule.SourceRoot, rule.ComponentPrefix,
						rule.ContentFolder, rule.TargetRoot, rule.TargetFolder, rule.ComponentKind, rule.MaximumComponentNameLength));
				}
				catch (ArgumentException error) { throw new ArgumentException("installLayout.rules[" + rules.Count + "]: " + error.Message, error); }
			}
			return new ManagedContentLayoutPlanner(layout.Provider, layout.Revision, layout.MaximumWrapperSegments,
				rules, layout.IgnoredFileNames, layout.RejectedFileSuffixes);
		}
	}
}
