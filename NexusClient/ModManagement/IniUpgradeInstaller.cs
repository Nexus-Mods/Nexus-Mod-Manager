using System.Collections.Generic;
using System.Linq;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// This installs Ini edits when a mod is being upgraded.
	/// </summary>
	/// <remarks>
	/// This differs from the regular <see cref="IniInstaller"/> in that
	/// installed edits are installed overtop of their current location. Edits
	/// are only made to Ini files if the mod being upgraded was the latest mod
	/// to edit the value in question. If the edit was not last made by the mod
	/// being upgraded, the edit is simply archived in the install log, to
	/// be used as required in future uninstallation. If an edit being installed
	/// has not been previously installed, it is installed as usual.
	/// </remarks>
	public class IniUpgradeInstaller : IniInstaller
	{
		#region Properties

		/// <summary>
		/// Gets the list of Ini edits that were already installed by the current mod
		/// before the upgrade, but not yet reinstalled during the upgrade.
		/// </summary>
		protected Set<IniEdit> OriginallyInstalledEdits { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log file installations.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public IniUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
			: base(p_modMod, p_ilgInstallLog, p_tfmFileManager, p_dlgOverwriteConfirmationDelegate)
		{
			OriginallyInstalledEdits = new Set<IniEdit>();
			OriginallyInstalledEdits.AddRange(InstallLog.GetInstalledIniEdits(Mod));
		}

		#endregion

		/// <summary>
		/// Sets the specified value in the specified Ini file to the given value.
		/// </summary>
		/// <remarks>
		/// This method writes the given value in the specified Ini value, if it is owned
		/// by the mod being upgraded. If the specified Ini edit is not owned by the mod
		/// being upgraded, the Ini edit is archived in the install log.
		/// 
		/// If the Ini edit was not previously installed by the mod, then the normal install
		/// rules apply, including confirming overwrite if applicable.
		/// </remarks>
		/// <param name="p_strSettingsFileName">The name of the settings file to edit.</param>
		/// <param name="p_strSection">The section in the Ini file to edit.</param>
		/// <param name="p_strKey">The key in the Ini file to edit.</param>
		/// <param name="p_strValue">The value to which to set the key.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c>
		/// if the user chose not to overwrite the existing value.</returns>
		public override bool EditIni(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			IList<IMod> lstInstallers = InstallLog.GetIniEditInstallers(p_strSettingsFileName, p_strSection, p_strKey);
			if (lstInstallers.Contains(Mod, ModComparer.Filename))
			{
				if (!ModComparer.Filename.Equals(lstInstallers[lstInstallers.Count - 1], Mod))
					InstallLog.ReplaceIniEdit(Mod, p_strSettingsFileName, p_strSection, p_strKey, p_strValue);
				else
				{
					if (!TouchedFiles.Contains(p_strSettingsFileName))
					{
						TouchedFiles.Add(p_strSettingsFileName);
						TransactionalFileManager.Snapshot(p_strSettingsFileName);
					}
					IniMethods.WritePrivateProfileString(p_strSection, p_strKey, p_strValue, p_strSettingsFileName);
				}
				IniEdit iniEdit = new IniEdit(p_strSettingsFileName, p_strSection, p_strKey);
				OriginallyInstalledEdits.Remove(iniEdit);
				return true;
			}

			return base.EditIni(p_strSettingsFileName, p_strSection, p_strKey, p_strValue);
		}

		/// <summary>
		/// Finalizes the installation of the values.
		/// </summary>
		/// <remarks>
		/// This removes all of the file that weren't reinstalled during the upgrade.
		/// </remarks>
		public override void FinalizeInstall()
		{
			foreach (IniEdit iniEdit in OriginallyInstalledEdits)
				UneditIni(iniEdit.File, iniEdit.Section, iniEdit.Key);
		}
	}
}
