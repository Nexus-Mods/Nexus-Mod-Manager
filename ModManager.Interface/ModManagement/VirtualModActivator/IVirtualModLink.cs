using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nexus.Client.ModManagement
{
	public interface IVirtualModLink
	{
		#region Properties
		string VirtualModPath { get; set; }
		string RealModPath { get; set; }
		string ModName { get; set; }
		string ModFileName { get; set; }
		int Priority { get; set; }
		bool Active { get; set; }
		#endregion
	}
}
