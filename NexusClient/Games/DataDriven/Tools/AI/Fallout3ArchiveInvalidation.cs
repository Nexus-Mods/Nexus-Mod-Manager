using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Nexus.Client.Games.Gamebryo.Tools.AI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven.Tools.AI
{
	/// <summary>
	/// Implements Fallout 3 Archive Invalidation for a data-driven
	/// Gamebryo game mode.
	/// </summary>
	public sealed class Fallout3ArchiveInvalidation
		: ArchiveInvalidationBase
	{
		private const string ArchiveInvalidationBsa =
			"ArchiveInvalidationInvalidated!.bsa";

		/*
         * This is the same 72-byte BSA payload used by the legacy
         * Fallout 3 ArchiveInvalidation implementation.
         */
		private static readonly byte[] ArchiveInvalidationBsaData =
			Convert.FromBase64String(
				"QlNBAGcAAAAkAAAAAwcAAAEAAAABAAAAAQAAAAIAAAACAAAAAAAA" +
				"AAAAAAABAAAANgAAAAEAYQABYQAAAAAAAAAASAAAAGEA");

		public Fallout3ArchiveInvalidation(
			DataDrivenGamebryoGameMode gameMode)
			: base(gameMode)
		{
		}

		public override bool IsActive()
		{
			string iniPath = GameMode.SettingsFiles.IniPath;

			if (!File.Exists(iniPath))
				return false;

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
				string iniPath = GameMode.SettingsFiles.IniPath;

				SetArchiveTimestamps(
					pluginDirectory,
					"Fallout - *.bsa",
					new DateTime(2008, 10, 1));

				SetArchiveTimestamps(
					pluginDirectory,
					"Anchorage - *.bsa",
					new DateTime(2008, 10, 2));

				SetArchiveTimestamps(
					pluginDirectory,
					"ThePitt - *.bsa",
					new DateTime(2008, 10, 3));

				SetArchiveTimestamps(
					pluginDirectory,
					"BrokenSteel - *.bsa",
					new DateTime(2008, 10, 4));

				SetArchiveTimestamps(
					pluginDirectory,
					"PointLookout - *.bsa",
					new DateTime(2008, 10, 5));

				SetArchiveTimestamps(
					pluginDirectory,
					"Zeta - *.bsa",
					new DateTime(2008, 10, 6));

				IniMethods.WritePrivateProfileInt32(
					"Archive",
					"bInvalidateOlderFiles",
					1,
					iniPath);

				IniMethods.WritePrivateProfileInt32(
					"General",
					"bLoadFaceGenHeadEGTFiles",
					1,
					iniPath);

				IniMethods.WritePrivateProfileString(
					"Archive",
					"SInvalidationFile",
					string.Empty,
					iniPath);

				File.Delete(
					Path.Combine(
						pluginDirectory,
						"archiveinvalidation.txt"));

				File.WriteAllBytes(
					Path.Combine(
						pluginDirectory,
						ArchiveInvalidationBsa),
					ArchiveInvalidationBsaData);

				IniMethods.WritePrivateProfileString(
					"Archive",
					"SArchiveList",
					ArchiveInvalidationBsa + ", " +
					GetArchiveListWithoutArchiveInvalidation(),
					iniPath);
			}
			catch (Exception exception)
			{
				Trace.TraceError(
					"ApplyAI - Could not apply ArchiveInvalidation.");

				TraceUtil.TraceException(exception);

				MessageBox.Show(
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

			IniMethods.WritePrivateProfileInt32(
				"Archive",
				"bInvalidateOlderFiles",
				0,
				iniPath);

			IniMethods.WritePrivateProfileInt32(
				"General",
				"bLoadFaceGenHeadEGTFiles",
				0,
				iniPath);

			IniMethods.WritePrivateProfileString(
				"Archive",
				"SInvalidationFile",
				"ArchiveInvalidation.txt",
				iniPath);

			File.Delete(
				Path.Combine(
					GameMode.PluginDirectory,
					ArchiveInvalidationBsa));

			IniMethods.WritePrivateProfileString(
				"Archive",
				"SArchiveList",
				GetArchiveListWithoutArchiveInvalidation(),
				iniPath);
		}

		private string GetArchiveListWithoutArchiveInvalidation()
		{
			string iniPath = GameMode.SettingsFiles.IniPath;

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

			var result = new List<string>();

			foreach (string entry in entries)
			{
				string archive = entry.Trim();

				if (string.Equals(
					archive,
					ArchiveInvalidationBsa,
					StringComparison.Ordinal))
				{
					continue;
				}

				result.Add(archive);
			}

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
