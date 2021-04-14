using Nexus.Client.BackgroundTasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.ModManagement
{
	public class WriteProfileTask : ThreadedBackgroundTask
	{
		#region Fields

		private static readonly object _objLock = new object();
		private IModProfile _modProfile;
		private byte[] _modList;
		private byte[] _iniList;
		private byte[] _loadOrder;
		private string[] _optionalFiles;
		private string _profileManagerPath;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public WriteProfileTask(IModProfile modProfile, byte[] modList, byte[] iniList, byte[] loadOrder, string[] optionalFiles, string profileManagerPath)
		{
			_modProfile = modProfile;
			_modList = modList;
			_iniList = iniList;
			_loadOrder = loadOrder;
			_optionalFiles = optionalFiles;
			_profileManagerPath = profileManagerPath;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			base.OnTaskEnded(e);
		}

		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		public void Update()
		{
			Start();
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			base.Cancel();
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			//if (_modProfile != null)
			//{
			//	string strProfilePath = Path.Combine(p_strProfileManagerPath, _modProfile.Id);

			//	if (!Directory.Exists(strProfilePath))
			//		Directory.CreateDirectory(strProfilePath);

			//	try
			//	{
			//		if ((m_booUsesPlugin) && (p_bteLoadOrder != null) && (p_bteLoadOrder.Length > 0))
			//			File.WriteAllBytes(Path.Combine(strProfilePath, "loadorder.txt"), p_bteLoadOrder);
			//	}
			//	catch (Exception ex)
			//	{
			//		return "Error: " + ex.Message;
			//	}
			//	if ((p_bteModList != null) && (p_bteModList.Length > 0))
			//		File.WriteAllBytes(Path.Combine(strProfilePath, "modlist.xml"), p_bteModList);
			//	else if (VirtualModActivator.VirtualLinks != null)
			//	{
			//		if (VirtualModActivator.ModCount > 0)
			//			VirtualModActivator.SaveModList(Path.Combine(strProfilePath, "modlist.xml"));
			//		else
			//			FileUtil.ForceDelete(Path.Combine(strProfilePath, "modlist.xml"));
			//	}

			//	if ((p_bteIniList != null) && (p_bteIniList.Length > 0))
			//		File.WriteAllBytes(Path.Combine(strProfilePath, "IniEdits.xml"), p_bteIniList);

			//	byte[] bteProfileBytes = GetProfileBytes(p_impModProfile);

			//	if ((bteProfileBytes != null) && (bteProfileBytes.Length > 0))
			//		File.WriteAllBytes(Path.Combine(strProfilePath, "profile.xml"), GetProfileBytes(_modProfile));

			//	string strOptionalFolder = Path.Combine(strProfilePath, "Optional");

			//	if (!(p_strOptionalFiles == null))
			//	{
			//		if (Directory.Exists(strOptionalFolder))
			//		{
			//			string[] strFiles = Directory.GetFiles(strOptionalFolder);

			//			foreach (string file in strFiles)
			//				lock (_objLock)
			//					FileUtil.ForceDelete(file);
			//		}

			//		if ((p_strOptionalFiles != null) && (p_strOptionalFiles.Length > 0))
			//		{
			//			lock (_objLock)
			//				if (!Directory.Exists(strOptionalFolder))
			//					Directory.CreateDirectory(strOptionalFolder);

			//			foreach (string strFile in p_strOptionalFiles)
			//			{
			//				lock (_objLock)
			//					if (File.Exists(strFile))
			//						File.Copy(strFile, Path.Combine(strOptionalFolder, Path.GetFileName(strFile)), true);
			//			}
			//		}
			//	}
			//}

			return null;
		}

		/// <summary>
		/// Checks whether the file to write to is currently free for use.
		/// </summary>
		private static bool IsFileReady(string p_strFilePath, bool p_booReadOnly)
		{
			try
			{
				using (FileStream inputStream = File.Open(p_strFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
				{
					return (inputStream.Length >= 0);
				}
			}
			catch (Exception)
			{
				SetFileReadOnlyAccess(p_strFilePath, false);

				return false;
			}
		}

		/// <summary>
		/// Returns whether a file is read-only.
		/// </summary>
		private static bool IsFileReadOnly(string p_strFileName)
		{
			// Create a new FileInfo object.
			FileInfo fiInfo = new FileInfo(p_strFileName);

			// Return the IsReadOnly property value.
			return fiInfo.IsReadOnly;
		}

		/// <summary>
		/// Sets the read-only value of a file.
		/// </summary>
		private static void SetFileReadOnlyAccess(string p_strFileName, bool p_booSetReadOnly)
		{
			try
			{
				// Create a new FileInfo object.
				FileInfo fInfo = new FileInfo(p_strFileName);

				// Set the IsReadOnly property.
				fInfo.IsReadOnly = p_booSetReadOnly;
			}
			catch (Exception e)
			{
				Trace.TraceError("Could not set file read access of {0} to {1}: {2} - {3}", p_strFileName, p_booSetReadOnly, e.GetType(), e.Message);
			}
		}
	}
}
