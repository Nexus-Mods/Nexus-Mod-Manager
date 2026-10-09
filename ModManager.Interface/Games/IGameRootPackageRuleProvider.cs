using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexus.Client.Games
{
	/// <summary>Exposes optional, immutable package recognition rules without changing the legacy GameMode contract.</summary>
	public interface IGameRootPackageRuleProvider
	{
		/// <summary>Gets the rules identifying packages that belong beside the game executable.</summary>
		IReadOnlyList<GameRootPackageRule> GameRootPackageRules { get; }
	}

	/// <summary>Describes an exact file-name signature and optional XML checks at one archive base directory.</summary>
	public sealed class GameRootPackageRule
	{
		/// <summary>Creates a validated rule whose inputs are copied into immutable collections.</summary>
		public GameRootPackageRule(string id, IEnumerable<string> requiredFiles,
			IEnumerable<GameRootPackageXmlCheck> xmlChecks = null, bool allowSingleWrapperFolder = false)
		{
			if (String.IsNullOrWhiteSpace(id) || !Regex.IsMatch(id, "\\A[A-Za-z][A-Za-z0-9._-]*\\z", RegexOptions.CultureInvariant))
				throw new ArgumentException("A rule id must begin with a letter and contain only letters, digits, '.', '_' or '-'.", nameof(id));
			List<string> files = (requiredFiles ?? Enumerable.Empty<string>()).ToList();
			if (files.Count == 0 || files.Any(file => !IsSafeFileName(file)))
				throw new ArgumentException("requiredFiles must contain at least one exact safe file name, without paths or wildcards.", nameof(requiredFiles));
			if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
				throw new ArgumentException("requiredFiles contains duplicate file names.", nameof(requiredFiles));
			List<GameRootPackageXmlCheck> checks = (xmlChecks ?? Enumerable.Empty<GameRootPackageXmlCheck>()).ToList();
			if (checks.Any(check => check == null || !files.Contains(check.FileName, StringComparer.OrdinalIgnoreCase)))
				throw new ArgumentException("Every XML check must refer to one of the requiredFiles.", nameof(xmlChecks));
			if (checks.Select(check => check.FileName.ToUpperInvariant() + "|" + String.Join("/", check.ElementPath)).Distinct(StringComparer.Ordinal).Count() != checks.Count)
				throw new ArgumentException("xmlChecks contains duplicate file/element checks.", nameof(xmlChecks));

			Id = id;
			RequiredFiles = new ReadOnlyCollection<string>(files);
			XmlChecks = new ReadOnlyCollection<GameRootPackageXmlCheck>(checks);
			AllowSingleWrapperFolder = allowSingleWrapperFolder;
		}

		/// <summary>Gets the stable rule identifier for review and diagnostic evidence.</summary>
		public string Id { get; }
		/// <summary>Gets the file names that must all exist directly inside the archive base directory.</summary>
		public IReadOnlyList<string> RequiredFiles { get; }
		/// <summary>Gets the structural checks that must also match.</summary>
		public IReadOnlyList<GameRootPackageXmlCheck> XmlChecks { get; }
		/// <summary>Gets whether one common enclosing folder may be treated as an archive wrapper.</summary>
		public bool AllowSingleWrapperFolder { get; }

		/// <summary>Checks a portable Windows file name, excluding paths, wildcard patterns and ambiguous suffixes.</summary>
		internal static bool IsSafeFileName(string value)
		{
			return !String.IsNullOrWhiteSpace(value) && value != "." && value != ".." &&
				value.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) < 0 &&
				value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !value.Any(Char.IsControl) &&
				!value.EndsWith(".", StringComparison.Ordinal) && !value.EndsWith(" ", StringComparison.Ordinal);
		}
	}

	/// <summary>Identifies an exact sequence of unqualified XML elements beginning at the document root.</summary>
	public sealed class GameRootPackageXmlCheck
	{
		/// <summary>Creates a structural XML check without XPath expressions, namespaces or executable instructions.</summary>
		public GameRootPackageXmlCheck(string fileName, IEnumerable<string> elementPath)
		{
			if (!GameRootPackageRule.IsSafeFileName(fileName))
				throw new ArgumentException("An XML check must name an exact safe file name.", nameof(fileName));
			List<string> elements = (elementPath ?? Enumerable.Empty<string>()).ToList();
			if (elements.Count == 0 || elements.Count > 16 || elements.Any(element => String.IsNullOrEmpty(element) ||
				!Regex.IsMatch(element, "\\A[A-Za-z_][A-Za-z0-9._-]*\\z", RegexOptions.CultureInvariant)))
				throw new ArgumentException("elementPath must contain 1 to 16 literal, unqualified XML element names.", nameof(elementPath));
			FileName = fileName;
			ElementPath = new ReadOnlyCollection<string>(elements);
		}

		/// <summary>Gets the required archive file to inspect.</summary>
		public string FileName { get; }
		/// <summary>Gets the case-sensitive element names, starting with the document element.</summary>
		public IReadOnlyList<string> ElementPath { get; }
	}
}
