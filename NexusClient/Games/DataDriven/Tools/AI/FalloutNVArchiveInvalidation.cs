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
	/// Implements Fallout: New Vegas Archive Invalidation for a
	/// data-driven Gamebryo game mode.
	/// </summary>
	public sealed class FalloutNVArchiveInvalidation
		: ArchiveInvalidationBase
	{
		private const string ArchiveInvalidationBsa =
			"Fallout - AI!.bsa";

		private const string LegacyArchiveInvalidationBsa =
			"ArchiveInvalidationInvalidated!.bsa";

		private const string DefaultIniSettingsKey =
			"FODefaultIniPath";

		/*
         * Fallout 3 and Fallout: New Vegas use the same small
         * Archive Invalidation BSA payload.
         */
		private static readonly byte[] ArchiveInvalidationBsaData =
			Convert.FromBase64String(
				"QlNBAGcAAAAkAAAAAwcAAAEAAAABAAAAAQAAAAIAAAACAAAAAAAA" +
				"AAAAAAABAAAANgAAAAEAYQABYQAAAAAAAAAASAAAAGEA");

		public FalloutNVArchiveInvalidation(
			DataDrivenGamebryoGameMode gameMode)
			: base(gameMode)
		{
		}

		private string DefaultIniPath
		{
			get
			{
				return GameMode.SettingsFiles[
					DefaultIniSettingsKey];
			}
		}

		public override bool IsActive()
		{
			string iniPath = GameMode.SettingsFiles.IniPath;

			if (!File.Exists(iniPath))
				return false;

			string archiveList =
				IniMethods.GetPrivateProfileString(
					"Archive",
					"SArchiveList",
					string.Empty,
					iniPath) ??
				string.Empty;

			string[] entries = archiveList.Split(
				new[] { ',' },
				StringSplitOptions.RemoveEmptyEntries);

			foreach (string entry in entries)
			{
				if (string.Equals(
					entry.Trim(),
					ArchiveInvalidationBsa,
					StringComparison.Ordinal))
				{
					return true;
				}
			}

			return IniMethods.GetPrivateProfileInt32(
					   "Archive",
					   "bInvalidateOlderFiles",
					   0,
					   iniPath) != 0;
		}

		protected override void ApplyAI()
		{
			try
			{
				string pluginDirectory = GameMode.PluginDirectory;

				SetArchiveTimestamps(
					pluginDirectory,
					"Fallout - *.bsa",
					new DateTime(2008, 10, 1));

				SetArchiveTimestamps(
					pluginDirectory,
					"ClassicPack - *.bsa",
					new DateTime(2008, 10, 1));

				WriteIniInt(
					"Archive",
					"bInvalidateOlderFiles",
					1);

				WriteIniInt(
					"General",
					"bLoadFaceGenHeadEGTFiles",
					1);

				WriteIniString(
					"Archive",
					"SInvalidationFile",
					string.Empty);

				File.Delete(
					Path.Combine(
						pluginDirectory,
						"archiveinvalidation.txt"));

				File.Delete(
					Path.Combine(
						pluginDirectory,
						LegacyArchiveInvalidationBsa));

				File.WriteAllBytes(
					Path.Combine(
						pluginDirectory,
						ArchiveInvalidationBsa),
					ArchiveInvalidationBsaData);

				WriteIniString(
					"Archive",
					"SArchiveList",
					BuildArchiveList(true));
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
			string pluginDirectory = GameMode.PluginDirectory;

			WriteIniInt(
				"Archive",
				"bInvalidateOlderFiles",
				0);

			WriteIniInt(
				"General",
				"bLoadFaceGenHeadEGTFiles",
				0);

			WriteIniString(
				"Archive",
				"SInvalidationFile",
				"ArchiveInvalidation.txt");

			File.Delete(
				Path.Combine(
					pluginDirectory,
					ArchiveInvalidationBsa));

			File.Delete(
				Path.Combine(
					pluginDirectory,
					LegacyArchiveInvalidationBsa));

			WriteIniString(
				"Archive",
				"SArchiveList",
				BuildArchiveList(false));
		}

		private void WriteIniInt(
			string section,
			string key,
			int value)
		{
			IniMethods.WritePrivateProfileInt32(
				section,
				key,
				value,
				GameMode.SettingsFiles.IniPath);

			string defaultIniPath = DefaultIniPath;

			if (File.Exists(defaultIniPath))
			{
				IniMethods.WritePrivateProfileInt32(
					section,
					key,
					value,
					defaultIniPath);
			}
		}

		private void WriteIniString(
			string section,
			string key,
			string value)
		{
			IniMethods.WritePrivateProfileString(
				section,
				key,
				value,
				GameMode.SettingsFiles.IniPath);

			string defaultIniPath = DefaultIniPath;

			if (File.Exists(defaultIniPath))
			{
				IniMethods.WritePrivateProfileString(
					section,
					key,
					value,
					defaultIniPath);
			}
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
					LegacyArchiveInvalidationBsa,
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

				/*
                 * Preserve the old New Vegas behavior: archives whose
                 * names contain "Misc" are moved to the beginning.
                 */
				if (archive.IndexOf(
						"Misc",
						StringComparison.Ordinal) >= 0)
				{
					result.Insert(0, archive);
				}
				else
				{
					result.Add(archive);
				}
			}

			if (includeArchiveInvalidation)
				result.Insert(0, ArchiveInvalidationBsa);

			return string.Join(", ", result.ToArray());
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
