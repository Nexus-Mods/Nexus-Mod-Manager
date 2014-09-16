using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModLink : IVirtualModLink
	{
		#region Properties
		public string VirtualModPath { get; set; }
		public string RealModPath { get; set; }
		public string ModName { get; set; }
		public string ModFileName { get; set; }
		public int Priority { get; set; }
		public bool Active { get; set; }
		#endregion

		#region Constructors
		public VirtualModLink(string p_strRealPath, string p_strVirtualPath, string p_strModName, string p_strModFileName, int p_intPriority, bool p_booActive)
		{
			VirtualModPath = p_strVirtualPath;
			RealModPath = p_strRealPath;
			ModName = p_strModName;
			ModFileName = p_strModFileName;
			Priority = p_intPriority;
			Active = p_booActive;
		}

		#endregion
	}
}
