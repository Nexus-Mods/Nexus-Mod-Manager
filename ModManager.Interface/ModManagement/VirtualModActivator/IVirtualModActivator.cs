using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	public interface IVirtualModActivator
	{
		event EventHandler ModActivationChanged;

		#region Properties
		bool UseHardLinks { get; }
		bool Initialized { get; }
		bool DisableLinkCreation { get; }
		bool DisableIniLogging { get; }
		string VirtualPath { get; }
		ThreadSafeObservableList<IVirtualModLink> VirtualLinks { get; }
		IEnumerable<string> ActiveModList { get; }
		Int32 ModCount { get; }
		#endregion

		#region Virtual Mod Activator
		void Initialize();
		void Setup();
		void SaveList();
		void SetCurrentList(IList<IVirtualModLink> p_ilvVirtualLinks);
		List<IVirtualModLink> LoadList(string p_strXMLFilePath);
		List<IVirtualModLink> LoadImportedList(string p_strXML);
		void SaveModListAt(string p_strPath);
		string CheckVirtualLink(string p_strFilePath);
		Int32 CheckFileLink(string p_strFilePath, out IMod p_modMod, out List<IVirtualModLink> lstFileLinks);
		bool PurgeLinks();
		void AddInactiveLink(IMod p_modMod, string p_strBaseFilePath, Int32 p_intPriority);
		string AddFileLink(IMod p_modMod, string p_strBaseFilePath, bool p_booIsSwitching, bool p_booIsRestoring, Int32 p_intPriority);
		void RemoveFileLink(string p_strFilePath, IMod p_modMod);
		void RemoveFileLink(IVirtualModLink p_ivlVirtualLink, IMod p_modMod);
		void UpdateLinkPriority(List<IVirtualModLink> lstFileLinks);
		void DisableMod(IMod p_modMod);
		void EnableMod(IMod p_modMod);
		void LogIniEdits(IMod p_modMod, string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue);
		Dictionary<string, string> CheckLinkListIntegrity(IList<IVirtualModLink> p_ivlVirtualLinks);
		IModLinkInstaller GetModLinkInstaller();
		#endregion
	}
}
