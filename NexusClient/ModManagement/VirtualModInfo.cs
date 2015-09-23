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
