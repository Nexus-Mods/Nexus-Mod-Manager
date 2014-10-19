using System;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModLink : IVirtualModLink
	{
		#region Properties
		
		public string VirtualModPath { get; set; }
		public string RealModPath { get; set; }
		public int Priority { get; set; }
		public bool Active { get; set; }
		public IVirtualModInfo ModInfo { get; set; }
		
		#endregion

		#region Constructors

		public VirtualModLink(string p_strRealPath, string p_strVirtualPath, int p_intPriority, bool p_booActive, IVirtualModInfo p_vmiVirtualModInfo)
		{
			VirtualModPath = p_strVirtualPath;
			RealModPath = p_strRealPath;
			Priority = p_intPriority;
			Active = p_booActive;
			ModInfo = p_vmiVirtualModInfo;
		}

		#endregion
	}
}
