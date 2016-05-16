using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModInfo : IVirtualModInfo, IEquatable<IVirtualModInfo>, IEqualityComparer<IVirtualModInfo>
	{
		#region Properties

		public string ModId { get; set; }
		public string ModName { get; set; }
		public string NewFileName { get; set; }
		public string DownloadId { get; set; }
		public string UpdatedDownloadId { get; set; }
		public string ModFileName { get; private set; }
		public string ModFilePath { get; set; }
		public string ModFileFullPath
		{
			get
			{
				return Path.Combine(ModFilePath, ModFileName);
			}
		}
		public string FileVersion { get; }

		#endregion

		#region Constructor

		public VirtualModInfo(string p_strModFileName) 
		{
			ModFileName = p_strModFileName;
		}

		public VirtualModInfo(string p_strModId, string p_strModName, string p_strModFile)
		{
			ModId = p_strModId;
			ModName = p_strModName;
			ModFileName = Path.GetFileName(p_strModFile);
			ModFilePath = Path.GetDirectoryName(p_strModFile);
		}

		public VirtualModInfo(string p_strModId, string p_strModName, string p_strModFileName, string p_strModFilePath)
		{
			ModId = p_strModId;
			ModName = p_strModName;
			ModFileName = p_strModFileName;
			ModFilePath = p_strModFilePath;
		}

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
			if (b == null)
				return false;
			return (a.ModFileName.Equals(b.ModFileName, StringComparison.InvariantCultureIgnoreCase));
		}

		public int GetHashCode(IVirtualModInfo a)
		{
			return a.GetHashCode();
		}

		#region IEquatable<IVirtualModLink>

		public bool Equals(IVirtualModInfo other)
		{
			if (other == null) return false;
			return (this.ModFileName.Equals(other.ModFileName, StringComparison.InvariantCultureIgnoreCase));
		}

		public bool Equals(VirtualModInfo other)
		{
			if (other == null) return false;
			return (this.ModFileName.Equals(other.ModFileName, StringComparison.InvariantCultureIgnoreCase));
		}

		#endregion

		#endregion
	}
}
