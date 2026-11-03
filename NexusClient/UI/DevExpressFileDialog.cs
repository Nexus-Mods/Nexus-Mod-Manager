namespace Nexus.Client.UI
{
	using System;
	using System.Diagnostics;
	using System.IO;
	using System.Windows.Forms;
	using DevExpress.XtraEditors;
	using Nexus.Client.Settings;

	/// <summary>Retains accepted file-dialog folders in the existing user interface preference store.</summary>
	internal static class DevExpressFileDialog
	{
		private const string DirectoryKeyPrefix = "fileDialog.LastDirectory.";

		/// <summary>Shows a file dialog with folder history scoped to its action.</summary>
		internal static DialogResult ShowDialog(FileDialogBase dialog, IWin32Window owner, string purpose)
		{
			ISettings settings = Properties.Settings.Default;
			string initialDirectory = dialog.InitialDirectory;
			dialog.InitialDirectory = GetInitialDirectory(settings, purpose, initialDirectory);
			try
			{
				DialogResult result = dialog.ShowDialog(owner);
				RememberSelection(settings, purpose, dialog.FileName, result);
				return result;
			}
			finally
			{
				// A reused dialog must not treat the restored history as an explicit caller override.
				dialog.InitialDirectory = initialDirectory;
			}
		}

		/// <summary>Restores an existing folder while preserving any explicitly requested starting directory.</summary>
		internal static string GetInitialDirectory(ISettings settings, string purpose, string initialDirectory)
		{
			if (!string.IsNullOrEmpty(initialDirectory) || settings == null) return initialDirectory;
			try
			{
				lock (settings)
				{
					string directory;
					if (settings.DockPanelLayouts != null && settings.DockPanelLayouts.TryGetValue(DirectoryKeyPrefix + purpose, out directory) &&
						!string.IsNullOrWhiteSpace(directory) && Path.IsPathRooted(directory) && Directory.Exists(directory))
						return directory;
				}
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				Trace.TraceWarning("Could not restore file-dialog folder for {0}: {1}", purpose, exception.Message);
			}
			return initialDirectory;
		}

		/// <summary>Saves only accepted file selections; unavailable folders and preference failures remain nonfatal.</summary>
		internal static void RememberSelection(ISettings settings, string purpose, string fileName, DialogResult result)
		{
			if (result != DialogResult.OK || settings == null || string.IsNullOrWhiteSpace(fileName)) return;
			try
			{
				if (!Path.IsPathRooted(fileName)) return;
				string directory = Path.GetDirectoryName(Path.GetFullPath(fileName));
				if (!Directory.Exists(directory)) return;
				lock (settings)
				{
					if (settings.DockPanelLayouts == null) return;
					string key = DirectoryKeyPrefix + purpose;
					string previous;
					if (settings.DockPanelLayouts.TryGetValue(key, out previous) && string.Equals(previous, directory, StringComparison.OrdinalIgnoreCase)) return;
					settings.DockPanelLayouts[key] = directory;
					settings.Save();
				}
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				Trace.TraceWarning("Could not save file-dialog folder for {0}: {1}", purpose, exception.Message);
			}
		}
	}
}
