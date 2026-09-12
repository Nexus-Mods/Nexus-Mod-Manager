using System;
using System.Threading;
using ChinhDo.Transactions;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Installs a mod as an upgrade.
	/// </summary>
	/// <remarks>
	/// An upgrade installs the mod without changing the owners of installed
	/// files and other items.
	/// </remarks>
	public class ModUpgrader : ModInstaller
	{
		#region Properties

		/// <summary>
		/// Gets the mod that is being upgraded.
		/// </summary>
		/// <value>The mod that is being upgraded.</value>
		protected IMod OldMod { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modOldMod">The mod from which to upgrade.</param>
		/// <param name="p_modNewMod">The mod to which to upgrade.</param>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgModInstallLog">The install log that tracks mod install info
		/// for the current game mode</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public ModUpgrader(IMod p_modOldMod, IMod p_modNewMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, IModDeploymentManager p_mdmDeploymentManager, IProfileManager p_ipmProfileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ModInstallContext p_micInstallContext)
			: base(p_modNewMod, p_gmdGameMode, p_eifEnvironmentInfo, p_futFileUtility, p_scxUIContext, p_ilgModInstallLog, p_pmgPluginManager, p_ivaVirtualModActivator, p_mdmDeploymentManager, p_ipmProfileManager, p_dlgOverwriteConfirmationDelegate, null, p_micInstallContext)
		{
			OldMod = p_modOldMod ?? throw new ArgumentNullException(nameof(p_modOldMod));
		}

		#endregion

		#region Installer Creation

		/// <summary>
		/// Creates the file installer to use to install the mod's files.
		/// </summary>
		/// <remarks>
		/// This returns the upgrade <see cref="ModFileUpgradeInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected override IModFileInstaller CreateFileInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			if (InstallContext.Method == ModInstallMethod.Direct)
			{
				return new DirectModFileInstaller(Mod, OldMod, GameMode, ModInstallLog, DeploymentManager, PluginManager,
					p_tfmFileManager, p_dlgOverwriteConfirmationDelegate, EnvironmentInfo, InstallContext, true);
			}

			string installBasePath = InstallContext.InstallRoot == ModInstallRoot.GameRoot
				? GameMode.InstallationPath
				: GameMode.GameModeEnvironmentInfo.InstallationPath;

			return new ModFileUpgradeInstaller(GameMode.GameModeEnvironmentInfo, Mod, ModInstallLog, PluginManager,
				new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath), p_tfmFileManager,
				p_dlgOverwriteConfirmationDelegate, GameMode.UsesPlugins, EnvironmentInfo, installBasePath);
		}

		/// <summary>
		/// Creates the file installer to use to install the mod's ini edits.
		/// </summary>
		/// <remarks>
		/// This returns the upgrade <see cref="IniUpgradeInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected override IIniInstaller CreateIniInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return new IniUpgradeInstaller(Mod, ModInstallLog, VirtualModActivator, p_tfmFileManager, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Creates the file installer to use to install the mod's game specific value edits.
		/// </summary>
		/// <remarks>
		/// This returns an upgrade <see cref="IGameSpecificValueInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected override IGameSpecificValueInstaller CreateGameSpecificValueInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return GameMode.GetGameSpecificValueUpgradeInstaller(Mod, ModInstallLog, p_tfmFileManager, new NexusFileUtil(EnvironmentInfo), p_dlgOverwriteConfirmationDelegate);
		}

		#endregion

		/// <summary>
		/// Registers the mod being upgraded with the install log.
		/// </summary>
		protected override void RegisterMod()
		{
			ModInstallLog.ReplaceActiveMod(OldMod, Mod, InstallContext.InstallRoot, InstallContext.Method);
		}

		/// <summary>
		/// Removes promoted Virtual ownership entries for files that belonged to the old version but were not
		/// reinstalled by the replacement version. Reinstalled targets already point at the new staged payload.
		/// </summary>
		protected override void FinalizeDeploymentAfterInstall(TxFileManager p_tfmFileManager)
		{
			if (InstallContext.Method != ModInstallMethod.Virtual || DeploymentManager == null || !DeploymentManager.HasPromotedTargets)
				return;

			var deploymentBackend = VirtualModActivator as Nexus.Client.ModManagement.VirtualModActivator;
			if (deploymentBackend == null)
				throw new InvalidOperationException("Promoted Virtual upgrades require the transaction-aware VMA deployment backend.");

			foreach (ModDeploymentTarget target in deploymentBackend.GetStalePromotedVirtualTargetsForUpgrade(OldMod))
				DeploymentManager.RemoveOwnedTarget(Mod, target, p_tfmFileManager);
		}
	}
}
	