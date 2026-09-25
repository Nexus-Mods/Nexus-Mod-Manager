using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Classifies a limitation observed while capturing Local Collection non-file native effects.
	/// </summary>
	public enum CollectionNativeEffectIssueKind
	{
		AmbiguousIniIdentity = 1,
		EmptyIniOwnerHistory = 2,
		UnresolvedIniOwner = 3,
		DuplicateGameValueIdentity = 4,
		EmptyGameValueOwnerHistory = 5,
		UnresolvedGameValueOwner = 6,
		PluginStateIncomplete = 7,
		DuplicatePluginIdentity = 8,
		AmbiguousPluginOrder = 9
	}

	/// <summary>
	/// Distinguishes an original-value owner from a captured native mod snapshot reference.
	/// </summary>
	public enum CollectionNativeEffectOwnerKind
	{
		Unresolved = 0,
		OriginalValue = 1,
		NativeMod = 2
	}

	/// <summary>
	/// Records one non-fatal C7.5 non-file-effect capture limitation.
	/// </summary>
	public sealed class CollectionNativeEffectIssue
	{
		/// <summary>Creates one immutable effect-capture issue.</summary>
		public CollectionNativeEffectIssue(CollectionNativeEffectIssueKind kind, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionNativeEffectIssueKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionNativeEffectIssueKind Kind { get; }
		public string ResourceKey { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Captures one ordered native INI owner/value entry.
	/// </summary>
	public sealed class CollectionCapturedIniOwnerValue
	{
		/// <summary>Creates one immutable INI owner/value observation.</summary>
		public CollectionCapturedIniOwnerValue(string ownerKey, CollectionNativeEffectOwnerKind ownerKind,
			bool currentWinner, string value)
		{
			if (String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A native effect owner key is required.", nameof(ownerKey));
			if (!Enum.IsDefined(typeof(CollectionNativeEffectOwnerKind), ownerKind))
				throw new ArgumentOutOfRangeException(nameof(ownerKind));
			OwnerKey = ownerKey;
			OwnerKind = ownerKind;
			CurrentWinner = currentWinner;
			Value = value;
		}

		public string OwnerKey { get; }
		public CollectionNativeEffectOwnerKind OwnerKind { get; }
		public bool CurrentWinner { get; }
		public string Value { get; }
	}

	/// <summary>
	/// Captures one exact committed INI setting and its ordered fallback-to-winner native value history.
	/// </summary>
	public sealed class CollectionCapturedIniEffect
	{
		private readonly ReadOnlyCollection<CollectionCapturedIniOwnerValue> _values;

		/// <summary>Creates one immutable INI effect observation.</summary>
		public CollectionCapturedIniEffect(string file, string section, string key,
			IEnumerable<CollectionCapturedIniOwnerValue> values)
		{
			File = file ?? String.Empty;
			Section = section ?? String.Empty;
			Key = key ?? String.Empty;
			List<CollectionCapturedIniOwnerValue> copied = (values ?? throw new ArgumentNullException(nameof(values))).ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("An INI owner history cannot contain null records.", nameof(values));
			_values = new ReadOnlyCollection<CollectionCapturedIniOwnerValue>(copied);
		}

		public string File { get; }
		public string Section { get; }
		public string Key { get; }
		public ReadOnlyCollection<CollectionCapturedIniOwnerValue> Values { get { return _values; } }
		public CollectionCapturedIniOwnerValue CurrentValue { get { return _values.Count == 0 ? null : _values[_values.Count - 1]; } }
		public string ResourceKey { get { return File + "|" + Section + "|" + Key; } }
	}

	/// <summary>
	/// Captures one ordered native game-specific binary owner/value entry.
	/// </summary>
	public sealed class CollectionCapturedGameValueOwnerValue
	{
		private readonly byte[] _value;

		/// <summary>Creates one immutable game-specific owner/value observation.</summary>
		public CollectionCapturedGameValueOwnerValue(string ownerKey, CollectionNativeEffectOwnerKind ownerKind,
			bool currentWinner, byte[] value)
		{
			if (String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A native effect owner key is required.", nameof(ownerKey));
			if (!Enum.IsDefined(typeof(CollectionNativeEffectOwnerKind), ownerKind))
				throw new ArgumentOutOfRangeException(nameof(ownerKind));
			OwnerKey = ownerKey;
			OwnerKind = ownerKind;
			CurrentWinner = currentWinner;
			_value = value == null ? null : (byte[])value.Clone();
		}

		public string OwnerKey { get; }
		public CollectionNativeEffectOwnerKind OwnerKind { get; }
		public bool CurrentWinner { get; }
		public byte[] Value { get { return _value == null ? null : (byte[])_value.Clone(); } }
	}

	/// <summary>
	/// Captures one exact committed game-specific value and its ordered fallback-to-winner native value history.
	/// </summary>
	public sealed class CollectionCapturedGameValueEffect
	{
		private readonly ReadOnlyCollection<CollectionCapturedGameValueOwnerValue> _values;

		/// <summary>Creates one immutable game-specific effect observation.</summary>
		public CollectionCapturedGameValueEffect(string key, IEnumerable<CollectionCapturedGameValueOwnerValue> values)
		{
			if (String.IsNullOrWhiteSpace(key))
				throw new ArgumentException("A game-specific effect key is required.", nameof(key));
			Key = key;
			List<CollectionCapturedGameValueOwnerValue> copied = (values ?? throw new ArgumentNullException(nameof(values))).ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A game-specific owner history cannot contain null records.", nameof(values));
			_values = new ReadOnlyCollection<CollectionCapturedGameValueOwnerValue>(copied);
		}

		public string Key { get; }
		public ReadOnlyCollection<CollectionCapturedGameValueOwnerValue> Values { get { return _values; } }
		public CollectionCapturedGameValueOwnerValue CurrentValue { get { return _values.Count == 0 ? null : _values[_values.Count - 1]; } }
	}

	/// <summary>
	/// Immutable target-scoped C7.5 capture of supported non-file native effects.
	/// </summary>
	/// <remarks>
	/// INI/game-value owner keys are snapshot references only. Restore must remap native mod registrations instead of treating
	/// those keys as permanent identities. Plugin records are detached C7.1 native observations and contain no live manager state.
	/// </remarks>
	public sealed class CollectionNativeEffectSnapshot
	{
		/// <summary>Creates one immutable non-file-effect capture.</summary>
		public CollectionNativeEffectSnapshot(CollectionTargetIdentity target, long deploymentCommitSequence,
			IEnumerable<CollectionCapturedIniEffect> iniEdits, NativeStateCaptureCoverage iniCoverage,
			IEnumerable<CollectionCapturedGameValueEffect> gameValues, NativeStateCaptureCoverage gameValueCoverage,
			IEnumerable<NativeStateCapturePlugin> plugins, NativeStateCaptureCoverage pluginCoverage,
			IEnumerable<CollectionNativeEffectIssue> issues)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), iniCoverage))
				throw new ArgumentOutOfRangeException(nameof(iniCoverage));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), gameValueCoverage))
				throw new ArgumentOutOfRangeException(nameof(gameValueCoverage));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), pluginCoverage))
				throw new ArgumentOutOfRangeException(nameof(pluginCoverage));
			DeploymentCommitSequence = deploymentCommitSequence;
			IniEdits = Copy(iniEdits, nameof(iniEdits));
			GameValues = Copy(gameValues, nameof(gameValues));
			Plugins = Copy(plugins, nameof(plugins));
			IniCoverage = iniCoverage;
			GameValueCoverage = gameValueCoverage;
			PluginCoverage = pluginCoverage;
			Coverage = AggregateCoverage(iniCoverage, gameValueCoverage, pluginCoverage);
			Issues = Copy(issues, nameof(issues));
		}

		public CollectionTargetIdentity Target { get; }
		/// <summary>Gets the observed native deployment checkpoint for diagnostics; it is not a universal mutation generation.</summary>
		public long DeploymentCommitSequence { get; }
		public ReadOnlyCollection<CollectionCapturedIniEffect> IniEdits { get; }
		public ReadOnlyCollection<CollectionCapturedGameValueEffect> GameValues { get; }
		public ReadOnlyCollection<NativeStateCapturePlugin> Plugins { get; }
		public NativeStateCaptureCoverage IniCoverage { get; }
		public NativeStateCaptureCoverage GameValueCoverage { get; }
		public NativeStateCaptureCoverage PluginCoverage { get; }
		/// <summary>Gets aggregate completeness for convenience; C7.7 still decides the final Local Collection promise.</summary>
		public NativeStateCaptureCoverage Coverage { get; }
		public ReadOnlyCollection<CollectionNativeEffectIssue> Issues { get; }

		private static NativeStateCaptureCoverage AggregateCoverage(params NativeStateCaptureCoverage[] coverages)
		{
			if (coverages.Any(x => x == NativeStateCaptureCoverage.Partial))
				return NativeStateCaptureCoverage.Partial;
			int applicableCount = coverages.Count(x => x != NativeStateCaptureCoverage.NotApplicable);
			if (applicableCount == 0)
				return NativeStateCaptureCoverage.NotApplicable;
			int unavailableCount = coverages.Count(x => x == NativeStateCaptureCoverage.Unavailable);
			if (unavailableCount == applicableCount)
				return NativeStateCaptureCoverage.Unavailable;
			if (unavailableCount > 0)
				return NativeStateCaptureCoverage.Partial;
			return NativeStateCaptureCoverage.Complete;
		}

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A native-effect snapshot cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
