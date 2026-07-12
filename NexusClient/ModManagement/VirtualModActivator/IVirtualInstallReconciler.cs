namespace Nexus.Client.ModManagement
{
	using System.Collections.Generic;

	internal interface IVirtualInstallReconciler
	{
		VirtualInstallReconciliationReport Inspect(IList<IVirtualModInfo> virtualMods, IList<IVirtualModLink> virtualLinks, IList<string> sourceRoots, IList<string> gameDataRoots);
	}
}