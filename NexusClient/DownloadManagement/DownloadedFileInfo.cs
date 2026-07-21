using System;

namespace Nexus.Client.DownloadManagement
{
	/// <summary>
	/// Summarizes the info about a download file.
	/// </summary>
	public class DownloadedFileInfo
	{
		#region Properties

		/// <summary>
		/// Gets the URL from whichthe file was downloaded.
		/// </summary>
		/// <value>The URL from whichthe file was downloaded.</value>
		public Uri URL { get; private set; }

		/// <summary>
		/// Gets the path to which the file was saved.
		/// </summary>
		/// <value>The path to which the file was saved.</value>
		public string SavedFilePath { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_uriUrl">The URL from whichthe file was downloaded.</param>
		/// <param name="p_strSavedFilePath">The path to which the file was saved.</param>
		public DownloadedFileInfo(Uri p_uriUrl, string p_strSavedFilePath)
		{
			URL = p_uriUrl;
			SavedFilePath = p_strSavedFilePath;
		}

		#endregion
	}
}
