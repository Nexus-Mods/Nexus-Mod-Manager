using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.Games.Gamebryo.Tools.AI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven.Tools.AI
{
	/// <summary>
	/// Implements Oblivion BSA redirection for a data-driven
	/// Gamebryo game mode.
	/// </summary>
	public sealed class OblivionArchiveInvalidation
		: ArchiveInvalidationBase
	{
		private const string ArchiveInvalidationBsa =
			"BSARedirection.bsa";

		/*
         * This is the entry written by older OBMM versions into
         * Oblivion.ini. It is an INI entry, not a path supplied by JSON.
         */
		private const string LegacyArchiveListEntry =
			@"..\obmm\BSARedirection.bsa";

		private static readonly byte[] ArchiveInvalidationBsaData =
			Convert.FromBase64String(
				"QlNBAGcAAAAkAAAAAwcAAAAAAAAAAAAAAAAAAAAAAAACAAAA");

		public OblivionArchiveInvalidation(
			DataDrivenGamebryoGameMode gameMode)
			: base(gameMode)
		{
		}

		public override bool IsActive()
		{
			return File.Exists(
				Path.Combine(
					GameMode.PluginDirectory,
					ArchiveInvalidationBsa));
		}

		protected override void ApplyAI()
		{
			try
			{
				string pluginDirectory = GameMode.PluginDirectory;
				string iniPath = GameMode.SettingsFiles.IniPath;

				DateTime archiveTimestamp =
					new DateTime(2005, 10, 1);

				SetArchiveTimestamps(
					pluginDirectory,
					"Oblivion - *.bsa",
					archiveTimestamp);

				SetArchiveTimestamps(
					pluginDirectory,
					"DLC*.bsa",
					archiveTimestamp);

				SetArchiveTimestamps(
					pluginDirectory,
					"Knights.bsa",
					archiveTimestamp);

				IniMethods.WritePrivateProfileString(
					"Archive",
					"SInvalidationFile",
					string.Empty,
					iniPath);

				FileUtil.ForceDelete(
					Path.Combine(
						pluginDirectory,
						"archiveinvalidation.txt"));

				FileUtil.ForceDelete(GetLegacyObmmBsaPath());

				File.WriteAllBytes(
					Path.Combine(
						pluginDirectory,
						ArchiveInvalidationBsa),
					ArchiveInvalidationBsaData);

				IniMethods.WritePrivateProfileString(
					"Archive",
					"SArchiveList",
					BuildArchiveList(true),
					iniPath);
			}
			catch (Exception exception)
			{
				Trace.TraceError(
					"ApplyAI - Could not apply ArchiveInvalidation.");

				TraceUtil.TraceException(exception);

				XtraMessageBox.Show(
					"Could not apply Archive Invalidation, at least one " +
					"file could not be modified.\n" +
					"Please try again, or check trace log for more info." +
					"\n\n" +
					exception.Message,
					"Archive Invalidation failed",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
			}
		}

		protected override void RemoveAI()
		{
			string iniPath = GameMode.SettingsFiles.IniPath;

			IniMethods.WritePrivateProfileString(
				"Archive",
				"SInvalidationFile",
				"ArchiveInvalidation.txt",
				iniPath);

			FileUtil.ForceDelete(
				Path.Combine(
					GameMode.PluginDirectory,
					ArchiveInvalidationBsa));

			FileUtil.ForceDelete(GetLegacyObmmBsaPath());

			IniMethods.WritePrivateProfileString(
				"Archive",
				"SArchiveList",
				BuildArchiveList(false),
				iniPath);
		}

		private string BuildArchiveList(
			bool includeArchiveInvalidation)
		{
			string archiveList =
				IniMethods.GetPrivateProfileString(
					"Archive",
					"SArchiveList",
					string.Empty,
					GameMode.SettingsFiles.IniPath) ??
				string.Empty;

			string[] entries = archiveList.Split(
				new[] { ',' },
				StringSplitOptions.RemoveEmptyEntries);

			var result = new List<string>();

			foreach (string entry in entries)
			{
				string archive = entry.Trim();

				if (string.Equals(
					archive,
					LegacyArchiveListEntry,
					StringComparison.Ordinal))
				{
					continue;
				}

				if (string.Equals(
					archive,
					ArchiveInvalidationBsa,
					StringComparison.Ordinal))
				{
					continue;
				}

				result.Add(archive);
			}

			if (includeArchiveInvalidation)
				result.Insert(0, ArchiveInvalidationBsa);

			return string.Join(", ", result.ToArray());
		}

		private string GetLegacyObmmBsaPath()
		{
			return Path.Combine(
				GameMode.GameModeEnvironmentInfo.InstallationPath,
				"obmm",
				ArchiveInvalidationBsa);
		}

		private static void SetArchiveTimestamps(
			string directory,
			string searchPattern,
			DateTime timestamp)
		{
			FileInfo[] files =
				new DirectoryInfo(directory).GetFiles(searchPattern);

			foreach (FileInfo file in files)
				file.LastWriteTime = timestamp;
		}
	}
}
