using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.UI;
using SevenZip;

namespace Nexus.Client.ModAuthoring
{
	/// <summary>
	/// The delegate for callbacks that confirm a file overwrite.
	/// </summary>
	/// <remarks>
	/// The callback can provide an alternate file name.
	/// </remarks>
	/// <param name="p_strOldFilePath">The path to the file that is to be overwritten.</param>
	/// <param name="p_strNewFilePath">An out parameter specifying the file to to which to
	/// write the file.</param>
	/// <returns><c>true</c> if the file should be written;
	/// <c>false</c> otherwise.</returns>
	public delegate bool ConfirmOverwriteCallback(string p_strOldFilePath, out string p_strNewFilePath);

	/// <summary>
	/// Builds mods from various sources.
	/// </summary>
	public class ModBuilder : ThreadedBackgroundTask, IDisposable
	{
		/// <summary>
		/// The list of possible sources from which a mod can be built.
		/// </summary>
		protected enum Sources
		{
			/// <summary>
			/// An archive file.
			/// </summary>
			Archive,

			/// <summary>
			/// An EXE file.
			/// </summary>
			Exe
		}

		#region Properties

		/// <summary>
		/// Gets or sets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		protected FileUtil FileUtility { get; set; }

		/// <summary>
		/// Gets the environment info of the current game mode.
		/// </summary>
		/// <value>The environment info of the current game mode.</value>
		protected IGameModeEnvironmentInfo GameModeInfo { get; private set; }

		/// <summary>
		/// Gets or sets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple construtor that initializes the object with the reqruied dependencies.
		/// </summary>
		/// <param name="p_gmiGameModeInfo">The environment info of the current game mode.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public ModBuilder(IGameModeEnvironmentInfo p_gmiGameModeInfo, IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
		{
			GameModeInfo = p_gmiGameModeInfo;
			EnvironmentInfo = p_eifEnvironmentInfo;
			FileUtility = p_futFileUtility;
			OverallProgressMaximum = 4;
		}

		#endregion

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <remarks>
		/// This method hands off to another methods, as determined by the first parameter
		/// which indicates the type of source from which the mod is being built.
		/// </remarks>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <param name="p_strMessage">The message describing the state of the task.</param>
		/// <returns>A return value.</returns>
		protected override object DoWork(object[] p_objArgs, out string p_strMessage)
		{
			switch ((Sources)p_objArgs[0])
			{
				case Sources.Archive:
					return DoFromArchive((IModFormatRegistry)p_objArgs[1], (string)p_objArgs[2], (ConfirmOverwriteCallback)p_objArgs[3], out p_strMessage);
			}
			throw new ArgumentException("Unrecognized activity source.");
		}

		#region From File

		/// <summary>
		/// Builds mods from a file.
		/// </summary>
		/// <remarks>
		/// This detects the type of file and takes appropriate action.
		/// </remarks>
		/// <param name="p_mfrFormats">The registry of supported mod formats.</param>
		/// <param name="p_strFilePath">The archive to build into a mod.</param>
		/// <param name="p_dlgConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <exception cref="ArgumentException">Thrown if the specified path is not an archive.</exception>
		public void BuildFromFile(IModFormatRegistry p_mfrFormats, string p_strFilePath, ConfirmOverwriteCallback p_dlgConfirmOverwrite)
		{
			ShowItemProgress = true;
			OverallProgressStepSize = 1;
			ItemProgressStepSize = 1;
			OverallProgressMaximum = 4;
			OverallMessage = "Building Mod...";
			Sources srcModSource = Sources.Archive;
			if (String.IsNullOrEmpty(p_strFilePath) || !File.Exists(p_strFilePath))
				throw new ArgumentException("The given file path does not exist: " + p_strFilePath);
			if (!Archive.IsArchive(p_strFilePath))
			{
				Status = TaskStatus.Error;
				OnTaskEnded(String.Format("Cannot add {0}. File format is not recognized.", Path.GetFileName(p_strFilePath)), null);
				return;
			}

			Start(srcModSource, p_mfrFormats, p_strFilePath, p_dlgConfirmOverwrite);
		}

		#region From Archive

