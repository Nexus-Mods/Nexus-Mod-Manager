using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Captures the supported INI, game-specific and plugin effects needed by a later Local Collection restore plan.
	/// </summary>
	/// <remarks>
	/// The reader is strictly read-only. It preserves native owner history and final plugin state, reports ambiguity instead of
	/// inventing restore semantics, and leaves all mutation/restoration policy to later C7 stages.
	/// </remarks>
	public sealed class CollectionNativeEffectCaptureReader
	{
		private readonly NativeStateCaptureReader _nativeStateReader;

		/// <summary>Creates a C7.5 non-file-effect reader over the generic C7.1 native capture boundary.</summary>
		public CollectionNativeEffectCaptureReader(NativeStateCaptureReader nativeStateReader)
		{
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
		}

		/// <summary>Captures current committed supported non-file native effects for one Local Collection target.</summary>
		public CollectionNativeEffectSnapshot Capture(CollectionTargetIdentity target)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			return Capture(target, _nativeStateReader.Capture());
		}

		internal CollectionNativeEffectSnapshot Capture(CollectionTargetIdentity target, NativeStateCaptureSnapshot nativeState)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));

			InstallLogReadSnapshot install = nativeState.InstallLog;
			var activeModKeys = new HashSet<string>(install.Mods.Where(x => !x.Hidden).Select(x => x.ModKey),
				StringComparer.OrdinalIgnoreCase);
			var issues = new List<CollectionNativeEffectIssue>();

			NativeStateCaptureCoverage iniCoverage;
			List<CollectionCapturedIniEffect> iniEdits = CaptureIniEdits(install, activeModKeys, issues, out iniCoverage);
			NativeStateCaptureCoverage gameValueCoverage;
			List<CollectionCapturedGameValueEffect> gameValues = CaptureGameValues(install, activeModKeys, issues,
				out gameValueCoverage);
			NativeStateCaptureCoverage pluginCoverage;
			List<NativeStateCapturePlugin> plugins = CapturePlugins(nativeState, issues, out pluginCoverage);

			return new CollectionNativeEffectSnapshot(target, install.DeploymentCommitSequence, iniEdits, iniCoverage,
				gameValues, gameValueCoverage, plugins, pluginCoverage, issues);
		}

		private static List<CollectionCapturedIniEffect> CaptureIniEdits(InstallLogReadSnapshot install,
			ISet<string> activeModKeys, List<CollectionNativeEffectIssue> issues, out NativeStateCaptureCoverage coverage)
		{
			List<InstallLogReadIniEdit> source = install.IniEdits.ToList();
			if (source.Count == 0)
			{
				coverage = NativeStateCaptureCoverage.NotApplicable;
				return new List<CollectionCapturedIniEffect>();
			}

			coverage = NativeStateCaptureCoverage.Complete;
			foreach (IGrouping<CollectionNativeIniKey, InstallLogReadIniEdit> aliasGroup in source
				.GroupBy(x => new CollectionNativeIniKey(x.File, x.Section, x.Key)).Where(x => x.Count() > 1))
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new CollectionNativeEffectIssue(CollectionNativeEffectIssueKind.AmbiguousIniIdentity,
					aliasGroup.Key.ToString(), "InstallLog contains multiple case-alias records for one logical INI setting."));
			}

			var result = new List<CollectionCapturedIniEffect>(source.Count);
			foreach (InstallLogReadIniEdit ini in source.OrderBy(x => x.File, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.Section, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.File, StringComparer.Ordinal).ThenBy(x => x.Section, StringComparer.Ordinal)
				.ThenBy(x => x.Key, StringComparer.Ordinal))
			{
				string resourceKey = (ini.File ?? String.Empty) + "|" + (ini.Section ?? String.Empty) + "|" + (ini.Key ?? String.Empty);
				if (ini.Values.Count == 0)
				{
					coverage = NativeStateCaptureCoverage.Partial;
					issues.Add(new CollectionNativeEffectIssue(CollectionNativeEffectIssueKind.EmptyIniOwnerHistory,
						resourceKey, "A committed INI record has no native owner/value history to restore."));
				}

				var values = new List<CollectionCapturedIniOwnerValue>(ini.Values.Count);
				for (int index = 0; index < ini.Values.Count; index++)
				{
					InstallLogReadStringValue value = ini.Values[index];
					CollectionNativeEffectOwnerKind kind = ResolveOwnerKind(install.OriginalValuesKey, activeModKeys,
						value.OwnerKey, CollectionNativeEffectIssueKind.UnresolvedIniOwner, resourceKey, issues, ref coverage);
					values.Add(new CollectionCapturedIniOwnerValue(value.OwnerKey, kind, index == ini.Values.Count - 1, value.Value));
				}
				result.Add(new CollectionCapturedIniEffect(ini.File, ini.Section, ini.Key, values));
			}
			return result;
		}

		private static List<CollectionCapturedGameValueEffect> CaptureGameValues(InstallLogReadSnapshot install,
			ISet<string> activeModKeys, List<CollectionNativeEffectIssue> issues, out NativeStateCaptureCoverage coverage)
		{
			List<InstallLogReadGameValue> source = install.GameValues.ToList();
			if (source.Count == 0)
			{
				coverage = NativeStateCaptureCoverage.NotApplicable;
				return new List<CollectionCapturedGameValueEffect>();
			}

			coverage = NativeStateCaptureCoverage.Complete;
			foreach (IGrouping<string, InstallLogReadGameValue> duplicate in source
				.GroupBy(x => x.Key, StringComparer.Ordinal).Where(x => x.Count() > 1))
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new CollectionNativeEffectIssue(CollectionNativeEffectIssueKind.DuplicateGameValueIdentity,
					duplicate.Key, "InstallLog contains duplicate records for one game-specific value key."));
			}

			var result = new List<CollectionCapturedGameValueEffect>(source.Count);
			foreach (InstallLogReadGameValue gameValue in source.OrderBy(x => x.Key, StringComparer.Ordinal))
			{
				if (gameValue.Values.Count == 0)
				{
					coverage = NativeStateCaptureCoverage.Partial;
					issues.Add(new CollectionNativeEffectIssue(CollectionNativeEffectIssueKind.EmptyGameValueOwnerHistory,
						gameValue.Key, "A committed game-specific value has no native owner/value history to restore."));
				}

				var values = new List<CollectionCapturedGameValueOwnerValue>(gameValue.Values.Count);
				for (int index = 0; index < gameValue.Values.Count; index++)
				{
					InstallLogReadBinaryValue value = gameValue.Values[index];
					CollectionNativeEffectOwnerKind kind = ResolveOwnerKind(install.OriginalValuesKey, activeModKeys,
						value.OwnerKey, CollectionNativeEffectIssueKind.UnresolvedGameValueOwner, gameValue.Key,
						issues, ref coverage);
					values.Add(new CollectionCapturedGameValueOwnerValue(value.OwnerKey, kind,
						index == gameValue.Values.Count - 1, value.Value));
				}
				result.Add(new CollectionCapturedGameValueEffect(gameValue.Key, values));
			}
			return result;
		}

		private static List<NativeStateCapturePlugin> CapturePlugins(NativeStateCaptureSnapshot nativeState,
			List<CollectionNativeEffectIssue> issues, out NativeStateCaptureCoverage coverage)
		{
			coverage = nativeState.PluginCoverage;
			List<NativeStateCapturePlugin> plugins = nativeState.Plugins
				.OrderBy(x => x.Priority).ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
				.ThenBy(x => x.FileName, StringComparer.Ordinal).ToList();

			if (coverage == NativeStateCaptureCoverage.Unavailable || coverage == NativeStateCaptureCoverage.Partial)
			{
				NativeStateCaptureIssue nativeIssue = nativeState.Issues.FirstOrDefault(x =>
					x.Kind == NativeStateCaptureIssueKind.PluginStateUnavailable);
				issues.Add(new CollectionNativeEffectIssue(CollectionNativeEffectIssueKind.PluginStateIncomplete,
					"plugins", nativeIssue == null ? "The authoritative native plugin state is incomplete." : nativeIssue.Message));
			}

			if (coverage == NativeStateCaptureCoverage.NotApplicable)
				return plugins;

			foreach (IGrouping<string, NativeStateCapturePlugin> duplicate in plugins
				.GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new CollectionNativeEffectIssue(CollectionNativeEffectIssueKind.DuplicatePluginIdentity,
					duplicate.Key, "The native plugin snapshot contains multiple records for the same plugin identity."));
			}

			foreach (IGrouping<int, NativeStateCapturePlugin> duplicatePriority in plugins
				.GroupBy(x => x.Priority).Where(x => x.Count() > 1))
			{
				coverage = NativeStateCaptureCoverage.Partial;
				issues.Add(new CollectionNativeEffectIssue(CollectionNativeEffectIssueKind.AmbiguousPluginOrder,
					duplicatePriority.Key.ToString(), "The native plugin snapshot contains more than one plugin at the same final priority."));
			}
			return plugins;
		}

		private static CollectionNativeEffectOwnerKind ResolveOwnerKind(string originalValuesKey, ISet<string> activeModKeys,
			string ownerKey, CollectionNativeEffectIssueKind unresolvedIssueKind, string resourceKey,
			List<CollectionNativeEffectIssue> issues, ref NativeStateCaptureCoverage coverage)
		{
			if (!String.IsNullOrWhiteSpace(originalValuesKey) && String.Equals(ownerKey, originalValuesKey,
				StringComparison.OrdinalIgnoreCase))
				return CollectionNativeEffectOwnerKind.OriginalValue;
			if (activeModKeys.Contains(ownerKey))
				return CollectionNativeEffectOwnerKind.NativeMod;

			coverage = NativeStateCaptureCoverage.Partial;
			issues.Add(new CollectionNativeEffectIssue(unresolvedIssueKind, resourceKey,
				"A recorded native effect owner does not identify the original value or an active captured native mod."));
			return CollectionNativeEffectOwnerKind.Unresolved;
		}
	}
}
