using System;

namespace Nexus.Client.ModManagement
{
	public interface IVirtualModLink
	{
		#region Properties

		string VirtualModPath { get; set; }
		string RealModPath { get; set; }
		int Priority { get; set; }
		bool Active { get; set; }
		IVirtualModInfo ModInfo { get; set; }

		#endregion
	}
}
