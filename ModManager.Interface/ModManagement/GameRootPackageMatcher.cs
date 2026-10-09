using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Nexus.Client.Games;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>Records the matched rule and exact archive base without choosing or changing a deployment method.</summary>
	public sealed class GameRootPackageMatch
	{
		/// <summary>Creates read-only recognition evidence for later native installation planning.</summary>
		internal GameRootPackageMatch(GameRootPackageRule rule, string archiveBaseDirectory)
		{
			Rule = rule;
			ArchiveBaseDirectory = archiveBaseDirectory;
		}

		/// <summary>Gets the immutable rule that matched the archive.</summary>
		public GameRootPackageRule Rule { get; }
		/// <summary>Gets the enclosing folder to strip, or an empty string for an unwrapped package.</summary>
		public string ArchiveBaseDirectory { get; }
	}

	/// <summary>Recognizes exact game-root package signatures from read-only archive inputs.</summary>
	/// <remarks>Callers establish archive identity before using this evidence in a reviewed installation plan.</remarks>
	public static class GameRootPackageMatcher
	{
		private const long MaximumXmlCharacters = 256 * 1024;

		/// <summary>Inspects a caller-verified mod archive without writing game, deployment or installation state.</summary>
		public static GameRootPackageMatch Match(IGameMode gameMode, IMod archive,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			if (archive == null) throw new ArgumentNullException(nameof(archive));
			cancellationToken.ThrowIfCancellationRequested();
			IGameRootPackageRuleProvider provider = gameMode as IGameRootPackageRuleProvider;
			if (!gameMode.SupportsGameRootModInstall || provider == null ||
				provider.GameRootPackageRules == null || provider.GameRootPackageRules.Count == 0) return null;
			return Match(gameMode, archive.GetFileList(), path => archive.GetFileStream(path), cancellationToken);
		}

		/// <summary>Matches verified archive paths and reads only XML files needed by complete file-name signatures.</summary>
		/// <remarks>The matcher disposes returned streams. Archive read failures propagate; a nonmatching XML document returns no match.</remarks>
		public static GameRootPackageMatch Match(IGameMode gameMode, IEnumerable<string> archiveFiles,
			Func<string, Stream> openArchiveFile, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			if (archiveFiles == null) throw new ArgumentNullException(nameof(archiveFiles));
			if (openArchiveFile == null) throw new ArgumentNullException(nameof(openArchiveFile));
			cancellationToken.ThrowIfCancellationRequested();
			IGameRootPackageRuleProvider provider = gameMode as IGameRootPackageRuleProvider;
			if (!gameMode.SupportsGameRootModInstall || provider == null) return null;
			IReadOnlyList<GameRootPackageRule> rules = provider.GameRootPackageRules;
			if (rules == null || rules.Count == 0) return null;

			Dictionary<string, string> files = ReadArchivePaths(archiveFiles, cancellationToken);
			if (files == null || files.Count == 0) return null;
			string wrapper = GetCommonWrapper(files.Keys, gameMode.StopFolders);
			var xmlDocuments = new Dictionary<string, XmlDocument>(StringComparer.OrdinalIgnoreCase);
			foreach (GameRootPackageRule rule in rules.Where(value => value != null).OrderBy(value => value.Id, StringComparer.Ordinal))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (MatchesAtBase(rule, String.Empty, files, openArchiveFile, xmlDocuments, cancellationToken))
					return new GameRootPackageMatch(rule, String.Empty);
				if (rule.AllowSingleWrapperFolder && wrapper != null &&
					MatchesAtBase(rule, wrapper + "\\", files, openArchiveFile, xmlDocuments, cancellationToken))
					return new GameRootPackageMatch(rule, wrapper);
			}
			return null;
		}

		/// <summary>Rejects unsafe or duplicate physical paths and preserves original names for archive reads.</summary>
		private static Dictionary<string, string> ReadArchivePaths(IEnumerable<string> archiveFiles, CancellationToken cancellationToken)
		{
			var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string original in archiveFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (String.IsNullOrWhiteSpace(original)) return null;
				string path = original.Replace('/', '\\');
				bool directory = path.EndsWith("\\", StringComparison.Ordinal);
				if (directory) path = path.Substring(0, path.Length - 1);
				if (path.Split('\\').Any(part => !GameRootPackageRule.IsSafeFileName(part))) return null;
				if (directory) continue;
				if (files.ContainsKey(path)) return null;
				files.Add(path, original);
			}
			return files;
		}

		/// <summary>Recognizes a single common wrapper while excluding the game's known content folders.</summary>
		private static string GetCommonWrapper(IEnumerable<string> paths, IEnumerable<string> stopFolders)
		{
			string wrapper = null;
			foreach (string path in paths)
			{
				int separator = path.IndexOf('\\');
				if (separator <= 0) return null;
				string topFolder = path.Substring(0, separator);
				if (wrapper == null) wrapper = topFolder;
				else if (!StringComparer.OrdinalIgnoreCase.Equals(wrapper, topFolder)) return null;
			}
			if (StringComparer.OrdinalIgnoreCase.Equals(wrapper, "Data")) return null;
			if ((stopFolders ?? Enumerable.Empty<string>()).Any(folder => !String.IsNullOrEmpty(folder) &&
				StringComparer.OrdinalIgnoreCase.Equals(wrapper, folder.Replace('/', '\\').Split('\\')[0]))) return null;
			return wrapper;
		}

		/// <summary>Requires every marker and XML check at the same exact archive base.</summary>
		private static bool MatchesAtBase(GameRootPackageRule rule, string prefix, IDictionary<string, string> files,
			Func<string, Stream> openArchiveFile, IDictionary<string, XmlDocument> xmlDocuments, CancellationToken cancellationToken)
		{
			if (rule.RequiredFiles.Any(file => !files.ContainsKey(prefix + file))) return false;
			foreach (GameRootPackageXmlCheck check in rule.XmlChecks)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string path = prefix + check.FileName;
				XmlDocument document;
				if (!xmlDocuments.TryGetValue(path, out document))
				{
					document = ReadXml(files[path], openArchiveFile, cancellationToken);
					xmlDocuments.Add(path, document);
				}
				if (document == null || !MatchesElementPath(document.DocumentElement, check.ElementPath, 0)) return false;
			}
			return true;
		}

		/// <summary>Reads a bounded XML document with DTD and external resource resolution disabled.</summary>
		private static XmlDocument ReadXml(string path, Func<string, Stream> openArchiveFile, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (Stream stream = openArchiveFile(path))
			{
				if (stream == null) throw new InvalidDataException("The archive did not provide the required XML file: " + path);
				try
				{
					var settings = new XmlReaderSettings
					{
						DtdProcessing = DtdProcessing.Prohibit,
						XmlResolver = null,
						MaxCharactersInDocument = MaximumXmlCharacters
					};
					var document = new XmlDocument { XmlResolver = null };
					using (XmlReader reader = XmlReader.Create(stream, settings)) document.Load(reader);
					cancellationToken.ThrowIfCancellationRequested();
					return document;
				}
				catch (XmlException)
				{
					cancellationToken.ThrowIfCancellationRequested();
					return null;
				}
			}
		}

		/// <summary>Matches literal child elements without accepting namespaces or descendant-only matches.</summary>
		private static bool MatchesElementPath(XmlElement element, IReadOnlyList<string> path, int index)
		{
			if (element == null || element.NamespaceURI.Length != 0 || !StringComparer.Ordinal.Equals(element.Name, path[index])) return false;
			if (index == path.Count - 1) return true;
			foreach (XmlNode child in element.ChildNodes)
			{
				XmlElement childElement = child as XmlElement;
				if (childElement != null && MatchesElementPath(childElement, path, index + 1)) return true;
			}
			return false;
		}
	}
}
