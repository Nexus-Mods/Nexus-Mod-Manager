using System;

namespace Nexus.Client.ModManagement
{
	public interface IBackupInfo : IEquatable<IBackupInfo>
	{
		string VirtualModPath { get; set; }
		string RealModPath { get; set; }
		string ModID { get; set; }
	}
}
