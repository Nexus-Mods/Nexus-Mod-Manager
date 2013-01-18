using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Downloader;

namespace Nexus.Client.Updating
{
	/// <summary>
	/// Provides a base implementation of <see cref="IUpdater"/>.
	/// </summary>
	/// <remarks>
	/// It is recommended that all updaters derivce from this base class. This class provides
	/// helper methods for downloading files, and publishing the status of the update.
	/// </remarks>
	public abstract class UpdaterBase : ObservableObject, IUpdater
	{
		private Int32 m_intProgressMaximum = 100;
		private Int32 m_intProgress = 0;
		private string m_strMessage = null;
		private bool m_booRestartRequired = false;
		private Dictionary<FileDownloader, AutoResetEvent> m_dicWaitForDownloads = new Dictionary<FileDownloader, AutoResetEvent>();
		private Dictionary<FileDownloader, string> m_dicSaveDownloadPaths = new Dictionary<FileDownloader, string>();
		private ConfirmActionMethod m_camConfirmMethod = null;

		#region Properties

		/// <summary>
		/// Gets the updater's name.
		/// </summary>
		/// <value>The updater's name.</value>
		public abstract string Name { get; }

		/// <summary>
		/// Gets the maximum value of the updater's progress.
		/// </summary>
		/// <value>The maximum value of the updater's progress.</value>
		public Int32 ProgressMaximum
		{
			get
			{
				return m_intProgressMaximum;
			}
			private set
			{
				SetPropertyIfChanged(ref m_intProgressMaximum, value, () => ProgressMaximum);
			}
		}

		/// <summary>
		/// Gets the updater's progress.
		/// </summary>
		/// <value>The updater's progress.</value>
		public Int32 Progress
		{
			get
			{
				return m_intProgress;
			}
			private set
			{
				SetPropertyIfChanged(ref m_intProgress, value, () => Progress);
			}
		}

		/// <summary>
		/// Gets the updater's message.
		/// </summary>
		/// <remarks>
		/// The updater's message can change throughout the updating process, in order
		/// to reflect the current state.
		/// </remarks>
		/// <value>The updater's message.</value>
		public string Message
		{
			get
			{
				return m_strMessage;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strMessage, value, () => Message);
			}
		}

		/// <summary>
		/// Gets whether the programme needs to be restarted in order for the update to
		/// take effect.
		/// </summary>
		/// <value>Whether the programme needs to be restarted in order for the update to
		/// take effect.</value>
		public bool RequiresRestart
		{
			get
			{
				return m_booRestartRequired;
			}
			private set
			{
				SetPropertyIfChanged(ref m_booRestartRequired, value, () => RequiresRestart);
			}
		}

		/// <summary>
		/// Sets the method to use to confirm updater actions.
		/// </summary>
		/// <value>The method to use to confirm updater actions.</value>
		public ConfirmActionMethod Confirm
		{
			protected get
			{
				return m_camConfirmMethod ?? DefaultConfirm;
			}
			set
			{
				m_camConfirmMethod = value;
			}
		}

