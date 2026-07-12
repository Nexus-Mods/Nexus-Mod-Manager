namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;

	internal interface IVirtualModStore
	{
		bool IsReadyForWrite(string filePath);

		void Save(Version fileVersion, string filePath, IList<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink);

		void SaveWithModInfoMatching(Version fileVersion, string filePath, IList<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink);

		VirtualModStoreData Load(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion);

		bool TryLoad(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion, out VirtualModStoreData data);

		void Copy(string sourcePath, string destinationPath);
	}

	internal sealed class VirtualModStoreData
	{
		public VirtualModStoreData(List<IVirtualModInfo> virtualMods, List<IVirtualModLink> virtualLinks)
		{
			VirtualMods = virtualMods;
			VirtualLinks = virtualLinks;
		}

		public List<IVirtualModInfo> VirtualMods { get; private set; }

		public List<IVirtualModLink> VirtualLinks { get; private set; }
	}
}