		/// <summary>
		/// Builds mods from an archive.
		/// </summary>
		/// <remarks>
		/// If the specified archive contains mods, they are simply extracted. Otherwise, the archive
		/// is examined to determine if it is already in a recognized format. If not, or if the archive
		/// spans multiple volumes, then the archive is repackaged.
		/// </remarks>
		/// <param name="p_mfrFormats">The registry of supported mod formats.</param>
		/// <param name="p_strArchivePath">The archive to build into a mod.</param>
		/// <param name="p_dlgConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <param name="p_strMessage">The message describing the state of the task.</param>
		/// <returns>The paths to the new mods.</returns>
		/// <exception cref="ArgumentException">Thrown if the specified path is not an archive.</exception>
		private IList<string> DoFromArchive(IModFormatRegistry p_mfrFormats, string p_strArchivePath, ConfirmOverwriteCallback p_dlgConfirmOverwrite, out string p_strMessage)
		{
			p_strMessage = null;
			Trace.TraceInformation(String.Format("[{0}] Adding mod from archive.", p_strArchivePath));
			if (String.IsNullOrEmpty(p_strArchivePath) || !File.Exists(p_strArchivePath) || !Archive.IsArchive(p_strArchivePath))
				throw new ArgumentException("The specified path is not an archive file.", "p_strArchivePath");

			List<string> lstFoundMods = new List<string>();
			List<string> lstModsInArchive = new List<string>();

			ItemMessage = "Examining archive...";
			ItemProgress = 0;
			ItemProgressMaximum = p_mfrFormats.Formats.Count;
			IModFormat mftDestFormat = null;

			try
			{
				using (SevenZipExtractor szeExtractor = Archive.GetExtractor(p_strArchivePath))
				{
					if (Status == TaskStatus.Cancelling)
						return lstFoundMods;
					ReadOnlyCollection<string> lstArchiveFiles = szeExtractor.ArchiveFileNames;
					foreach (IModFormat mftFormat in p_mfrFormats.Formats)
					{
						ItemMessage = String.Format("Examining archive for {0} mods...", mftFormat.Name);
						lstModsInArchive.AddRange(lstArchiveFiles.Where(x => mftFormat.Extension.Equals(Path.GetExtension(x), StringComparison.OrdinalIgnoreCase)));
						StepItemProgress();
						if (Status == TaskStatus.Cancelling)
							return lstFoundMods;
					}
					StepOverallProgress();
				}

				if (lstModsInArchive.Count == 0)
				{
					ItemMessage = "Determining archive format...";
					ItemProgress = 0;
					ItemProgressMaximum = p_mfrFormats.Formats.Count;
					List<KeyValuePair<FormatConfidence, IModFormat>> lstFormats = new List<KeyValuePair<FormatConfidence, IModFormat>>();
					foreach (IModFormat mftFormat in p_mfrFormats.Formats)
					{
						lstFormats.Add(new KeyValuePair<FormatConfidence, IModFormat>(mftFormat.CheckFormatCompliance(p_strArchivePath), mftFormat));
						StepItemProgress();
						if (Status == TaskStatus.Cancelling)
							return lstFoundMods;
					}
					lstFormats.Sort((x, y) => y.Key.CompareTo(x.Key));
					if ((lstFormats.Count == 0) || (lstFormats[0].Key <= FormatConfidence.Convertible))
						return lstFoundMods;
					mftDestFormat = lstFormats[0].Value;
				}
				StepOverallProgress();
			}
			catch (Exception ex)
			{
				MessageBox.Show("An error has occured with the following archive: " + p_strArchivePath + "\n\n ERROR: " + ex.Message);
				return lstFoundMods;
			}
			string strTmpPath = null;
			try
			{
				using (SevenZipExtractor szeExtractor = Archive.GetExtractor(p_strArchivePath))
				{
					if ((mftDestFormat != null) && (szeExtractor.VolumeFileNames.Count > 1) ||
						(lstModsInArchive.Count > 0))
					{
						ItemMessage = "Extracting archive...";
						ItemProgress = 0;
						ItemProgressMaximum = szeExtractor.ArchiveFileNames.Count;
						strTmpPath = FileUtility.CreateTempDirectory();
						szeExtractor.FileExtractionStarted += new EventHandler<FileInfoEventArgs>(Extractor_FileExtractionStarted);
						szeExtractor.FileExtractionFinished += new EventHandler<FileInfoEventArgs>(Extractor_FileExtractionFinished);
						try
						{
							szeExtractor.ExtractArchive(strTmpPath);
						}
						catch (FileNotFoundException ex)
						{
							Status = TaskStatus.Error;
							p_strMessage = ex.Message;
							return lstFoundMods;
						}
						for (Int32 i = 0; i < lstModsInArchive.Count; i++)
							lstModsInArchive[i] = Path.Combine(strTmpPath, lstModsInArchive[i]);
					}
					else
						lstModsInArchive.Add(p_strArchivePath);
				}
				StepOverallProgress();

				if (!String.IsNullOrEmpty(strTmpPath) && (mftDestFormat != null))
				{
					//if we have extracted the file to do format shifting
					if (!mftDestFormat.SupportsModCompression)
						return lstFoundMods;
					ItemMessage = "Compressing mod...";
					ItemProgress = 0;
					ItemProgressMaximum = Directory.GetFiles(strTmpPath, "*", SearchOption.AllDirectories).Length;
					IModCompressor mcpCompressor = mftDestFormat.GetModCompressor(EnvironmentInfo);
					mcpCompressor.FileCompressionFinished += new CancelEventHandler(Compressor_FileCompressionFinished);
					string strDest = Path.Combine(GameModeInfo.ModDirectory, Path.GetFileName(p_strArchivePath));
					strDest = Path.ChangeExtension(strDest, mftDestFormat.Extension);
					strDest = ConfirmOverwrite(p_dlgConfirmOverwrite, strDest);
					if (!String.IsNullOrEmpty(strDest))
					{
						mcpCompressor.Compress(strTmpPath, strDest);
						lstFoundMods.Add(strDest);
					}
				}
				else
				{
					ItemMessage = "Copying mods...";
					ItemProgress = 0;
					ItemProgressMaximum = lstModsInArchive.Count;
					foreach (string strMod in lstModsInArchive)
					{
						if (Status == TaskStatus.Cancelling)
							return lstFoundMods;
						ItemMessage = String.Format("Copying mod {0}...", Path.GetFileName(strMod));
						string strDest = Path.Combine(GameModeInfo.ModDirectory, Path.GetFileName(strMod));
						strDest = ConfirmOverwrite(p_dlgConfirmOverwrite, strDest);
						if (!String.IsNullOrEmpty(strDest))
						{
                            // Make sure we aren't copying mod to itself.
                            if (!string.Equals(strMod, strDest, StringComparison.OrdinalIgnoreCase))
							    File.Copy(strMod, strDest, true);
							lstFoundMods.Add(strDest);
						}
						StepItemProgress();
					}
				}
				StepOverallProgress();
			}
			catch (FileNotFoundException ex)
			{
				MessageBox.Show("An error has occured with the following archive: " + p_strArchivePath + "\n\n ERROR: " + ex.Message);
				return lstFoundMods;
			}
			finally
			{
				if (!String.IsNullOrEmpty(strTmpPath))
					FileUtil.ForceDelete(strTmpPath);
			}
			return lstFoundMods;
		}

