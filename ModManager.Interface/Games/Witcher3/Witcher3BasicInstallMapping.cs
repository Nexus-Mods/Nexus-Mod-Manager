using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.Witcher3
{
	/// <summary>An installation problem with localized guidance suitable for the failure dialog.</summary>
	public sealed class Witcher3InstallException : IOException
	{
		/// <summary>Retains diagnostic details separately from the message shown to the user.</summary>
		public Witcher3InstallException(string message, Exception innerException = null) : base(message, innerException) { }
	}

	/// <summary>
	/// Bridges the W3R.2 read-only planner to the native basic installer WITHOUT writing files.
	/// The existing native installer still owns the transaction, target history, backups, and
	/// Direct/Virtual deployment. Only complete, unambiguous Mods/DLC layouts are accepted.
	/// </summary>
	public static class Witcher3BasicInstallMapping
	{
		// This fixed compatibility policy also validates persisted native targets when no
		// authored layout is available. It must not expand when game.json is edited.
		private static readonly ManagedContentLayoutPlanner NativePathValidator = new ManagedContentLayoutPlanner(
			ManagedContentLayoutPlanner.ProviderId, 1, 0, new[]
			{
				new ManagedContentLayoutRule("mods", "component-folder", "Mods", "mod", "content", "Data", "Mods", "mod-folder", 63),
				new ManagedContentLayoutRule("dlc", "component-folder", "DLC", "dlc", "content", "Data", "DLC", "dlc-folder", 255)
			}, null, new[] { ".part.txt" });

		public static IReadOnlyList<KeyValuePair<string, string>> Map(
			IEnumerable<KeyValuePair<string, string>> selectedFiles, string stablePackageIdentity,
			string suggestedModName, Witcher3Edition edition, ModInstallContext context)
		{
			if (selectedFiles == null) throw new ArgumentNullException(nameof(selectedFiles));
			if (context == null) throw new ArgumentNullException(nameof(context));
			// NMM's normal Data root is the Witcher game root. GameRoot uses a second
			// wrapper-stripping path which is not yet characterized for this adapter.
			if (context.InstallRoot != ModInstallRoot.Data)
				throw new InvalidDataException("Witcher 3 basic mapping supports only the ordinary Data install root.");
			if (edition != Witcher3Edition.Remastered)
				throw new InvalidDataException("Witcher 3 Remastered installation requires confirmed 5.x edition evidence.");

			var input = new List<Witcher3LayoutInputFile>();
			var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, string> file in selectedFiles)
			{
				if (input.Count == ManagedContentLayoutPlanner.MaximumFiles)
					throw new InvalidDataException("Witcher 3 archive exceeds the bounded basic-install file count.");
				if (!String.IsNullOrWhiteSpace(file.Key) && !sources.Add(file.Key.Replace('/', '\\')))
					throw new InvalidDataException("One Witcher archive source cannot be deployed twice in a basic-install selection.");
				input.Add(new Witcher3LayoutInputFile(file.Key,
					String.IsNullOrWhiteSpace(file.Value) ? file.Key : file.Value));
			}

			Witcher3InstallLayoutPlan plan = Witcher3InstallLayoutPlanner.Plan(input,
				stablePackageIdentity, suggestedModName, edition);
			if (!plan.IsUnambiguous || plan.Files.Count == 0)
			{
				throw DescribeLayoutFailure(plan);
			}

			// Vortex's folder-level identity has no relationship to the native file owner:
			// a single archive may contribute multiple mod folders, and a single folder
			// may have multiple native file owners. The native installer handles the latter.
			return new ReadOnlyCollection<KeyValuePair<string, string>>(plan.Files.Select(file =>
				new KeyValuePair<string, string>(file.ArchivePath, file.GameRelativeTargetPath)).ToList());
		}

		/// <summary>Explains a rejected archive without exposing internal policy names or adapter terminology.</summary>
		public static Witcher3InstallException DescribeLayoutFailure(Witcher3InstallLayoutPlan plan,
			IEnumerable<Witcher3LayoutIssue> blockingIssues = null)
		{
			Witcher3LayoutIssue issue = (blockingIssues ?? plan.Issues).FirstOrDefault();
			string message;
			switch (issue?.Kind)
			{
				case Witcher3LayoutIssueKind.ConfigurationMergeRequired:
					message = LanguageManager.Get("Witcher3.Install.SharedSettings", "This mod changes shared game settings that NMM cannot safely combine yet. Use the mod author's manual installation instructions."); break;
				case Witcher3LayoutIssueKind.InvalidPath:
				case Witcher3LayoutIssueKind.DuplicateDestination:
				case Witcher3LayoutIssueKind.FileDirectoryCollision:
					message = LanguageManager.Get("Witcher3.Install.InvalidPaths", "This archive contains invalid or conflicting file paths. Download a corrected archive from the mod author."); break;
				case Witcher3LayoutIssueKind.InconsistentWrapper:
				case Witcher3LayoutIssueKind.UnresolvedInstallerSelection:
					message = LanguageManager.Get("Witcher3.Install.AmbiguousLayout", "NMM cannot determine which files to install from this archive. Choose a version with a standard Mods or DLC folder, or follow the mod author's manual installation instructions."); break;
				case Witcher3LayoutIssueKind.ModFolderTooLong:
					message = LanguageManager.Get("Witcher3.Install.FolderTooLong", "This mod's folder name exceeds the game's 63-character limit. Ask the mod author for a compatible archive."); break;
				default:
					message = LanguageManager.Get("Witcher3.Install.UnsupportedFiles", "This archive contains files that NMM cannot install automatically for this game. Check that you downloaded the Remastered version and follow the mod author's installation instructions."); break;
			}
			if (!String.IsNullOrEmpty(issue?.SourcePath))
				message += Environment.NewLine + LanguageManager.Format("Witcher3.Install.File", "File: {0}", issue.SourcePath);
			string details = String.Join("; ", plan.Issues.Select(item => item.Kind + ": " + item.SourcePath + ": " + item.Explanation));
			return new Witcher3InstallException(message, new InvalidDataException(details));
		}

		/// <summary>
		/// Validates an ALREADY planned native path for every downstream path-projection call,
		/// including VMA relinking. This method never guesses raw content/ or wrapper roots.
		/// </summary>
		public static string ValidatePlannedPath(string gameRelativePath)
		{
			try
			{
				return NativePathValidator.ValidatePlannedPath(gameRelativePath);
			}
			catch (InvalidDataException error)
			{
				throw new InvalidDataException("Witcher 3 native file path is not a reviewed Mods/DLC target: " + gameRelativePath, error);
			}
		}
	}
}
