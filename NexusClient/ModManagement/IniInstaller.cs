using System;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// This installs INI value changes.
	/// </summary>
	public class IniInstaller : IIniInstaller
	{
		private bool m_booDontOverwriteAllIni = false;
		private bool m_booOverwriteAllIni = false;
		private ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmationDelegate = null;

		#region Properties

		/// <summary>
		/// Gets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected IMod Mod { get; private set; }

		/// <summary>
		/// Gets the install log to use to log file installations.
		/// </summary>
		/// <value>The install log to use to log file installations.</value>
		protected IInstallLog InstallLog { get; private set; }

		/// <summary>
		/// Gets the transactional file manager to use to interact with the file system.
		/// </summary>
		/// <value>The transactional file manager to use to interact with the file system.</value>
		protected TxFileManager TransactionalFileManager { get; private set; }

		/// <summary>
		/// Gets the virtual mod activator to use.
		/// </summary>
		/// <value>The virtual mod activator to use.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets the set of Ini files that have been edited.
		/// </summary>
		protected Set<string> TouchedFiles { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log file installations.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public IniInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, IVirtualModActivator p_ivaVirtualModActivator, TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			TouchedFiles = new Set<string>(StringComparer.OrdinalIgnoreCase);
			Mod = p_modMod;
			InstallLog = p_ilgInstallLog;
			VirtualModActivator = p_ivaVirtualModActivator;
			TransactionalFileManager = p_tfmFileManager;
			m_dlgOverwriteConfirmationDelegate = p_dlgOverwriteConfirmationDelegate ?? ((s, b, m) => OverwriteResult.No);
		}

		#endregion

		#region Ini Management

		#region Ini File Value Retrieval

		/// <summary>
		/// Retrieves the specified settings value as a string.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file from which to retrieve the value.</param>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		public string GetIniString(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			return IniMethods.GetPrivateProfileString(p_strSection, p_strKey, null, p_strSettingsFileName);
		}

		/// <summary>
		/// Retrieves the specified settings value as an integer.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file from which to retrieve the value.</param>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as an integer.</returns>
		public Int32 GetIniInt(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			return IniMethods.GetPrivateProfileInt32(p_strSection, p_strKey, 0, p_strSettingsFileName);
		}

		#endregion

		#region Ini Editing

		/// <summary>
		/// Sets the specified value in the specified Ini file to the given value.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file to edit.</param>
		/// <param name="p_strSection">The section in the Ini file to edit.</param>
		/// <param name="p_strKey">The key in the Ini file to edit.</param>
		/// <param name="p_strValue">The value to which to set the key.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c>
		/// if the user chose not to overwrite the existing value.</returns>
		public virtual bool EditIni(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			if (m_booDontOverwriteAllIni)
				return false;

			if (!TouchedFiles.Contains(p_strSettingsFileName))
			{
				TouchedFiles.Add(p_strSettingsFileName);
				TransactionalFileManager.Snapshot(p_strSettingsFileName);
			}

			IMod modOldMod = InstallLog.GetCurrentIniEditOwner(p_strSettingsFileName, p_strSection, p_strKey);
			string strOldValue = IniMethods.GetPrivateProfileString(p_strSection, p_strKey, null, p_strSettingsFileName);
			if (!m_booOverwriteAllIni)
			{
				string strMessage = null;
				if (modOldMod != null)
				{
					strMessage = String.Format("Key '{{0}}' in section '{{1}}' of {{2}} has already been overwritten by '{0}'\n" +
									"Overwrite again with this mod?\n" +
									"Current value '{{3}}', new value '{{4}}'", modOldMod.ModName);
				}
				else
				{
					strMessage = "The mod wants to modify key '{0}' in section '{1}' of {2}.\n" +
									"Allow the change?\n" +
									"Current value '{3}', new value '{4}'";
				}
				switch (m_dlgOverwriteConfirmationDelegate(String.Format(strMessage, p_strKey, p_strSection, p_strSettingsFileName, strOldValue, p_strValue), false, false))
				{
					case OverwriteResult.YesToAll:
						m_booOverwriteAllIni = true;
						break;
					case OverwriteResult.NoToAll:
						m_booDontOverwriteAllIni = true;
						break;
					case OverwriteResult.Yes:
						break;
					default:
						return false;
				}
			}

			//if we are overwriting an original value, back it up
			if ((modOldMod == null) && (strOldValue != null))
				InstallLog.LogOriginalIniValue(p_strSettingsFileName, p_strSection, p_strKey, strOldValue);

			IniMethods.WritePrivateProfileString(p_strSection, p_strKey, p_strValue, p_strSettingsFileName);
			InstallLog.AddIniEdit(Mod, p_strSettingsFileName, p_strSection, p_strKey, p_strValue);

			if (VirtualModActivator != null)
				VirtualModActivator.LogIniEdits(Mod, p_strSettingsFileName, p_strSection, p_strKey, p_strValue);

			return true;
		}

		#endregion

		#region Ini Unediting

		/// <summary>
		/// Undoes the edit made to the spcified key.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file to unedit.</param>
		/// <param name="p_strSection">The section in the Ini file to unedit.</param>
		/// <param name="p_strKey">The key in the Ini file to unedit.</param>
		public void UneditIni(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			string strKey = InstallLog.GetModKey(Mod);
			string strCurrentOwnerKey = InstallLog.GetCurrentIniEditOwnerKey(p_strSettingsFileName, p_strSection, p_strKey);
			//if we didn't edit the value, then leave it alone
			if (!strKey.Equals(strCurrentOwnerKey))
				return;

			if (!TouchedFiles.Contains(p_strSettingsFileName))
			{
				TouchedFiles.Add(p_strSettingsFileName);
				TransactionalFileManager.Snapshot(p_strSettingsFileName);
			}

			//if we did edit the value, replace if with the value we overwrote.
			// if we didn't overwrite a value, then just delete it.
			// writing null effectively deletes the value, so if
			// strPreviousValue is null, indicating we didn't overwrite a value,
			// we still write it
			string strPreviousValue = InstallLog.GetPreviousIniValue(p_strSettingsFileName, p_strSection, p_strKey);
			IniMethods.WritePrivateProfileString(p_strSection, p_strKey, strPreviousValue, p_strSettingsFileName);

			InstallLog.RemoveIniEdit(Mod, p_strSettingsFileName, p_strSection, p_strKey);
		}

		#endregion

		#endregion

		/// <summary>
		/// Finalizes the installation of the values.
		/// </summary>
		public virtual void FinalizeInstall()
		{
		}
	}
}
