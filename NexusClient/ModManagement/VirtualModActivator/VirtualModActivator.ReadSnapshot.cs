using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModActivator
	{
		/// <inheritdoc />
		public VirtualModReadSnapshot GetReadSnapshot()
		{
			IVirtualModLink[] links = VirtualLinks == null ? new IVirtualModLink[0] : VirtualLinks.ToArray();
			Dictionary<IVirtualModInfo, IMod> managedMods = BuildManagedModLookupForVirtualLinks(links);
			var result = new List<VirtualModReadLink>(links.Length);

			foreach (IVirtualModLink link in links)
			{
				if (link == null || String.IsNullOrWhiteSpace(link.VirtualModPath))
					continue;

				IMod mod = null;
				if (link.ModInfo != null)
					managedMods.TryGetValue(link.ModInfo, out mod);

				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(GameMode, mod, link.VirtualModPath, link.InstallRoot);

				string ownerKey = mod == null ? null : ModInstallLog.GetModKey(mod);
				string ownerReference = link.ModInfo == null
					? String.Empty
					: String.Join("|", new[] { link.ModInfo.ModFileName, link.ModInfo.DownloadId, link.ModInfo.UpdatedDownloadId, link.ModInfo.ModFileFullPath });
				string resolvedPayloadSourcePath = String.Empty;
				if (link.Active)
				{
					try
					{
						resolvedPayloadSourcePath = ResolveVirtualSourcePath(link, target) ?? String.Empty;
					}
					catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
						exception is System.IO.IOException || exception is NotSupportedException)
					{
						resolvedPayloadSourcePath = String.Empty;
					}
				}
				result.Add(new VirtualModReadLink(target, ownerKey, ownerReference, link.Active, link.Priority,
					link.RealModPath, resolvedPayloadSourcePath));
			}

			return new VirtualModReadSnapshot(result);
		}
	}
}
