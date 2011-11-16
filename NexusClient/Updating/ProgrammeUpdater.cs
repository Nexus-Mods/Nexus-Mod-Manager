using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Nexus.Client.Util;

namespace Nexus.Client.Updating
{
	/// <summary>
	/// Updates the programme.
	/// </summary>
	public class ProgrammeUpdater : UpdaterBase
	{
		#region Properties

		/// <summary>
		/// Gets the updater's name.
		/// </summary>
		/// <value>The updater's name.</value>
		public override string Name
		{
			get
			{
				return String.Format("{0} Updater", EnvironmentInfo.Settings.ModManagerName);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public ProgrammeUpdater(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
			SetRequiresRestart(true);
		}

		#endregion

		/// <summary>
		/// Performs the update.
		/// </summary>
		/// <returns><c>true</c> if the update completed successfully;
		/// <c>false</c> otherwise.</returns>
		public override bool Update()
		{
			Trace.TraceInformation("Checking for new client version...");
			Trace.Indent();
			SetProgressMaximum(2);
			SetMessage(String.Format("Checking for new {0} version...", EnvironmentInfo.Settings.ModManagerName));
			Version verNew = GetNewProgrammeVersion();
			SetProgress(1);

			if (CancelRequested)
			{
				Trace.Unindent();
				return CancelUpdate();
			}

			if (verNew == new Version("0.0.0.0"))
			{
				SetMessage("Could not get version information from the update server.");
				return false;
			}

			if (verNew > new Version(ProgrammeMetadata.VersionString))
			{
				if (!Confirm(String.Format("A new version of {0} is available ({1}).{2}Would you like to download and install it?", EnvironmentInfo.Settings.ModManagerName, verNew, Environment.NewLine), "New Version"))
				{
					Trace.Unindent();
					return CancelUpdate();
				}

				SetMessage(String.Format("Downloading new {0} version...", EnvironmentInfo.Settings.ModManagerName));
				string strNewInstaller = DownloadFile(new Uri(String.Format("http://dev.tesnexus.com/client/releases/Nexus Mod Manager-{0}.exe", verNew.ToString())));
				SetProgress(2);

				if (CancelRequested)
				{
					Trace.Unindent();
					return CancelUpdate();
				}

				if (!String.IsNullOrEmpty(strNewInstaller))
				{
					string strOldPath = strNewInstaller;
					strNewInstaller = Path.Combine(Path.GetTempPath(), Path.GetFileName(strNewInstaller));
					FileUtil.ForceDelete(strNewInstaller);
					File.Move(strOldPath, strNewInstaller);

					SetMessage("Launching installer...");
					ProcessStartInfo psiInfo = new ProcessStartInfo(strNewInstaller);
					Process.Start(psiInfo);
					Trace.Unindent();
					return true;
				}
			}
			SetMessage(String.Format("{0} is already up to date.", EnvironmentInfo.Settings.ModManagerName));
			SetProgress(2);
			Trace.Unindent();
			return true;
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		/// <remarks>
		/// This is a convience method that allows the setting of the message and
		/// the determination of the return value in one call.
		/// </remarks>
		/// <returns>Always <c>true</c>.</returns>
		private bool CancelUpdate()
		{
			SetMessage(String.Format("Cancelled {0} update.", EnvironmentInfo.Settings.ModManagerName));
			SetProgress(2);
			return true;
		}

		/// <summary>
		/// Gets the newest available programme version.
		/// </summary>
		/// <returns>The newest available programme version,
		/// or 0.0.0.0 if now information could be retrieved.</returns>
		private Version GetNewProgrammeVersion()
		{
			FtpWebRequest fwrGetter = (FtpWebRequest)WebRequest.Create("ftp://dev.tesnexus.com/releases");
			//the current server doesn't allow anonymous access, so use this for now
			fwrGetter.Credentials = new NetworkCredential("readonly@dev.tesnexus.com", "n0writing");
			fwrGetter.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
			SortedList<DateTime, string> sltFileList = new SortedList<DateTime, string>();
			Version verNew = new Version("0.0.0.0");
			try
			{
				using (FtpWebResponse wrpFileList = (FtpWebResponse)fwrGetter.GetResponse())
				{
					if ((wrpFileList.StatusCode != FtpStatusCode.DataAlreadyOpen) && (wrpFileList.StatusCode != FtpStatusCode.OpeningData))
						throw new Exception("Request to the update directory failed with FTP error: " + wrpFileList.StatusCode);

					Stream stmFileList = wrpFileList.GetResponseStream();
					using (StreamReader srdFileList = new StreamReader(stmFileList))
					{
						while (!srdFileList.EndOfStream)
							ParseFileListEntry(sltFileList, srdFileList.ReadLine());
						srdFileList.Close();
					}
					wrpFileList.Close();
				}
				if (sltFileList.Count > 0)
				{
					Regex rgxVersion = new Regex(@"-(\d+(\.\d+)+)\.exe");
					Match mchVersion = rgxVersion.Match(sltFileList.Last().Value);
					if (mchVersion.Success)
						verNew = new Version(mchVersion.Groups[1].Value);
				}
			}
			catch (WebException e)
			{
				Trace.TraceError(String.Format("Could not connect to update server: {0}", e.Message));
			}
			return verNew;
		}

		private void ParseFileListEntry(SortedList<DateTime, string> p_sltFileList, string p_strEntry)
		{
			Regex rgxDate = new Regex(@"\w\w\w\s+\d\d?\s\d\d:\d\d");
			Match mchDate = rgxDate.Match(p_strEntry);
			if (!mchDate.Success)
				throw new Exception("Cannot parse file list.");
			string strDate = mchDate.Groups[0].Value.Replace("  ", " ");
			DateTime dteDate = DateTime.ParseExact(strDate, "MMM d HH:mm", CultureInfo.InvariantCulture);
			string strFilename = p_strEntry.Substring(mchDate.Index + mchDate.Length);
			if (strFilename.Contains("exe"))
				p_sltFileList[dteDate] = strFilename;
		}
	}
}
