using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.Games.Witcher3
{
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
				string reason = String.Join("; ", plan.Issues.Take(4)
					.Select(issue => issue.Kind + ": " + issue.Explanation));
				throw new InvalidDataException("Witcher 3 archive layout needs review before native installation: " + reason);
			}

			// Vortex's folder-level identity has no relationship to the native file owner:
			// a single archive may contribute multiple mod folders, and a single folder
			// may have multiple native file owners. The native installer handles the latter.
			return new ReadOnlyCollection<KeyValuePair<string, string>>(plan.Files.Select(file =>
				new KeyValuePair<string, string>(file.ArchivePath, file.GameRelativeTargetPath)).ToList());
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
