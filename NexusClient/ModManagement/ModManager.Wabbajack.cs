using System;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.ModManagement
{
	public partial class ModManager
	{
		/// <summary>Queues a raw Wabbajack list or source file in the existing Download Manager.</summary>
		internal IBackgroundTask QueueWabbajackDownload(Uri source, string destination, string hash, long size)
		{
			return ModAdditionQueue.AddWabbajackDownload(source, destination, hash, size);
		}
	}
}
