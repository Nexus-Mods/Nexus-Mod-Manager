using System;

namespace Nexus.Client.ModManagement
{
	public interface IVirtualModInfo : IEquatable<IVirtualModInfo>
	{
		#region Properties

		string ModId { get; }
		string ModName { get; }
		string DownloadId { get; }
		string UpdatedDownloadId { get; }
		string ModFileName { get; }
		string NewFileName { get; }
		string ModFilePath { get; }
		string ModFileFullPath { get; }
		string FileVersion { get; }

		#endregion
	}
}