		/// <summary>
		/// Gets whether the updater has been asked to cancel its actions.
		/// </summary>
		/// <value>Whether the updater has been asked to cancel its actions.</value>
		protected bool CancelRequested { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public UpdaterBase(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
		}

		#endregion

		#region Property Helpers

		/// <summary>
		/// Sets the updater's <see cref="ProgressMaximum"/>.
		/// </summary>
		/// <param name="p_intMaximum">The new maximum to use for the updater's progress.</param>
		protected void SetProgressMaximum(Int32 p_intMaximum)
		{
			ProgressMaximum = p_intMaximum;
		}

		/// <summary>
		/// Sets the updater's <see cref="Progress"/>.
		/// </summary>
		/// <param name="p_intProgress">The updater's new progress.</param>
		protected void SetProgress(Int32 p_intProgress)
		{
			Progress = p_intProgress;
		}

		/// <summary>
		/// Sets the updater's <see cref="Message"/>.
		/// </summary>
		/// <param name="p_strMessage">The updater's new message.</param>
		protected void SetMessage(string p_strMessage)
		{
			Message = p_strMessage;
		}

		/// <summary>
		/// Sets the updater's <see cref="RequiresRestart"/>.
		/// </summary>
		/// <param name="p_booRequiresRestart">The updater's new requires restart value.</param>
		protected void SetRequiresRestart(bool p_booRequiresRestart)
		{
			RequiresRestart = p_booRequiresRestart;
		}

		#endregion

		/// <summary>
		/// Performs the update.
		/// </summary>
		/// <returns><c>true</c> if the update completed successfully;
		/// <c>false</c> otherwise.</returns>
		public abstract bool Update();

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public void Cancel()
		{
			CancelRequested = true;
			foreach (FileDownloader fdrDownloader in m_dicWaitForDownloads.Keys.ToArray())
				fdrDownloader.Stop();
		}

		#region Download Helpers

		/// <summary>
		/// Downloads the specified file, and returns the file contents as a string.
		/// </summary>
		/// <param name="p_uriUrl">The URL of the file to download.</param>
		/// <returns>The file contents as a string.</returns>
		protected string DownloadFileContentsAsString(Uri p_uriUrl)
		{
			string strFileName = DownloadFile(p_uriUrl);
			byte[] bteFile = null;
			if (!String.IsNullOrEmpty(strFileName))
			{
				bteFile = File.ReadAllBytes(strFileName);
				FileUtil.ForceDelete(strFileName);
			}
			return TextUtil.ByteToString(bteFile);
		}

		/// <summary>
		/// Downloads the specified file, and returns the file contents.
		/// </summary>
		/// <param name="p_uriUrl">The URL of the file to download.</param>
		/// <returns>The file contents.</returns>
		protected byte[] DownloadFileContents(Uri p_uriUrl)
		{
			string strFileName = DownloadFile(p_uriUrl);
			byte[] bteFile = null;
			if (!String.IsNullOrEmpty(strFileName))
			{
				bteFile = File.ReadAllBytes(strFileName);
				FileUtil.ForceDelete(strFileName);
			}
			return bteFile;
		}

		/// <summary>
		/// Downloads the specified file, and returns the local path where the file was saved.
		/// </summary>
		/// <param name="p_uriUrl">The URL of the file to download.</param>
		/// <returns>The local path where the specified file was saved.</returns>
		protected string DownloadFile(Uri p_uriUrl)
		{
			//TODO get the max connection and block size from settings
			FileDownloader fdrDownloader = new FileDownloader(p_uriUrl, null, EnvironmentInfo.TemporaryPath, true, 5, 500 * 1024, "");
			fdrDownloader.DownloadComplete += new EventHandler<CompletedDownloadEventArgs>(Downloader_DownloadComplete);
			fdrDownloader.StartDownload();
			m_dicWaitForDownloads[fdrDownloader] = new AutoResetEvent(false);
			m_dicWaitForDownloads[fdrDownloader].WaitOne();
			
			string strFileName = m_dicSaveDownloadPaths[fdrDownloader];
			m_dicSaveDownloadPaths.Remove(fdrDownloader);
			m_dicWaitForDownloads.Remove(fdrDownloader);
			return strFileName;
		}

		/// <summary>
		/// Handles the <see cref="FileDownloader.DownloadComplete"/> event of the file downloader.
		/// </summary>
		/// <remarks>
		/// This stops the updating of the progress properties.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="CompletedDownloadEventArgs"/> describing the event arguments.</param>
		private void Downloader_DownloadComplete(object sender, CompletedDownloadEventArgs e)
		{
			FileDownloader fdrDownloader = ((FileDownloader)sender);
			if (!e.GotEntireFile)
			{
				fdrDownloader.Cleanup();
				m_dicSaveDownloadPaths[fdrDownloader] = null;
			}
			m_dicSaveDownloadPaths[fdrDownloader] = e.SavedFileName;
			if (m_dicWaitForDownloads.ContainsKey(fdrDownloader))
				m_dicWaitForDownloads[fdrDownloader].Set();
		}

		#endregion

		/// <summary>
		/// The default confirm method.
		/// </summary>
		/// <param name="p_strMessage">The message describing the action to confirm. Ignored.</param>
		/// <param name="p_strTitle">The title of the action to confirm. Ignored.</param>
		/// <returns>Always <c>true</c>.</returns>
		private bool DefaultConfirm(string p_strMessage, string p_strTitle)
		{
			return true;
		}
	}
}
