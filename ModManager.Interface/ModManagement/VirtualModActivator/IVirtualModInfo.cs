using System;

namespace Nexus.Client.ModManagement
{
	public interface IVirtualModInfo : IEquatable<IVirtualModInfo>
	{
		#region Properties

		string ModId { get; }
		string ModName { get; }
		string ModFileName { get; }
		string ModFilePath { get; }
		string ModFileFullPath { get; }

		#endregion
	}
}
