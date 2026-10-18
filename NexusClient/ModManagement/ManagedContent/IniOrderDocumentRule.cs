using System;
using System.IO;
using System.Linq;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Optional configured order document; editing requires a separately declared provider and native authority.</summary>
	public interface IManagedContentOrderDocumentProvider
	{
		/// <summary>Immutable order rule, or null when the game declares no ordering capability.</summary>
		IniOrderDocumentRule OrderDocument { get; }
	}

	/// <summary>Versioned, literal INI order-file location and component association resolved from game.json.</summary>
	public sealed class IniOrderDocumentRule
	{
		public const string ProviderId = "sectioned-ini-order-v1";
		public const string EditProviderId = "reviewed-sequential-ini-v1";

		/// <summary>Preserves the existing observation-only constructor for compiled consumers.</summary>
		public IniOrderDocumentRule(string provider, int revision, string fileRoot, string relativePath,
			string componentKind, string targetFolder, string componentPrefix, string priorityKey, string enabledKey)
			: this(provider, revision, fileRoot, relativePath, componentKind, targetFolder, componentPrefix, priorityKey, enabledKey, null)
		{
		}

		/// <summary>Captures a bounded observation contract without accepting absolute paths, executable templates or arbitrary write policies.</summary>
		public IniOrderDocumentRule(string provider, int revision, string fileRoot, string relativePath,
			string componentKind, string targetFolder, string componentPrefix, string priorityKey, string enabledKey, string editing)
		{
			if (provider != ProviderId) throw new ArgumentException("Unknown componentOrdering provider: " + provider);
			if (revision < 1) throw new ArgumentException("componentOrdering.revision must be positive.");
			if (fileRoot != "GameRoot" && fileRoot != "Documents") throw new ArgumentException("componentOrdering.fileRoot must be GameRoot or Documents.");
			string[] parts;
			if (!ManagedContentPath.TryParse(relativePath, out parts)) throw new ArgumentException("componentOrdering.relativePath must be a safe relative file path.");
			RelativePath = String.Join("\\", parts);
			if (!ManagedContentPath.TryParse(targetFolder, out parts) || parts.Length != 1) throw new ArgumentException("A literal component target folder is required.");
			if (String.IsNullOrEmpty(componentKind) || !Char.IsLetter(componentKind[0]) || !componentKind.All(c =>
				(c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.'))
				throw new ArgumentException("componentOrdering.componentKind must be an identifier.");
			if (editing != null && editing != EditProviderId) throw new ArgumentException("Unknown componentOrdering.editing provider: " + editing);
			Editing = editing;
			Revision = revision;
			FileRoot = fileRoot;
			ComponentKind = componentKind;
			TargetFolder = targetFolder;
			Format = new IniOrderFormat(componentPrefix, priorityKey, enabledKey);
		}

		public string Editing { get; }
		public int Revision { get; }
		public string FileRoot { get; }
		public string RelativePath { get; }
		public string ComponentKind { get; }
		public string TargetFolder { get; }
		public IniOrderFormat Format { get; }

		/// <summary>Resolves the literal file below the selected current root without creating directories or expanding templates.</summary>
		public string ResolvePath(string gameRoot, string documentsRoot)
		{
			string selected = FileRoot == "Documents" ? documentsRoot : gameRoot;
			if (String.IsNullOrWhiteSpace(selected) || !Path.IsPathRooted(selected))
				throw new ArgumentException("The selected order-file root must be an absolute configured directory.");
			string root = Path.GetFullPath(selected);
			string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? root : root + Path.DirectorySeparatorChar;
			string path = Path.GetFullPath(Path.Combine(root, RelativePath));
			if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The order-file path is outside its configured root.");
			return path;
		}
	}
}
