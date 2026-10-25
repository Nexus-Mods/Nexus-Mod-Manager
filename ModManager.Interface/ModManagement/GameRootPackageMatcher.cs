using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
		private const int MaximumConsoleBatchCharacters = 64 * 1024;


		/// <summary>Reports whether this game has configured or characterized native game-root package recognition.</summary>
		public static bool HasRules(IGameMode gameMode)
		{
			IGameRootPackageRuleProvider provider = gameMode as IGameRootPackageRuleProvider;
			return gameMode != null && gameMode.SupportsGameRootModInstall &&
				(HasConsoleBatchRules(gameMode) || (provider != null && provider.GameRootPackageRules != null && provider.GameRootPackageRules.Count > 0));
		}

		/// <summary>Inspects a caller-verified mod archive without writing game, deployment or installation state.</summary>
		public static GameRootPackageMatch Match(IGameMode gameMode, IMod archive,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			if (archive == null) throw new ArgumentNullException(nameof(archive));
			cancellationToken.ThrowIfCancellationRequested();
			if (!HasRules(gameMode)) return null;
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
			if (!HasRules(gameMode)) return null;
			IReadOnlyList<GameRootPackageRule> rules = provider == null ? null : provider.GameRootPackageRules;

			Dictionary<string, string> files = ReadArchivePaths(archiveFiles, cancellationToken);
			if (files == null || files.Count == 0) return null;
			string wrapper = GetCommonWrapper(files.Keys, gameMode.StopFolders);
			if (HasConsoleBatchRules(gameMode))
			{
				GameRootPackageMatch batch = MatchConsoleBatch(GameModeRuntimePolicies.For(gameMode.ModeId), files, String.Empty, openArchiveFile, cancellationToken);
				if (batch == null && wrapper != null)
					batch = MatchConsoleBatch(GameModeRuntimePolicies.For(gameMode.ModeId), files, wrapper, openArchiveFile, cancellationToken);
				if (batch != null) return batch;
			}
			if (rules == null || rules.Count == 0) return null;
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

		/// <summary>Uses configured console-batch recognition, including legacy game modes.</summary>
		private static bool HasConsoleBatchRules(IGameMode gameMode)
		{
			return GameModeRuntimePolicies.For(gameMode.ModeId).ConsoleBatchCommands?.Length > 0;
		}

		/// <summary>Recognizes an executable console batch at the archive root or one proven common wrapper.</summary>
		private static GameRootPackageMatch MatchConsoleBatch(GameModeRuntimePolicy policy, IDictionary<string, string> files, string archiveBase,
			Func<string, Stream> openArchiveFile, CancellationToken cancellationToken)
		{
			string prefix = archiveBase.Length == 0 ? String.Empty : archiveBase + "\\";
			foreach (KeyValuePair<string, string> file in files.OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!file.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
				string relative = file.Key.Substring(prefix.Length);
				if (relative.IndexOf('\\') >= 0 || !String.Equals(Path.GetExtension(relative), ".txt", StringComparison.OrdinalIgnoreCase) ||
					Readme.IsDocumentationFile(relative)) continue;
				using (Stream stream = openArchiveFile(file.Value))
				using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024))
				{
					char[] buffer = new char[MaximumConsoleBatchCharacters + 1];
					int count = reader.ReadBlock(buffer, 0, buffer.Length);
					cancellationToken.ThrowIfCancellationRequested();
					if (count > MaximumConsoleBatchCharacters) continue;
					using (var lines = new StringReader(new String(buffer, 0, count)))
					{
						bool valid = true;
						bool hasCommand = false;
						string line;
						while ((line = lines.ReadLine()) != null)
						{
							line = line.Trim();
							if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;
							int separator = line.IndexOfAny(new[] { ' ', '\t' });
							string command = separator < 0 ? line : line.Substring(0, separator);
							if (!policy.ConsoleBatchCommands.Contains(command, StringComparer.OrdinalIgnoreCase) || line.IndexOf('\0') >= 0 ||
								(separator < 0 && !String.Equals(command, policy.ConsoleBatchNoArgumentCommand, StringComparison.OrdinalIgnoreCase)))
							{
								valid = false;
								break;
							}
							hasCommand = true;
						}
						if (valid && hasCommand)
							return new GameRootPackageMatch(new GameRootPackageRule(policy.ConsoleBatchRuleId, new[] { relative }), archiveBase);
					}
				}
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
