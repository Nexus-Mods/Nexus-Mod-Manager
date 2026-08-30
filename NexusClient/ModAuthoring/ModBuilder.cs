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
using Nexus.Client.Util.Localization;
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
		private readonly string m_strBuildingModText;
		private readonly string m_strExaminingArchiveText;
		private readonly string m_strExaminingArchiveFormat;
		private readonly string m_strDeterminingArchiveFormatText;
		private readonly string m_strExtractingArchiveText;
		private readonly string m_strCompressingModText;
		private readonly string m_strCopyingModsText;
		private readonly string m_strCopyingModFormat;

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
			m_strBuildingModText = LanguageManager.Get("ModAuthoring.Builder.Progress.Building", "Building Mod...");
			m_strExaminingArchiveText = LanguageManager.Get("ModAuthoring.Builder.Progress.ExaminingArchive", "Examining archive...");
			m_strExaminingArchiveFormat = LanguageManager.GetFormat("ModAuthoring.Builder.Progress.ExaminingArchiveForFormat", "Examining archive for {0} mods...");
			m_strDeterminingArchiveFormatText = LanguageManager.Get("ModAuthoring.Builder.Progress.DeterminingFormat", "Determining archive format...");
			m_strExtractingArchiveText = LanguageManager.Get("ModAuthoring.Builder.Progress.Extracting", "Extracting archive...");
			m_strCompressingModText = LanguageManager.Get("ModAuthoring.Builder.Progress.Compressing", "Compressing mod...");
			m_strCopyingModsText = LanguageManager.Get("ModAuthoring.Builder.Progress.CopyingMods", "Copying mods...");
			m_strCopyingModFormat = LanguageManager.GetFormat("ModAuthoring.Builder.Progress.CopyingMod", "Copying mod {0}...");
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
		/// <param name="args">Arguments to for the task execution.</param>
		/// <param name="p_strMessage">The message describing the state of the task.</param>
		/// <returns>A return value.</returns>
		protected override object DoWork(object[] args, out string p_strMessage)
		{
			switch ((Sources)args[0])
			{
				case Sources.Archive:
					return DoFromArchive(
						(IModFormatRegistry)args[1],
						(string)args[2],
						(ConfirmOverwriteCallback)args[3],
						args.Length > 4 && (bool)args[4],
						out p_strMessage);
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
		/// <param name="p_booConsumeSource">Whether the source is a task-owned temporary download that may be moved instead of copied.</param>
		/// <exception cref="ArgumentException">Thrown if the specified path is not an archive.</exception>
		public void BuildFromFile(IModFormatRegistry p_mfrFormats, string p_strFilePath, ConfirmOverwriteCallback p_dlgConfirmOverwrite, bool p_booConsumeSource = false)
		{
			ShowItemProgress = true;
			OverallProgressStepSize = 1;
			ItemProgressStepSize = 1;
			OverallProgressMaximum = 4;
			OverallMessage = m_strBuildingModText;
			Sources srcModSource = Sources.Archive;
			if (String.IsNullOrEmpty(p_strFilePath) || !File.Exists(p_strFilePath))
				throw new ArgumentException("The given file path does not exist: " + p_strFilePath);
			Stopwatch validationTimer = Stopwatch.StartNew();
			bool isArchive = Archive.IsArchive(p_strFilePath);
			validationTimer.Stop();
			Trace.TraceInformation(String.Format("[{0}] Archive validation completed in {1} ms.", p_strFilePath, validationTimer.ElapsedMilliseconds));
			if (!isArchive)
			{
				Status = TaskStatus.Error;
				OnTaskEnded(LanguageManager.Format("ModAuthoring.Builder.Error.UnrecognizedFormat", "Cannot add {0}. File format is not recognized.", Path.GetFileName(p_strFilePath)), null);
				return;
			}

			Start(srcModSource, p_mfrFormats, p_strFilePath, p_dlgConfirmOverwrite, p_booConsumeSource);
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
		/// <param name="p_booConsumeSource">Whether the original archive is a task-owned temporary download that may be moved instead of copied.</param>
		/// <param name="p_strMessage">The message describing the state of the task.</param>
		/// <returns>The paths to the new mods.</returns>
		/// <exception cref="ArgumentException">Thrown if the specified path is not an archive.</exception>
		private IList<string> DoFromArchive(IModFormatRegistry p_mfrFormats, string p_strArchivePath, ConfirmOverwriteCallback p_dlgConfirmOverwrite, bool p_booConsumeSource, out string p_strMessage)
		{
			p_strMessage = null;
			Stopwatch totalTimer = Stopwatch.StartNew();
			Trace.TraceInformation(String.Format("[{0}] Adding mod from archive.", p_strArchivePath));
			// BuildFromFile validates the archive before the background worker starts. Repeating
			// Archive.IsArchive here reopens large archives and adds avoidable post-download latency.
			if (String.IsNullOrEmpty(p_strArchivePath) || !File.Exists(p_strArchivePath))
				throw new ArgumentException("The specified path is not an archive file.", "p_strArchivePath");

			List<string> lstFoundMods = new List<string>();
			List<string> lstModsInArchive = new List<string>();

			ItemMessage = m_strExaminingArchiveText;
			ItemProgress = 0;
			ItemProgressMaximum = p_mfrFormats.Formats.Count;
			IModFormat mftDestFormat = null;
			bool isMultiVolume = false;

			try
			{
				Stopwatch examineTimer = Stopwatch.StartNew();
				using (SevenZipExtractor szeExtractor = Archive.GetExtractor(p_strArchivePath))
				{
					if (Status == TaskStatus.Cancelling)
						return lstFoundMods;
					isMultiVolume = szeExtractor.VolumeFileNames.Count > 1;
					ReadOnlyCollection<string> lstArchiveFiles = szeExtractor.ArchiveFileNames;
					foreach (IModFormat mftFormat in p_mfrFormats.Formats)
					{
						ItemMessage = String.Format(m_strExaminingArchiveFormat, mftFormat.Name);
						lstModsInArchive.AddRange(lstArchiveFiles.Where(x => mftFormat.Extension.Equals(Path.GetExtension(x), StringComparison.OrdinalIgnoreCase)));
						StepItemProgress();
						if (Status == TaskStatus.Cancelling)
							return lstFoundMods;
					}
					StepOverallProgress();
				}
				examineTimer.Stop();
				Trace.TraceInformation(String.Format("[{0}] Archive examination completed in {1} ms.", p_strArchivePath, examineTimer.ElapsedMilliseconds));

				if (lstModsInArchive.Count == 0)
				{
					ItemMessage = m_strDeterminingArchiveFormatText;
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
				MessageBox.Show(LanguageManager.Format("ModAuthoring.Builder.ArchiveError", "An error has occured with the following archive: {0}\n\n ERROR: {1}", p_strArchivePath, ex.Message));
				return lstFoundMods;
			}
			string strTmpPath = null;
			try
			{
				bool requiresExtraction = ((mftDestFormat != null) && isMultiVolume) || lstModsInArchive.Count > 0;
				if (requiresExtraction)
				{
					Stopwatch extractionTimer = Stopwatch.StartNew();
					using (SevenZipExtractor szeExtractor = Archive.GetExtractor(p_strArchivePath))
					{
						ItemMessage = m_strExtractingArchiveText;
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
					}
					for (Int32 i = 0; i < lstModsInArchive.Count; i++)
						lstModsInArchive[i] = Path.Combine(strTmpPath, lstModsInArchive[i]);
					extractionTimer.Stop();
					Trace.TraceInformation(String.Format("[{0}] Archive extraction completed in {1} ms.", p_strArchivePath, extractionTimer.ElapsedMilliseconds));
				}
				else
				{
					// Directly usable archives do not need to be opened a second time.
					lstModsInArchive.Add(p_strArchivePath);
				}
				StepOverallProgress();

				if (!String.IsNullOrEmpty(strTmpPath) && (mftDestFormat != null))
				{
					//if we have extracted the file to do format shifting
					if (!mftDestFormat.SupportsModCompression)
						return lstFoundMods;
					ItemMessage = m_strCompressingModText;
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
					ItemMessage = m_strCopyingModsText;
					ItemProgress = 0;
					ItemProgressMaximum = lstModsInArchive.Count;
					foreach (string strMod in lstModsInArchive)
					{
						if (Status == TaskStatus.Cancelling)
							return lstFoundMods;
						ItemMessage = String.Format(m_strCopyingModFormat, Path.GetFileName(strMod));
						string strDest = Path.Combine(GameModeInfo.ModDirectory, Path.GetFileName(strMod));
						strDest = ConfirmOverwrite(p_dlgConfirmOverwrite, strDest);
						if (!String.IsNullOrEmpty(strDest))
						{
							if (string.Equals(strMod, strDest, StringComparison.OrdinalIgnoreCase))
								throw new FileNotFoundException(LanguageManager.Get("ModAuthoring.Builder.Error.ModInModsFolder", "You can't add a mod directly from the NMM Mods folder, please move it somewhere else before adding it to the manager!"));

							Stopwatch transferTimer = Stopwatch.StartNew();
							bool consumeThisSource = p_booConsumeSource && PathsEqual(strMod, p_strArchivePath);
							TransferModFile(strMod, strDest, consumeThisSource);
							transferTimer.Stop();
							Trace.TraceInformation(String.Format(
								"[{0}] Mod transfer completed in {1} ms ({2}).",
								p_strArchivePath,
								transferTimer.ElapsedMilliseconds,
								consumeThisSource && !File.Exists(strMod) ? "move" : "copy"));
							lstFoundMods.Add(strDest);
						}
						StepItemProgress();
					}
				}
				StepOverallProgress();
			}
			catch (FileNotFoundException ex)
			{
				MessageBox.Show(LanguageManager.Format("ModAuthoring.Builder.ArchiveWarning", "Archive: {0}\n\n ERROR: {1}", p_strArchivePath, ex.Message), LanguageManager.Get("Common.Dialog.WarningTitle", "Warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return lstFoundMods;
			}
			finally
			{
				if (!String.IsNullOrEmpty(strTmpPath))
					FileUtil.ForceDelete(strTmpPath);
			}
			totalTimer.Stop();
			Trace.TraceInformation(String.Format("[{0}] Mod build completed in {1} ms.", p_strArchivePath, totalTimer.ElapsedMilliseconds));
			return lstFoundMods;
		}

		/// <summary>
		/// Transfers a built mod into the Mods directory, moving task-owned downloads when the
		/// source and destination share a volume and no overwrite is required.
		/// </summary>
		/// <param name="sourcePath">Source archive path.</param>
		/// <param name="destinationPath">Final mod archive path.</param>
		/// <param name="consumeSource">Whether the source may be consumed by this operation.</param>
		private static void TransferModFile(string sourcePath, string destinationPath, bool consumeSource)
		{
			if (consumeSource && !File.Exists(destinationPath) && AreOnSameVolume(sourcePath, destinationPath))
			{
				try
				{
					File.Move(sourcePath, destinationPath);
					return;
				}
				catch (IOException)
				{
					// Antivirus/file-system races can make a metadata move fail temporarily.
					// Preserve the established copy semantics rather than failing the add operation.
				}
				catch (UnauthorizedAccessException)
				{
					// A source may be readable even when the current process cannot remove it.
					// Fall back to copying so download finalization retains the previous behavior.
				}
			}

			File.Copy(sourcePath, destinationPath, true);
		}

		/// <summary>
		/// Determines whether two paths are rooted on the same local drive or UNC share.
		/// </summary>
		private static bool AreOnSameVolume(string firstPath, string secondPath)
		{
			try
			{
				string firstRoot = Path.GetPathRoot(Path.GetFullPath(firstPath));
				string secondRoot = Path.GetPathRoot(Path.GetFullPath(secondPath));
				return !String.IsNullOrEmpty(firstRoot) &&
					String.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
			{
				return false;
			}
		}

		/// <summary>
		/// Compares two file paths after normalization.
		/// </summary>
		private static bool PathsEqual(string firstPath, string secondPath)
		{
			try
			{
				return String.Equals(
					Path.GetFullPath(firstPath),
					Path.GetFullPath(secondPath),
					StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
			{
				return false;
			}
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
