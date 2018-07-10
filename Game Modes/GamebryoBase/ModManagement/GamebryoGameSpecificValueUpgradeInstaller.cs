using System;
using System.Collections.Generic;
using System.Linq;
using ChinhDo.Transactions;
using Nexus.Client.Games.Gamebryo.Tools.Shader;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games.Gamebryo.ModManagement
{
	/// <summary>
	/// Installs values that are specific to a game mode when a mod is being upgraded.
	/// </summary>
	/// <remarks>
	/// This differs from the regular <see cref="GamebryoGameSpecificValueInstaller"/>
	/// in that installed edits are installed overtop of their current location. Edits
	/// are only made to game specific values if the mod being upgraded was the latest
	/// mod to edit the value in question. If the edit was not last made by the mod
	/// being upgraded, the edit is simply archived in the install log, to be used as
	/// required in future uninstallation. If an edit being installed has not been
	/// previously installed, it is installed as usual.
	/// </remarks>
	public class GamebryoGameSpecificValueUpgradeInstaller : GamebryoGameSpecificValueInstaller
	{
		#region Properties

		/// <summary>
		/// Gets the list of game specific value edits that were already installed by the
		/// current mod before the upgrade, but not yet reinstalled during the upgrade.
		/// </summary>
		protected Set<string> OriginallyInstalledEdits { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmiGameModeInfo">The environment info of the current game mode.</param>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log file installations.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public GamebryoGameSpecificValueUpgradeInstaller(IMod p_modMod, IGameModeEnvironmentInfo p_gmiGameModeInfo, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
			:base(p_modMod, p_gmiGameModeInfo, p_ilgInstallLog, p_tfmFileManager, p_futFileUtility, p_dlgOverwriteConfirmationDelegate)
		{
			OriginallyInstalledEdits = new Set<string>();
			OriginallyInstalledEdits.AddRange(InstallLog.GetInstalledGameSpecificValueEdits(Mod));
		}

		#endregion

		/// <summary>
		/// Edits the specified game specific value.
		/// </summary>
		/// <remarks>
		/// This method writes the given value in the specified game specific value, if it
		/// is owned by the mod being upgraded. If the specified edit is not owned by the
		/// mod being upgraded, the edit is archived in the install log.
		/// 
		/// If the edit was not previously installed by the mod, then the normal install
		/// rules apply, including confirming overwrite if applicable.
		/// </remarks>
		/// <param name="p_strKey">The key of the edited Game Specific Value.</param>
		/// <param name="p_bteValue">The value to install.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c> otherwise.</returns>
		public override bool EditGameSpecificValue(string p_strKey, byte[] p_bteValue)
		{
			IList<IMod> lstInstallers = InstallLog.GetGameSpecificValueEditInstallers(p_strKey);
			if (lstInstallers.Contains(Mod, ModComparer.Filename))
			{
				if (!ModComparer.Filename.Equals(lstInstallers[lstInstallers.Count - 1], Mod))
					InstallLog.ReplaceGameSpecificValueEdit(Mod, p_strKey, p_bteValue);
				else
				{
					ShaderEdit sedShader = new ShaderEdit(p_strKey);
					SDPArchives sdpManager = new SDPArchives(GameModeInfo, FileUtility);
					if (!TouchedFiles.Contains(sdpManager.GetPath(sedShader.Package)))
					{
						TouchedFiles.Add(sdpManager.GetPath(sedShader.Package));
						TransactionalFileManager.Snapshot(sdpManager.GetPath(sedShader.Package));
					}
					byte[] oldData;
					if (!sdpManager.EditShader(sedShader.Package, sedShader.ShaderName, p_bteValue, out oldData))
						throw new Exception("Failed to edit the shader");
				}
				OriginallyInstalledEdits.Remove(p_strKey);
				return true;
			}

			return base.EditGameSpecificValue(p_strKey, p_bteValue);
		}

		/// <summary>
		/// Finalizes the installation of the values.
		/// </summary>
		/// <remarks>
		/// This removes all of the file that weren't reinstalled during the upgrade.
		/// </remarks>
		public override void FinalizeInstall()
		{
			foreach (string strEdit in OriginallyInstalledEdits)
				UnEditGameSpecificValue(strEdit);
		}
	}
}
