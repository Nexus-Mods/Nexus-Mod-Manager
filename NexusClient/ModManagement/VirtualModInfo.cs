using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModInfo : IVirtualModInfo, IEquatable<IVirtualModInfo>, IEqualityComparer<IVirtualModInfo>
	{
		string m_strDownloadID = string.Empty;
		string m_strModID = string.Empty;

		private static string GetNumbers(string p_strInput)
		{
			return new string(p_strInput.Where(c => char.IsDigit(c)).ToArray());
		}

		/// <summary>Notifies the native store tracker when mutable persisted metadata changes.</summary>
		internal event EventHandler PersistenceChanged;

		#region Properties

		public string ModId
		{
			get
			{
				if (!string.IsNullOrEmpty(m_strModID))
				{
					return GetNumbers(m_strModID);
				}
				else
					return m_strModID;
			}
			set
			{
				if (m_strModID == value) return;
				m_strModID = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		private string _persistedModName;
		public string ModName
		{
			get { return _persistedModName; }
			set
			{
				if (_persistedModName == value) return;
				_persistedModName = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		private string _persistedNewFileName;
		public string NewFileName
		{
			get { return _persistedNewFileName; }
			set
			{
				if (_persistedNewFileName == value) return;
				_persistedNewFileName = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		public string DownloadId
		{
			get
			{
				if (!string.IsNullOrEmpty(m_strDownloadID))
				{
					return GetNumbers(m_strDownloadID);
				}
				else
					return m_strDownloadID;
			}
			set
			{
				if (m_strDownloadID == value) return;
				m_strDownloadID = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		private string _persistedUpdatedDownloadId;
		public string UpdatedDownloadId
		{
			get { return _persistedUpdatedDownloadId; }
			set
			{
				if (_persistedUpdatedDownloadId == value) return;
				_persistedUpdatedDownloadId = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		public string ModFileName { get; private set; }
		private string _persistedModFilePath;
		public string ModFilePath
		{
			get { return _persistedModFilePath; }
			set
			{
				if (_persistedModFilePath == value) return;
				_persistedModFilePath = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		public string ModFileFullPath
		{
			get
			{
				if (FileUtil.IsValidPath(ModFilePath) && FileUtil.IsValidPath(ModFileName))
					return Path.Combine(ModFilePath, ModFileName);
				else
					return null;
			}
		}
		public string FileVersion { get; }

		#endregion

		#region Constructor

		public VirtualModInfo(string p_strModId, string p_strDownloadId, string p_strModName, string p_strModFile, string p_strFileVersion)
		{
			ModId = p_strModId;
			ModName = p_strModName;
			DownloadId = p_strDownloadId;
			UpdatedDownloadId = null;
			ModFileName = Path.GetFileName(p_strModFile);
			NewFileName = null;
			ModFilePath = Path.GetDirectoryName(p_strModFile);
			FileVersion = p_strFileVersion;
		}

		public VirtualModInfo(string p_strModId, string p_strDownloadId, string p_strModName, string p_strModFileName, string p_strModFilePath, string p_strFileVersion)
		{
			ModId = p_strModId;
			ModName = p_strModName;
			DownloadId = p_strDownloadId;
			UpdatedDownloadId = null;
			ModFileName = p_strModFileName;
			NewFileName = null;
			ModFilePath = p_strModFilePath;
			FileVersion = p_strFileVersion;
		}

		public VirtualModInfo(string p_strModId, string p_strDownloadId, string p_strUpdatedDownloadId, string p_strModName, string p_strModFileName, string p_strModNewFileName, string p_strModFilePath, string p_strFileVersion)
		{
			ModId = p_strModId;
			ModName = p_strModName;
			DownloadId = p_strDownloadId;
			UpdatedDownloadId = p_strUpdatedDownloadId;
			ModFileName = p_strModFileName;
			NewFileName = p_strModNewFileName;
			ModFilePath = p_strModFilePath;
			FileVersion = p_strFileVersion;
		}

		public VirtualModInfo(IVirtualModInfo p_ivmModInfo)
		{
			ModId = p_ivmModInfo.ModId;
			ModName = p_ivmModInfo.ModName;
			DownloadId = p_ivmModInfo.DownloadId;
			UpdatedDownloadId = p_ivmModInfo.UpdatedDownloadId;
			ModFileName = p_ivmModInfo.ModFileName;
			NewFileName = null;
			ModFilePath = p_ivmModInfo.ModFilePath;
			FileVersion = p_ivmModInfo.FileVersion;
		}

		#endregion

		#region IEqualityComparer

		public bool Equals(IVirtualModInfo a, IVirtualModInfo b)
		{
			if ((a == null) || (b == null)) return false;

			if (a.ModFileName.Equals(b.ModFileName, StringComparison.InvariantCultureIgnoreCase))
				return true;

			if (!string.IsNullOrEmpty(a.DownloadId) || !string.IsNullOrEmpty(b.DownloadId))
			{
				if ((a.DownloadId.Equals(b.DownloadId, StringComparison.InvariantCultureIgnoreCase)))
					return true;

				if (!string.IsNullOrEmpty(a.UpdatedDownloadId) && a.UpdatedDownloadId.Equals(b.DownloadId, StringComparison.InvariantCultureIgnoreCase))
					return true;

				if (!string.IsNullOrEmpty(b.UpdatedDownloadId) && a.DownloadId.Equals(b.UpdatedDownloadId, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		public int GetHashCode(IVirtualModInfo a)
		{
			VirtualModInfo vmi = new VirtualModInfo(a);
			vmi.UpdatedDownloadId = string.Empty;
			vmi.ModName = string.Empty;
			return vmi.GetHashCode();
		}

		#region IEquatable<IVirtualModLink>

		public bool Equals(IVirtualModInfo other)
		{
			if (other == null) return false;

			if (this.ModFileName.Equals(other.ModFileName, StringComparison.InvariantCultureIgnoreCase))
				return true;

			if (!string.IsNullOrEmpty(this.DownloadId) || !string.IsNullOrEmpty(other.DownloadId))
			{
				if ((this.DownloadId.Equals(other.DownloadId, StringComparison.InvariantCultureIgnoreCase)))
					return true;

				if (!string.IsNullOrEmpty(this.UpdatedDownloadId) && this.UpdatedDownloadId.Equals(other.DownloadId, StringComparison.InvariantCultureIgnoreCase))
					return true;

				if (!string.IsNullOrEmpty(other.UpdatedDownloadId) && this.DownloadId.Equals(other.UpdatedDownloadId, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		#endregion

		#endregion
	}
}
