﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	public interface IVirtualModActivator
	{
		event EventHandler ModActivationChanged;

		#region Properties
		bool MultiHDMode { get; }
		bool Initialized { get; }
		bool DisableLinkCreation { get; }
		bool DisableIniLogging { get; }
		string VirtualPath { get; }
		string HDLinkFolder { get; }
		ThreadSafeObservableList<IVirtualModLink> VirtualLinks { get; }
		ThreadSafeObservableList<IVirtualModInfo> VirtualMods { get; }
		IEnumerable<string> ActiveModList { get; }
		Int32 ModCount { get; }
		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		IGameMode GameMode { get; }
		#endregion

		#region Virtual Mod Activator
		void Initialize();
		void Setup();
		void Reset();
		void SaveList();
		void SetCurrentList(IList<IVirtualModLink> p_ilvVirtualLinks);
		List<IVirtualModLink> LoadList(string p_strXMLFilePath);
		List<IVirtualModLink> LoadImportedList(string p_strXML);
		bool LoadListOnDemand(string p_strProfilePath, out List<IVirtualModLink> p_lstVirtualLinks, out List<IVirtualModInfo> p_lstVirtualMods);
		void SaveModList(string p_strPath);
		string CheckVirtualLink(string p_strFilePath);
		Int32 CheckFileLink(string p_strFilePath, out IMod p_modMod, out List<IVirtualModLink> lstFileLinks);
		bool PurgeLinks();
		void AddInactiveLink(IMod p_modMod, string p_strBaseFilePath, Int32 p_intPriority);
		string AddFileLink(IMod p_modMod, string p_strBaseFilePath, bool p_booIsSwitching, bool p_booIsRestoring, Int32 p_intPriority);
		string AddFileLink(IMod p_modMod, string p_strBaseFilePath, bool p_booIsSwitching, bool p_booIsRestoring, bool p_booHandlePlugin, Int32 p_intPriority);
		void RemoveFileLink(string p_strFilePath, IMod p_modMod);
		void RemoveFileLink(IVirtualModLink p_ivlVirtualLink, IMod p_modMod);
		void UpdateLinkPriority(List<IVirtualModLink> lstFileLinks);
		void DisableMod(IMod p_modMod);
		void DisableModFiles(IMod p_modMod);
		void FinalizeModDeactivation(IMod p_modMod);
		void EnableMod(IMod p_modMod);
		void FinalizeModActivation(IMod p_modMod);
		void LogIniEdits(IMod p_modMod, string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue);
		void RestoreIniEdits();
		void PurgeIniEdits();
		void ImportIniEdits(string p_strIniXML);
		void SetNewFolders(string p_strVirtual, string p_strLink, bool? p_booMultiHD);
		void CheckLinkListIntegrity(IList<IVirtualModLink> p_ivlVirtualLinks, out Dictionary<string, string> p_dicUninstalled, out Dictionary<string, string> p_dicMissing, IList<string> p_lstForced);
		IModLinkInstaller GetModLinkInstaller();
		void PurgeMods(List<IMod> p_lstMods, string p_strPath);
		bool CheckHasActiveLinks(IMod p_modMod);
		IBackgroundTask ActivatingMod(IMod p_modMod, bool p_booDisabling, ConfirmActionMethod p_camConfirm);
		#endregion
	}
}
