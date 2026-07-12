namespace Nexus.Client.ModManagement
{
	using System;

	internal interface IVirtualModStoreFactory
	{
		IVirtualModStore Create(string xmlFilePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion);
	}
}
