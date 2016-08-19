using System;
using System.Collections.Generic;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModLink : IVirtualModLink, IEquatable<VirtualModLink>
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

		public VirtualModLink(IVirtualModLink p_vmlLink)
		{
			VirtualModPath = p_vmlLink.VirtualModPath;
			RealModPath = p_vmlLink.RealModPath;
			Priority = p_vmlLink.Priority;
			Active = p_vmlLink.Active;
			ModInfo = p_vmlLink.ModInfo;
		}

	#endregion

	#region IEquatable<IVirtualModLink>

	public bool Equals(IVirtualModLink other)
		{
			if (other == null) return false;
			return (this.RealModPath.Equals(other.RealModPath, StringComparison.InvariantCultureIgnoreCase) && this.VirtualModPath.Equals(other.VirtualModPath, StringComparison.InvariantCultureIgnoreCase));
		}

		public bool Equals(VirtualModLink other)
		{
			if (other == null) return false;
			return (this.RealModPath.Equals(other.RealModPath, StringComparison.InvariantCultureIgnoreCase) && this.VirtualModPath.Equals(other.VirtualModPath, StringComparison.InvariantCultureIgnoreCase));
		}

		#endregion
	}
}
