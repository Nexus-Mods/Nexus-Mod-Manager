using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexus.Client.ModManagement
{
	public partial class BackupInfo : IBackupInfo, IEquatable<IBackupInfo>
	{
		#region Properties

		public string VirtualModPath { get; set; }
		public string RealModPath { get; set; }
		public string ModID { get; set; }
		public string Directory { get; set; }
		public long Size { get; set; }


		#endregion

		#region Constructors

		public BackupInfo(string p_strVirtualModPath, string p_strRealModPath, string p_strModID, string p_strDirectory, long p_lngSize)
		{
			VirtualModPath = p_strVirtualModPath;
			RealModPath = p_strRealModPath;
			ModID = p_strModID;
			Directory = p_strDirectory;
			Size = p_lngSize;
		}

		#endregion

		#region IEquatable<IBackupInfo>

		public bool Equals(IBackupInfo other)
		{
			if (other == null) return false;
			return (this.RealModPath.Equals(other.RealModPath, StringComparison.InvariantCultureIgnoreCase) && this.VirtualModPath.Equals(other.VirtualModPath, StringComparison.InvariantCultureIgnoreCase));
		}
		
		#endregion
	}
}

