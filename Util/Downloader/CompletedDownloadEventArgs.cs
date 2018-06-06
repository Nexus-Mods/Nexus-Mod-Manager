using System;

namespace Nexus.Client.Util.Downloader
{
	/// <summary>
	/// Describes the arguments of a completed download event.
	/// </summary>
	public class CompletedDownloadEventArgs : EventArgs
	{
		#region Properties

		/// <summary>
		/// Gets whether the entire file has been downloaded.
		/// </summary>
		/// <value>Whether the entire file has been downloaded.</value>
		public bool GotEntireFile { get; private set; }

		/// <summary>
		/// Gets the path to the downloaded file.
		/// </summary>
		/// <value>The path to the downloaded file.</value>
		public string SavedFileName { get; private set; }

        /// <summary>
        /// Gets the failure message, if the download didn't succeed.
        /// </summary>
        public string FailureMessage { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_booGetEntireFile">Whether the entire file has been downloaded.</param>
		/// <param name="p_strSavedFileName">The path to the downloaded file.</param>
		public CompletedDownloadEventArgs(bool p_booGetEntireFile, string p_strSavedFileName)
		{
			GotEntireFile = p_booGetEntireFile;
			SavedFileName = p_strSavedFileName;
            FailureMessage = string.Empty;
		}

        /// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_booGetEntireFile">Whether the entire file has been downloaded.</param>
		/// <param name="p_strSavedFileName">The path to the downloaded file.</param>
        /// <param name="p_strFailureMessage">Failure message, if download did not succeed.</param>
        public CompletedDownloadEventArgs(bool p_booGetEntireFile, string p_strSavedFileName, string p_strFailureMessage)
        {
            GotEntireFile = p_booGetEntireFile;
            SavedFileName = p_strSavedFileName;
            FailureMessage = p_strFailureMessage;
        }

		#endregion
	}
}
