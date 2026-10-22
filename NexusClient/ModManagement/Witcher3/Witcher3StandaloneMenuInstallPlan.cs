using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>Reviews standalone custom menus using the same file generation and value history as Collections.</summary>
	internal sealed class Witcher3StandaloneMenuInstallPlan
	{
		internal const string MenuRoot = @"bin\config\r4game\user_config_matrix\pc\";
		internal readonly IReadOnlyList<KeyValuePair<string, string>> ComponentFiles;
		internal readonly IReadOnlyList<ScriptedInstallOperation> Operations;
		internal readonly IReadOnlyList<string> MenuPaths;

		/// <summary>Freezes the exact archive sources, normalized menus and reversible list entries before mutation.</summary>
		private Witcher3StandaloneMenuInstallPlan(List<KeyValuePair<string, string>> components,
			List<ScriptedInstallOperation> operations, List<string> menus)
		{
			ComponentFiles = components.AsReadOnly(); Operations = operations.AsReadOnly(); MenuPaths = menus.AsReadOnly();
		}

		/// <summary>Returns null for ordinary component-only archives; shared game XML remains unsupported here.</summary>
		internal static Witcher3StandaloneMenuInstallPlan TryCreate(IMod mod, ModInstallContext context)
		{
			if (mod.HasInstallScript || context.InstallRoot != ModInstallRoot.Data) return null;
			List<string> sources = mod.GetFileList();
			if (sources == null) throw new InvalidDataException("The archive file inventory is unavailable.");
			if (sources.Count > Nexus.Client.Games.ManagedContent.ManagedContentLayoutPlanner.MaximumFiles)
				throw new InvalidDataException("The archive exceeds the basic-install file count.");
			if (!sources.Any(source => source != null && source.Replace('/', '\\').IndexOf(MenuRoot, StringComparison.OrdinalIgnoreCase) >= 0))
				return null;
			List<Witcher3LayoutInputFile> selected = sources.Where(source => !ModInstallFileFilter.IsIgnored(source))
				.Select(source => new Witcher3LayoutInputFile(source)).ToList();
			Witcher3InstallLayoutPlan layout = Witcher3InstallLayoutPlanner.Plan(selected, mod.Filename, mod.ModName, Witcher3Edition.Remastered);
			List<Witcher3LayoutFile> menus = layout.Files.Where(file => file.Kind == Witcher3LayoutKind.GameConfiguration).ToList();
			if (menus.Count == 0) return null;
			List<Witcher3LayoutIssue> blockers = layout.Issues.Where(issue => issue.Kind != Witcher3LayoutIssueKind.ConfigurationMergeRequired ||
				!menus.Any(menu => String.Equals(menu.ArchivePath, issue.SourcePath, StringComparison.Ordinal) &&
					!Witcher3CollectionRecordStore.RequiresMenuMerge(menu.GameRelativeTargetPath))).ToList();
			if (blockers.Count > 0)
				throw Witcher3BasicInstallMapping.DescribeLayoutFailure(layout, blockers);
			List<KeyValuePair<string, string>> components = layout.Files.Where(file => file.Kind != Witcher3LayoutKind.GameConfiguration)
				.Select(file => new KeyValuePair<string, string>(file.ArchivePath, file.GameRelativeTargetPath)).ToList();
			if (components.Count == 0) throw Witcher3BasicInstallMapping.DescribeLayoutFailure(layout);
			var operations = components.Select(file => (ScriptedInstallOperation)new InstallModFileOperation(file.Key, file.Value)).ToList();
			long total = 0;
			foreach (Witcher3LayoutFile menu in menus)
			{
				try
				{
					byte[] bytes = CollectionWitcher3InstallPlanner.ReadMenuFile(mod, menu.ArchivePath, CancellationToken.None);
					total += bytes.Length;
					if (total > CollectionWitcher3Data.MaximumExpandedBytes) throw new InvalidDataException("Menu input exceeds its size limit.");
					operations.Add(new GenerateDataFileOperation(menu.GameRelativeTargetPath,
						Witcher3CollectionRecordStore.PrepareMenuFile(bytes), menu.ArchivePath));
					string line = Path.GetFileName(menu.GameRelativeTargetPath) + ";";
					foreach (KeyValuePair<string, byte[]> entry in Witcher3CollectionRecordStore.PlanMenu(
						MenuRoot + "dx12filelist.txt", System.Text.Encoding.UTF8.GetBytes(line)))
						operations.Add(new EditGameSpecificValueOperation(entry.Key, entry.Value));
				}
				catch (Exception error) when (error is IOException || error is InvalidDataException || error is System.Xml.XmlException || error is ArgumentException)
				{
					throw new Witcher3InstallException(LanguageManager.Format("Witcher3.Install.InvalidMenu",
						"NMM could not read the mod's settings menu: {0}. Download the mod again or ask its author for a corrected menu file.", menu.ArchivePath), error);
				}
			}
			return new Witcher3StandaloneMenuInstallPlan(components, operations, menus.Select(menu => menu.GameRelativeTargetPath).ToList());
		}

		/// <summary>Explains the currently unsupported upgrade of a standalone mod with recorded menu effects.</summary>
		internal static Witcher3InstallException UpgradeRequiresRemoval()
		{
			return new Witcher3InstallException(LanguageManager.Get("Witcher3.Install.MenuUpgrade",
				"NMM cannot update this mod's settings menu in place yet. Remove the installed version in NMM, then install the new version. Your game saves are not removed."));
		}
	}
}
