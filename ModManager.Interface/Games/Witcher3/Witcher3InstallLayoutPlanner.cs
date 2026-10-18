using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.Games.Witcher3
{
	/// <summary>What a verified, selected archive destination represents in the game tree.</summary>
	public enum Witcher3LayoutKind
	{
		ModComponent = 0,
		DlcComponent = 1,
		GameConfiguration = 2
	}

	/// <summary>Problems a caller must resolve before feeding any path into the native installer.</summary>
	public enum Witcher3LayoutIssueKind
	{
		InvalidPath = 0,
		UnsupportedPayload = 1,
		InconsistentWrapper = 2,
		MissingPayload = 3,
		UnresolvedInstallerSelection = 4,
		DocumentSettingsRequireAdapter = 5,
		ConfigurationMergeRequired = 6,
		MissingPackageIdentity = 7,
		ModFolderTooLong = 8,
		DuplicateDestination = 9,
		FileDirectoryCollision = 10,
		ExistingTargetCollision = 11,
		ExistingComponentRequiresOwnershipReview = 12,
		InvalidOccupiedPath = 13,
		InconsistentComponentCase = 14
	}

	/// <summary>
	/// One actual selected file from a native installer or simple archive. SelectedDestinationPath
	/// is the *installer-selected* relative path, never an inferred FOMOD alternative.
	/// </summary>
	public sealed class Witcher3LayoutInputFile
	{
		public Witcher3LayoutInputFile(string archivePath, string selectedDestinationPath)
		{
			ArchivePath = archivePath;
			SelectedDestinationPath = selectedDestinationPath;
		}

		/// <summary>Convenience for an ordinary archive without scripted destination choices.</summary>
		public Witcher3LayoutInputFile(string archivePath) : this(archivePath, archivePath) { }

		public string ArchivePath { get; }
		public string SelectedDestinationPath { get; }
	}

	/// <summary>One reviewed mapping, expressed relative to the *game root*, not to the virtual store.</summary>
	public sealed class Witcher3LayoutFile
	{
		internal Witcher3LayoutFile(string archivePath, string selectedPath, string targetPath,
			Witcher3LayoutKind kind, string componentPath)
		{
			ArchivePath = archivePath;
			SelectedDestinationPath = selectedPath;
			GameRelativeTargetPath = targetPath;
			Kind = kind;
			GameRelativeComponentPath = componentPath;
		}

		public string ArchivePath { get; }
		public string SelectedDestinationPath { get; }
		public string GameRelativeTargetPath { get; }
		public Witcher3LayoutKind Kind { get; }
		/// <summary>Mod or DLC folder; null for a root/configuration file.</summary>
		public string GameRelativeComponentPath { get; }
	}

	public sealed class Witcher3LayoutIssue
	{
		internal Witcher3LayoutIssue(Witcher3LayoutIssueKind kind, string sourcePath, string explanation)
		{
			Kind = kind;
			SourcePath = sourcePath;
			Explanation = explanation;
		}

		public Witcher3LayoutIssueKind Kind { get; }
		public string SourcePath { get; }
		public string Explanation { get; }
	}

	/// <summary>
	/// A read-only review snapshot. Even without issues, the caller MUST reconcile native owners,
	/// Direct/Virtual install state and physical-root aliases before authorizing a write.
	/// </summary>
	public sealed class Witcher3InstallLayoutPlan
	{
		internal Witcher3InstallLayoutPlan(string wrapper, List<Witcher3LayoutFile> files,
			List<string> ignoredDocumentation, List<Witcher3LayoutIssue> issues)
		{
			Wrapper = wrapper;
			Files = new ReadOnlyCollection<Witcher3LayoutFile>(files);
			IgnoredDocumentation = new ReadOnlyCollection<string>(ignoredDocumentation);
			Issues = new ReadOnlyCollection<Witcher3LayoutIssue>(issues);
		}

		/// <summary>Consistent stripped wrapper, or null if the layout was ambiguous.</summary>
		public string Wrapper { get; }
		public IReadOnlyList<Witcher3LayoutFile> Files { get; }
		public IReadOnlyList<string> IgnoredDocumentation { get; }
		public IReadOnlyList<Witcher3LayoutIssue> Issues { get; }
		public bool IsUnambiguous => Issues.Count == 0;
		/// <summary>This planner never constitutes deployment authorization, even for a valid layout.</summary>
		public bool RequiresNativeOwnershipReview => true;
	}

	/// <summary>
	/// An intentionally narrow, deterministic Witcher 3 path planner. No filesystem access or
	/// game-mode writes. This does not replace native InstallLog / basic, scripted or Virtual installers.
	/// It accepts only *selected file destinations*. Directory entries and unresolved FOMOD choices
	/// are not file mappings, and no prefix is guessed from archive titles or search results.
	/// </summary>
	public static class Witcher3InstallLayoutPlanner
	{
		private const int MaxRemasteredModFolderLength = 63;
		private const int MaximumWrapperSegments = 2;
		private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

		private sealed class ParsedFile
		{
			internal Witcher3LayoutInputFile Input;
			internal string[] Segments;
			internal int Anchor;
			internal bool HasAnchor;
		}

		/// <param name="selectedFiles">File source + *actual selected* destination (no unselected FOMOD listing).</param>
		/// <param name="stablePackageIdentity">Unique, stable archive identity; mandatory for raw content/.</param>
		/// <param name="suggestedModName">Display stem for a generated raw-content mod folder.</param>
		/// <param name="edition">Used only to apply known edition-specific filename restrictions.</param>
		/// <param name="occupiedGamePaths">Optional, read-only inventory of known game-relative files and component folders. Not a substitute for native owner validation.</param>
		public static Witcher3InstallLayoutPlan Plan(IEnumerable<Witcher3LayoutInputFile> selectedFiles,
			string stablePackageIdentity, string suggestedModName,
			Witcher3Edition edition = Witcher3Edition.Ambiguous,
			IEnumerable<string> occupiedGamePaths = null)
		{
			if (selectedFiles == null)
				throw new ArgumentNullException(nameof(selectedFiles));
			var files = new List<Witcher3LayoutFile>();
			var ignored = new List<string>();
			var issues = new List<Witcher3LayoutIssue>();
			var parsed = new List<ParsedFile>();

			foreach (Witcher3LayoutInputFile file in selectedFiles)
			{
				string[] archiveParts;
				if (file == null || !TryParseRelativePath(file.ArchivePath, out archiveParts))
				{
					Issue(issues, Witcher3LayoutIssueKind.InvalidPath, file?.ArchivePath,
						"The archive source is not a safe relative file path.");
					continue;
				}
				if (file.SelectedDestinationPath == null)
				{
					Issue(issues, Witcher3LayoutIssueKind.UnresolvedInstallerSelection, file.ArchivePath,
						"Resolve scripted installer choices before planning their destinations.");
					continue;
				}
				string[] selectedParts;
				if (!TryParseRelativePath(file.SelectedDestinationPath, out selectedParts))
				{
					Issue(issues, Witcher3LayoutIssueKind.InvalidPath, file.ArchivePath,
						"The selected destination is not a safe relative file path.");
					continue;
				}
				var item = new ParsedFile { Input = file, Segments = selectedParts };
				item.HasAnchor = TryFindAnchor(selectedParts, out item.Anchor);
				parsed.Add(item);
			}

			string[] wrapperParts = null;
			bool inconsistentWrapper = false;
			foreach (ParsedFile item in parsed.Where(value => value.HasAnchor))
			{
				string[] prefix = item.Segments.Take(item.Anchor).ToArray();
				if (wrapperParts == null)
					wrapperParts = prefix;
				else if (!SameSegments(wrapperParts, prefix))
					inconsistentWrapper = true;
			}
			if (inconsistentWrapper)
					Issue(issues, Witcher3LayoutIssueKind.InconsistentWrapper, null,
						"Recognized files require different archive roots; refusing to guess a wrapper.");

			string wrapper = inconsistentWrapper ? null : Join(wrapperParts ?? new string[0]);
			string generatedModFolder = null;
			var targetPaths = new HashSet<string>(PathComparer);
			var componentSpelling = new Dictionary<string, string>(PathComparer);
			foreach (ParsedFile item in parsed)
			{
				string[] parts = item.Segments;
				if (IsDocumentation(parts, new string[0]) ||
					(!inconsistentWrapper && IsDocumentation(parts, wrapperParts ?? new string[0])))
				{
					ignored.Add(item.Input.ArchivePath);
					continue;
				}

				if (inconsistentWrapper)
					continue; // Never emit an actionable partial plan with a guessed root.
				if (wrapperParts != null && !StartsWith(parts, wrapperParts))
				{
					Issue(issues, Witcher3LayoutIssueKind.UnsupportedPayload, item.Input.ArchivePath,
						"This file is outside the common game-root wrapper.");
					continue;
				}
				string[] relative = parts.Skip(wrapperParts?.Length ?? 0).ToArray();
				if (relative.Length == 0)
				{
					Issue(issues, Witcher3LayoutIssueKind.UnsupportedPayload, item.Input.ArchivePath,
						"A wrapper alone is not an installable file.");
					continue;
				}

				string[] target = null;
				string component = null;
				Witcher3LayoutKind kind = Witcher3LayoutKind.ModComponent;
				if (Eq(relative[0], "Mods") || Eq(relative[0], "DLC"))
				{
					bool mod = Eq(relative[0], "Mods");
					if (relative.Length < 4 || !StartsWithFolder(relative[1], mod ? "mod" : "dlc") ||
						!Eq(relative[2], "content"))
					{
						Issue(issues, Witcher3LayoutIssueKind.UnsupportedPayload, item.Input.ArchivePath,
							"A Mods/DLC package needs a mod*/dlc*/content/<file> structure.");
						continue;
					}
					kind = mod ? Witcher3LayoutKind.ModComponent : Witcher3LayoutKind.DlcComponent;
					target = (string[])relative.Clone();
					target[0] = mod ? "Mods" : "DLC";
					component = Join(target.Take(2));
				}
				else if (relative.Length >= 3 && StartsWithFolder(relative[0], "mod") && Eq(relative[1], "content"))
				{
					target = new[] { "Mods" }.Concat(relative).ToArray();
					component = Join(target.Take(2));
				}
				else if (relative.Length >= 3 && StartsWithFolder(relative[0], "dlc") && Eq(relative[1], "content"))
				{
					target = new[] { "DLC" }.Concat(relative).ToArray();
					component = Join(target.Take(2));
					kind = Witcher3LayoutKind.DlcComponent;
				}
				else if (Eq(relative[0], "content") && relative.Length >= 2)
				{
					if (generatedModFolder == null)
					{
						generatedModFolder = GenerateModFolder(stablePackageIdentity, suggestedModName);
						if (generatedModFolder == null)
						{
							Issue(issues, Witcher3LayoutIssueKind.MissingPackageIdentity, item.Input.ArchivePath,
								"Raw content/ requires a stable package identity and a suggested mod name.");
							continue;
						}
					}
					target = new[] { "Mods", generatedModFolder }.Concat(relative).ToArray();
					component = Join(target.Take(2));
				}
				else if (IsMatrixXml(relative))
				{
					kind = Witcher3LayoutKind.GameConfiguration;
					target = relative;
					Issue(issues, Witcher3LayoutIssueKind.ConfigurationMergeRequired, item.Input.ArchivePath,
						"Game configuration XML cannot be replaced without a verified merge/recovery adapter.");
					if (edition == Witcher3Edition.Remastered && Eq(relative[5], "graphicsdx11.xml"))
						Issue(issues, Witcher3LayoutIssueKind.UnsupportedPayload, item.Input.ArchivePath,
							"Remastered does not ship the DirectX 11 graphics matrix to merge against.");
				}
				else if (relative[relative.Length - 1].EndsWith(".part.txt", StringComparison.OrdinalIgnoreCase))
				{
					Issue(issues, Witcher3LayoutIssueKind.ConfigurationMergeRequired, item.Input.ArchivePath,
						"A .part.txt fragment requires a verified settings merge, not a raw copy.");
					continue;
				}
				else if (IsDocumentSettings(relative))
				{
					Issue(issues, Witcher3LayoutIssueKind.DocumentSettingsRequireAdapter, item.Input.ArchivePath,
						"Documents/settings changes need a separate, reviewed native effect adapter.");
					continue;
				}
				else if (Eq(relative[0], "fomod"))
				{
					Issue(issues, Witcher3LayoutIssueKind.UnresolvedInstallerSelection, item.Input.ArchivePath,
						"FOMOD metadata is not a selected game destination.");
					continue;
				}
				else
				{
					Issue(issues, Witcher3LayoutIssueKind.UnsupportedPayload, item.Input.ArchivePath,
						"Unrecognized root/binary/tool payload; no destination was guessed.");
					continue;
				}

				if (item.Input.ArchivePath.EndsWith(".part.txt", StringComparison.OrdinalIgnoreCase) ||
					item.Input.SelectedDestinationPath.EndsWith(".part.txt", StringComparison.OrdinalIgnoreCase))
					Issue(issues, Witcher3LayoutIssueKind.ConfigurationMergeRequired, item.Input.ArchivePath,
						"A .part.txt fragment requires a verified settings merge, not a raw copy.");

				if (kind == Witcher3LayoutKind.ModComponent && edition == Witcher3Edition.Remastered &&
					target[1].Length > MaxRemasteredModFolderLength)
					Issue(issues, Witcher3LayoutIssueKind.ModFolderTooLong, item.Input.ArchivePath,
						"Remastered may ignore a mod folder exceeding 63 characters; never rename it implicitly.");

				string targetPath = Join(target);
				if (!targetPaths.Add(targetPath))
					Issue(issues, Witcher3LayoutIssueKind.DuplicateDestination, item.Input.ArchivePath,
						"Multiple archive entries resolve to the same Windows destination.");
				files.Add(new Witcher3LayoutFile(item.Input.ArchivePath, item.Input.SelectedDestinationPath,
					targetPath, kind, component));
			}

			// A file cannot also be an ancestor directory of a different file. The check is
			// linear in the number of path components, not quadratic in archive file count.
			foreach (Witcher3LayoutFile file in files)
				foreach (string parent in ParentPaths(file.GameRelativeTargetPath))
					if (targetPaths.Contains(parent))
						Issue(issues, Witcher3LayoutIssueKind.FileDirectoryCollision, file.ArchivePath,
							"An archive destination is also used as another file's parent directory.");

			CheckOccupiedPaths(files, occupiedGamePaths, issues);
			if (files.Count == 0)
				Issue(issues, Witcher3LayoutIssueKind.MissingPayload, null,
					"There are no supported selected files to install.");
			return new Witcher3InstallLayoutPlan(wrapper, files, ignored, issues);
		}

		private static void CheckOccupiedPaths(IReadOnlyList<Witcher3LayoutFile> files,
			IEnumerable<string> occupiedGamePaths, List<Witcher3LayoutIssue> issues)
		{
			if (occupiedGamePaths == null)
				return;
			var occupied = new HashSet<string>(PathComparer);
			var existingParents = new HashSet<string>(PathComparer);
			var occupiedComponents = new HashSet<string>(PathComparer);
			foreach (string path in occupiedGamePaths)
			{
				string[] parts;
				if (!TryParseRelativePath(path, out parts))
				{
					Issue(issues, Witcher3LayoutIssueKind.InvalidOccupiedPath, path,
						"The supplied game inventory contained an unsafe or invalid relative path.");
					continue;
				}
				bool isComponent = parts.Length >= 2 && (Eq(parts[0], "Mods") || Eq(parts[0], "DLC"));
				if (isComponent)
					occupiedComponents.Add(Join(parts.Take(2)));
				// A supplied component directory is not an occupied *file*: a perfectly
				// ordinary target beneath it must not be reported as a file/dir collision.
				if (parts.Length == 2 && isComponent) continue;
				if (parts.Length == 1 && (Eq(parts[0], "Mods") || Eq(parts[0], "DLC"))) continue;
				string normalized = Join(parts);
				occupied.Add(normalized);
				foreach (string parent in ParentPaths(normalized))
					existingParents.Add(parent);
			}

			var flaggedComponents = new HashSet<string>(PathComparer);
			foreach (Witcher3LayoutFile file in files)
			{
				if (occupied.Contains(file.GameRelativeTargetPath) || existingParents.Contains(file.GameRelativeTargetPath) ||
					ParentPaths(file.GameRelativeTargetPath).Any(occupied.Contains))
					Issue(issues, Witcher3LayoutIssueKind.ExistingTargetCollision, file.ArchivePath,
						"An existing game path would be overwritten or used as both a file and directory.");
				if (file.GameRelativeComponentPath != null &&
					occupiedComponents.Contains(file.GameRelativeComponentPath) &&
					flaggedComponents.Add(file.GameRelativeComponentPath))
					Issue(issues, Witcher3LayoutIssueKind.ExistingComponentRequiresOwnershipReview,
						file.ArchivePath, "An existing Mods/DLC component has this folder name; native ownership must be reviewed.");
			}
		}

		private static bool TryFindAnchor(string[] parts, out int anchor)
		{
			for (int i = 0; i <= Math.Min(MaximumWrapperSegments, parts.Length - 1); i++)
			{
				string segment = parts[i];
				if (Eq(segment, "Mods") || Eq(segment, "DLC") || Eq(segment, "content") ||
					Eq(segment, "bin") || Eq(segment, "fomod") || Eq(segment, "Documents") ||
					IsDocumentSettings(parts, i) ||
					(i + 1 < parts.Length && Eq(parts[i + 1], "content") &&
						(StartsWithFolder(segment, "mod") || StartsWithFolder(segment, "dlc"))))
				{
					anchor = i;
					return true;
				}
			}
			anchor = -1;
			return false;
		}

		private static bool IsMatrixXml(string[] path)
		{
			return path.Length == 6 && Eq(path[0], "bin") && Eq(path[1], "config") &&
				Eq(path[2], "r4game") && Eq(path[3], "user_config_matrix") &&
				Eq(path[4], "pc") && path[5].EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
				path[5].Length > ".xml".Length;
		}

		private static bool IsDocumentSettings(string[] path, int offset = 0)
		{
			if (offset >= path.Length) return false;
			if (Eq(path[offset], "Documents")) return true;
			return path.Length - offset == 1 && (Eq(path[offset], "input.settings") ||
				Eq(path[offset], "user.settings") || Eq(path[offset], "dx12user.settings") ||
				Eq(path[offset], "mods.settings") || Eq(path[offset], "input.xml") ||
				path[offset].EndsWith(".part.txt", StringComparison.OrdinalIgnoreCase));
		}

		private static bool IsDocumentation(string[] path, string[] prefix)
		{
			if (!StartsWith(path, prefix) || path.Length != prefix.Length + 1) return false;
			string name = path[path.Length - 1];
			return Eq(name, "readme.txt") || Eq(name, "readme.md") ||
				Eq(name, "license.txt") || Eq(name, "license.md") ||
				Eq(name, "changelog.txt") || Eq(name, "changelog.md") || Eq(name, ".DS_Store");
		}

		/// <summary>Preserves the existing Witcher folder name through the shared deterministic naming primitive.</summary>
		private static string GenerateModFolder(string identity, string suggestedName)
		{
			return ManagedContentPath.GenerateComponentName(identity, suggestedName, "mod", MaxRemasteredModFolderLength);
		}

		/// <summary>Uses the same Windows path restrictions as configurable managed-content layouts.</summary>
		private static bool TryParseRelativePath(string path, out string[] parts)
		{
			return ManagedContentPath.TryParse(path, out parts);
		}

		private static bool StartsWithFolder(string candidate, string prefix)
		{
			return candidate.Length > prefix.Length && candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
		}

		private static bool Eq(string lhs, string rhs) => PathComparer.Equals(lhs, rhs);

		private static bool StartsWith(string[] parts, string[] prefix)
		{
			if (prefix.Length > parts.Length) return false;
			for (int i = 0; i < prefix.Length; i++)
				if (!Eq(parts[i], prefix[i])) return false;
			return true;
		}

		private static bool SameSegments(string[] lhs, string[] rhs) => lhs.Length == rhs.Length && StartsWith(lhs, rhs);
		private static string Join(IEnumerable<string> path) => String.Join("\\", path);

		private static IEnumerable<string> ParentPaths(string path)
		{
			for (int offset = path.IndexOf('\\'); offset >= 0; offset = path.IndexOf('\\', offset + 1))
				yield return path.Substring(0, offset);
		}

		private static void Issue(List<Witcher3LayoutIssue> issues, Witcher3LayoutIssueKind kind,
			string source, string explanation)
		{
			issues.Add(new Witcher3LayoutIssue(kind, source, explanation));
		}
	}
}
