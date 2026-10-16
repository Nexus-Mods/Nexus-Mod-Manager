using System.Collections.Generic;

namespace Nexus.Client.ModManagement
{
	/// <summary>Tracks native store changes by object identity, independently of mutable metadata equality.</summary>
	internal sealed class VirtualModStoreDelta
	{
		internal readonly HashSet<IVirtualModInfo> Mods = new HashSet<IVirtualModInfo>(NativeReferenceComparer<IVirtualModInfo>.Instance);
		internal readonly HashSet<IVirtualModInfo> RemovedMods = new HashSet<IVirtualModInfo>(NativeReferenceComparer<IVirtualModInfo>.Instance);
		internal readonly Dictionary<IVirtualModInfo, long> AppendedMods = new Dictionary<IVirtualModInfo, long>(NativeReferenceComparer<IVirtualModInfo>.Instance);
		internal readonly HashSet<IVirtualModLink> Links = new HashSet<IVirtualModLink>(NativeReferenceComparer<IVirtualModLink>.Instance);
		internal readonly HashSet<IVirtualModLink> RemovedLinks = new HashSet<IVirtualModLink>(NativeReferenceComparer<IVirtualModLink>.Instance);
		internal readonly Dictionary<IVirtualModLink, long> AppendedLinks = new Dictionary<IVirtualModLink, long>(NativeReferenceComparer<IVirtualModLink>.Instance);
		internal long Order;
		internal bool RequiresSnapshot;
		/// <summary>Clears changes only after the corresponding durable save succeeds.</summary>
		internal void Clear()
		{
			Mods.Clear();
			RemovedMods.Clear();
			AppendedMods.Clear();
			Links.Clear();
			RemovedLinks.Clear();
			AppendedLinks.Clear();
			RequiresSnapshot = false;
			Order = 0;
		}
	}
}
