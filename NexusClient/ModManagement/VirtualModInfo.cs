using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModInfo : IVirtualModInfo, IEqualityComparer<IVirtualModInfo>
	{
		#region Properties

		public string ModId { get; set; }
		public string ModName { get; set; }
		public string ModFileName { get; private set; }
		public string ModFilePath { get; set; }
		public string ModFileFullPath
		{
			get
			{
				return Path.Combine(ModFilePath, ModFileName);
			}
		}

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

		#endregion

		#region IEqualityComparer

		public bool Equals(IVirtualModInfo a, IVirtualModInfo b)
		{
			if (a.ModFileName == b.ModFileName)
			{
				return true;
			}
			else
			{
				return false;
			}
		}

		public int GetHashCode(IVirtualModInfo a)
		{
			return a.GetHashCode();
		}

		#endregion
	}
}