		#endregion

		/// <summary>
		/// A wrapper method for calls to <see cref="ConfirmOverwriteCallback"/> delegates.
		/// </summary>
		/// <remarks>
		/// This wrapper encapsulates delaing with the different return values the delegate can produce.
		/// </remarks>
		/// <param name="p_dlgConfirmOverwrite">The <see cref="ConfirmOverwriteCallback"/> delegate to call.</param>
		/// <param name="p_strDestinationPath">The path to use as a parameter for the call.</param>
		/// <returns>The new filename to use for the overwrite, or <c>null</c> if the overwrite
		/// should not be done.</returns>
		private string ConfirmOverwrite(ConfirmOverwriteCallback p_dlgConfirmOverwrite, string p_strDestinationPath)
		{
			string strDest = p_strDestinationPath;
			if (p_dlgConfirmOverwrite(strDest, out strDest))
				return strDest;
			return null;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="SevenZipExtractor.FileExtractionFinished"/> event of
		/// the archive extractors.
		/// </summary>
		/// <remarks>
		/// This cancels the extraction if the user has cancelled the task. This also updates
		/// the item progress.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FileInfoEventArgs"/> describing the event arguments.</param>
		private void Extractor_FileExtractionFinished(object sender, FileInfoEventArgs e)
		{
			e.Cancel = Status == TaskStatus.Cancelling;
			StepItemProgress();
		}

		/// <summary>
		/// Handles the <see cref="SevenZipExtractor.FileExtractionStarted"/> event of
		/// the archive extractors.
		/// </summary>
		/// <remarks>
		/// This cancels the extraction if the user has cancelled the task.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FileInfoEventArgs"/> describing the event arguments.</param>
		private void Extractor_FileExtractionStarted(object sender, FileInfoEventArgs e)
		{
			e.Cancel = Status == TaskStatus.Cancelling;
		}

		/// <summary>
		/// Handles the <see cref="IModCompressor.FileCompressionFinished"/> event of
		/// the mod compressors.
		/// </summary>
		/// <remarks>
		/// This cancels the compression if the user has cancelled the task. This also updates
		/// the item progress.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		private void Compressor_FileCompressionFinished(object sender, CancelEventArgs e)
		{
			e.Cancel = Status == TaskStatus.Cancelling;
			StepItemProgress();
		}

		#region IDisposable Members

		/// <summary>
		/// Cancels the task execution.
		/// </summary>
		/// <remarks>
		/// After being disposed, that is no guarantee that the task's status will be correct. Further
		/// interaction with the object is undefined.
		/// </remarks>
		public void Dispose()
		{
			Cancel();
		}

		#endregion
	}
}
