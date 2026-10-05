using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Outcome of applying the characterized Vortex version matcher to one concrete version.</summary>
	public enum CollectionVortexVersionMatchResult
	{
		Unknown = 0,
		NoMatch = 1,
		Match = 2
	}

	/// <summary>
	/// Immutable, deliberately bounded Vortex version matcher used by characterized Collection conflict rules.
	/// </summary>
	/// <remarks>
	/// The supported range grammar is intentionally smaller than npm-semver: wildcard, exact numeric/text versions,
	/// or an OR-list of one numeric comparator per clause (for example <c>&lt;0.7.9 || &gt;0.7.9</c>). A terminal
	/// Vortex <c>+prefer</c> marker is retained as fuzzy-reference metadata and removed before version evaluation.
	/// Unsupported range syntax is rejected during normalization rather than approximated.
	/// </remarks>
	public sealed class CollectionVortexVersionMatch : IEquatable<CollectionVortexVersionMatch>
	{
		private static readonly Regex ComparatorPattern = new Regex(
			@"^(?<op><=|>=|<|>|=)\s*(?<version>v?[0-9]+(?:\.[0-9]+){0,2})$",
			RegexOptions.CultureInvariant | RegexOptions.Compiled);
		private readonly ReadOnlyCollection<ComparatorClause> _clauses;

		private CollectionVortexVersionMatch(string expression, string matchExpression, bool any, bool range, bool prefer, IEnumerable<ComparatorClause> clauses)
		{
			Expression = expression;
			MatchExpression = matchExpression;
			IsAny = any;
			IsRange = range;
			IsPrefer = prefer;
			_clauses = new ReadOnlyCollection<ComparatorClause>((clauses ?? Enumerable.Empty<ComparatorClause>()).ToList());
		}

		public string Expression { get; }
		internal string MatchExpression { get; }
		public bool IsAny { get; }
		public bool IsRange { get; }
		public bool IsPrefer { get; }
		public bool IsFuzzy { get { return IsAny || IsRange || IsPrefer; } }

		/// <summary>Parses only the Vortex matcher subset that NMM has explicitly characterized.</summary>
		public static bool TryCreate(string expression, out CollectionVortexVersionMatch match, out string failure)
		{
			match = null;
			failure = null;
			if (String.IsNullOrWhiteSpace(expression) || !StringComparer.Ordinal.Equals(expression, expression.Trim()))
			{
				failure = "A conflict reference versionMatch must be a non-empty normalized string.";
				return false;
			}
			if (StringComparer.Ordinal.Equals(expression, "*"))
			{
				match = new CollectionVortexVersionMatch(expression, expression, true, false, false, null);
				return true;
			}
			bool prefer = expression.EndsWith("+prefer", StringComparison.Ordinal);
			string matchExpression = prefer ? expression.Substring(0, expression.Length - "+prefer".Length) : expression;
			if (String.IsNullOrWhiteSpace(matchExpression) || matchExpression.IndexOf('+') >= 0)
			{
				failure = "Only the characterized terminal Vortex +prefer suffix is supported in conflict versionMatch values.";
				return false;
			}

			bool looksLikeRange = matchExpression.IndexOf('<') >= 0 || matchExpression.IndexOf('>') >= 0 ||
				matchExpression.IndexOf('=') >= 0 || matchExpression.IndexOf("||", StringComparison.Ordinal) >= 0;
			if (!looksLikeRange)
			{
				match = new CollectionVortexVersionMatch(expression, matchExpression, false, false, prefer, null);
				return true;
			}

			string[] rawClauses = matchExpression.Split(new[] { "||" }, StringSplitOptions.None);
			if (rawClauses.Length == 0)
			{
				failure = "The Vortex conflict version range is empty.";
				return false;
			}
			var clauses = new List<ComparatorClause>();
			foreach (string rawClause in rawClauses)
			{
				string clause = rawClause.Trim();
				Match parsed = ComparatorPattern.Match(clause);
				NumericVersion version;
				if (!parsed.Success || !NumericVersion.TryParse(parsed.Groups["version"].Value, out version))
				{
					failure = "The Vortex conflict version range uses syntax outside the characterized single-comparator OR subset.";
					return false;
				}
				clauses.Add(new ComparatorClause(parsed.Groups["op"].Value, version));
			}
			match = new CollectionVortexVersionMatch(expression, matchExpression, false, true, prefer, clauses);
			return true;
		}

		/// <summary>Evaluates one installed/member version using the characterized Vortex subset.</summary>
		public CollectionVortexVersionMatchResult Evaluate(string candidateVersion)
		{
			if (IsAny || String.IsNullOrWhiteSpace(candidateVersion))
				return CollectionVortexVersionMatchResult.Match; // Vortex skips its version test when the installed version is absent.
			if (!IsRange)
			{
				if (StringComparer.Ordinal.Equals(MatchExpression, candidateVersion))
					return CollectionVortexVersionMatchResult.Match;
				NumericVersion expected;
				NumericVersion actual;
				if (NumericVersion.TryParse(MatchExpression, out expected) && NumericVersion.TryParse(candidateVersion, out actual))
					return expected.CompareTo(actual) == 0 ? CollectionVortexVersionMatchResult.Match : CollectionVortexVersionMatchResult.NoMatch;
				return CollectionVortexVersionMatchResult.NoMatch;
			}

			NumericVersion candidate;
			if (!NumericVersion.TryParse(candidateVersion, out candidate))
				return CollectionVortexVersionMatchResult.Unknown;
			return _clauses.Any(x => x.Matches(candidate))
				? CollectionVortexVersionMatchResult.Match
				: CollectionVortexVersionMatchResult.NoMatch;
		}

		public bool Equals(CollectionVortexVersionMatch other)
		{
			return other != null && StringComparer.Ordinal.Equals(Expression, other.Expression);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionVortexVersionMatch); }
		public override int GetHashCode() { return StringComparer.Ordinal.GetHashCode(Expression); }

		private sealed class ComparatorClause
		{
			public ComparatorClause(string operation, NumericVersion version) { Operation = operation; Version = version; }
			public string Operation { get; }
			public NumericVersion Version { get; }
			public bool Matches(NumericVersion candidate)
			{
				int comparison = candidate.CompareTo(Version);
				switch (Operation)
				{
					case "<": return comparison < 0;
					case "<=": return comparison <= 0;
					case ">": return comparison > 0;
					case ">=": return comparison >= 0;
					case "=": return comparison == 0;
					default: throw new InvalidOperationException("Unknown characterized Vortex comparator.");
				}
			}
		}

		private struct NumericVersion : IComparable<NumericVersion>
		{
			public NumericVersion(long major, long minor, long patch) { Major = major; Minor = minor; Patch = patch; }
			public long Major { get; }
			public long Minor { get; }
			public long Patch { get; }
			public int CompareTo(NumericVersion other)
			{
				int result = Major.CompareTo(other.Major);
				if (result != 0) return result;
				result = Minor.CompareTo(other.Minor);
				return result != 0 ? result : Patch.CompareTo(other.Patch);
			}
			public static bool TryParse(string value, out NumericVersion version)
			{
				version = default(NumericVersion);
				if (String.IsNullOrWhiteSpace(value)) return false;
				string normalized = value.Trim();
				if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(1);
				string[] parts = normalized.Split('.');
				if (parts.Length < 1 || parts.Length > 3) return false;
				long[] values = new long[3];
				for (int i = 0; i < parts.Length; i++)
					if (!Int64.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) return false;
				version = new NumericVersion(values[0], values[1], values[2]);
				return true;
			}
		}
	}

	/// <summary>Portable target reference retained for one characterized Vortex conflicts rule.</summary>
	public sealed class CollectionConflictReference : IEquatable<CollectionConflictReference>
	{
		public CollectionConflictReference(string fileMd5, string logicalFileName, string fileExpression,
			string gameId, string tag, string repository, string repositoryGameId, long? repositoryModId,
			long? repositoryFileId, CollectionVortexVersionMatch versionMatch)
		{
			FileMd5 = fileMd5;
			LogicalFileName = logicalFileName;
			FileExpression = fileExpression;
			GameId = gameId;
			Tag = tag;
			Repository = repository;
			RepositoryGameId = repositoryGameId;
			RepositoryModId = repositoryModId;
			RepositoryFileId = repositoryFileId;
			VersionMatch = versionMatch ?? throw new ArgumentNullException(nameof(versionMatch));
			if (String.IsNullOrWhiteSpace(FileMd5) && String.IsNullOrWhiteSpace(LogicalFileName) &&
				String.IsNullOrWhiteSpace(FileExpression) && String.IsNullOrWhiteSpace(Tag) && String.IsNullOrWhiteSpace(Repository))
				throw new ArgumentException("A Collection conflict reference requires at least one portable identity marker.");
		}

		public string FileMd5 { get; }
		public string LogicalFileName { get; }
		public string FileExpression { get; }
		public string GameId { get; }
		public string Tag { get; }
		public string Repository { get; }
		public string RepositoryGameId { get; }
		public long? RepositoryModId { get; }
		public long? RepositoryFileId { get; }
		public CollectionVortexVersionMatch VersionMatch { get; }

		public bool Equals(CollectionConflictReference other)
		{
			return other != null && StringComparer.Ordinal.Equals(FileMd5, other.FileMd5) &&
				StringComparer.Ordinal.Equals(LogicalFileName, other.LogicalFileName) &&
				StringComparer.Ordinal.Equals(FileExpression, other.FileExpression) &&
				StringComparer.Ordinal.Equals(GameId, other.GameId) && StringComparer.Ordinal.Equals(Tag, other.Tag) &&
				StringComparer.OrdinalIgnoreCase.Equals(Repository, other.Repository) &&
				StringComparer.Ordinal.Equals(RepositoryGameId, other.RepositoryGameId) &&
				RepositoryModId == other.RepositoryModId && RepositoryFileId == other.RepositoryFileId &&
				VersionMatch.Equals(other.VersionMatch);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionConflictReference); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = VersionMatch.GetHashCode();
				hash = (hash * 397) ^ (FileMd5 == null ? 0 : StringComparer.Ordinal.GetHashCode(FileMd5));
				hash = (hash * 397) ^ (LogicalFileName == null ? 0 : StringComparer.Ordinal.GetHashCode(LogicalFileName));
				hash = (hash * 397) ^ (FileExpression == null ? 0 : StringComparer.Ordinal.GetHashCode(FileExpression));
				hash = (hash * 397) ^ (RepositoryModId.HasValue ? RepositoryModId.Value.GetHashCode() : 0);
				return hash;
			}
		}
	}

	/// <summary>
	/// One Vortex conflicts rule whose source is a stable Collection member and whose target reference can be evaluated read-only.
	/// </summary>
	public sealed class CollectionConflictConstraint : IEquatable<CollectionConflictConstraint>
	{
		private readonly ReadOnlyCollection<CollectionMemberKey> _matchingMemberKeys;
		public CollectionConflictConstraint(CollectionMemberKey sourceMemberKey, CollectionConflictReference reference,
			IEnumerable<CollectionMemberKey> matchingMemberKeys)
		{
			SourceMemberKey = sourceMemberKey ?? throw new ArgumentNullException(nameof(sourceMemberKey));
			Reference = reference ?? throw new ArgumentNullException(nameof(reference));
			if (matchingMemberKeys == null) throw new ArgumentNullException(nameof(matchingMemberKeys));
			List<CollectionMemberKey> keys = matchingMemberKeys.ToList();
			if (keys.Any(x => x == null) || keys.Distinct().Count() != keys.Count)
				throw new ArgumentException("Conflict target member matches must be unique stable member keys.", nameof(matchingMemberKeys));
			_matchingMemberKeys = new ReadOnlyCollection<CollectionMemberKey>(keys);
		}

		public CollectionMemberKey SourceMemberKey { get; }
		public CollectionConflictReference Reference { get; }
		public ReadOnlyCollection<CollectionMemberKey> MatchingMemberKeys { get { return _matchingMemberKeys; } }

		public bool Equals(CollectionConflictConstraint other)
		{
			return other != null && SourceMemberKey.Equals(other.SourceMemberKey) && Reference.Equals(other.Reference) &&
				_matchingMemberKeys.SequenceEqual(other._matchingMemberKeys);
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionConflictConstraint); }
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = (SourceMemberKey.GetHashCode() * 397) ^ Reference.GetHashCode();
				foreach (CollectionMemberKey key in _matchingMemberKeys) hash = (hash * 397) ^ key.GetHashCode();
				return hash;
			}
		}
	}
}
