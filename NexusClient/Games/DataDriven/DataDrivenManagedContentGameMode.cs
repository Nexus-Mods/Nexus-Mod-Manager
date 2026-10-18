using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Mods;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>
	/// Capability host for configured advanced layouts. Reuses native BasicInstall, InstallLog,
	/// Direct/Virtual installation and ordinary data-driven discovery, launch and settings.
	/// </summary>
	public class DataDrivenManagedContentGameMode : DataDrivenGameMode, IReviewedBasicInstallMappingProvider, IManagedContentComponentProvider, IManagedContentOrderDocumentProvider
	{
		private readonly ManagedContentLayoutPlanner _layout;
		private readonly IniOrderDocumentRule _orderDocument;

		/// <summary>Resolves the configured layout once; an unsupported capability never falls back to ordinary extraction.</summary>
		public DataDrivenManagedContentGameMode(IEnvironmentInfo environmentInfo, FileUtil fileUtility, GameModeDefinition definition)
			: base(environmentInfo, fileUtility, RequireDefinition(definition))
		{
			_layout = DataDrivenManagedContentRules.Create(definition.ManagedContent);
			_orderDocument = DataDrivenManagedContentRules.CreateOrdering(definition.ManagedContent);
		}

		/// <summary>Rejects programmatic configuration bypasses using the same validator as the normal JSON loader.</summary>
		private static GameModeDefinition RequireDefinition(GameModeDefinition definition)
		{
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			if (definition.BehaviorProfile != "managed-content") throw new ArgumentException("The managed-content host requires its own profile.");
			List<GameModeDefinitionIssue> issues = new GameModeDefinitionValidator().Validate(definition)
				.Where(issue => issue.Severity == GameModeDefinitionIssueSeverity.Error).ToList();
			if (issues.Count > 0) throw new ArgumentException(String.Join("; ", issues.Select(issue => issue.PropertyPath + ": " + issue.Message)));
			return definition;
		}

		/// <summary>Supplies native component diagnostics from the same immutable rules used for install planning.</summary>
		public IReadOnlyList<ManagedContentLayoutRule> ComponentRules => _layout.Rules;

		/// <summary>Optional configured order-file observation, resolved once for this mode instance.</summary>
		public IniOrderDocumentRule OrderDocument => _orderDocument;

		/// <summary>Returns the Data root without creating a directory while native planning checks readme destinations.</summary>
		public override string PluginDirectory => InstallationPath;

		/// <summary>Maps exact selected files through the same native hook used by Direct, Virtual and Collections planning.</summary>
		public virtual IReadOnlyList<KeyValuePair<string, string>> MapBasicInstallFiles(IMod mod,
			ModInstallContext installContext, IEnumerable<KeyValuePair<string, string>> selectedFiles)
		{
			ManagedContentInstallSelection selection = ManagedContentInstallSelection.Create(mod, installContext, selectedFiles.Where(file => !IsStructuredMetadataFile(file.Key)), ModeId);
			return selection.Map(_layout);
		}

		/// <summary>Preserves an already reviewed target during VMA relinking without a second layout interpretation.</summary>
		public override string GetModFormatAdjustedPath(IModFormat format, string path, bool ignoreIfPresent) => IsStructuredMetadataFile(path) ? String.Empty : _layout.ValidatePlannedPath(path);

		/// <summary>Uses the same canonical target for native game and virtual-storage projection.</summary>
		public override string GetModFormatAdjustedPath(IModFormat format, string path, IMod mod, bool ignoreIfPresent) => IsStructuredMetadataFile(path) ? String.Empty : _layout.ValidatePlannedPath(path);
	}
}
