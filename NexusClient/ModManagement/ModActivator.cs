using System;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Activates mods.
	/// </summary>
	public class ModActivator
	{
		#region Properties

		/// <summary>
		/// Gets the install log tracking mod activations for the current game mode.
		/// </summary>
		/// <value>The install log tracking mod activations for the current game mode.</value>
		protected IInstallLog InstallationLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModInstallerFactory"/> to use to create
		/// <see cref="ModInstaller"/>s.
		/// </summary>
		/// <value>The <see cref="ModInstallerFactory"/> to use to create
		/// <see cref="ModInstaller"/>s.</value>
		protected ModInstallerFactory InstallerFactory { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_ilgInstallLog">The install log to use to log file installations.</param>
		/// <param name="p_mifInstallerFactory">The factory to use to create mod installers.</param>
		public ModActivator(IInstallLog p_ilgInstallLog, ModInstallerFactory p_mifInstallerFactory)
		{
			InstallationLog = p_ilgInstallLog;
			InstallerFactory = p_mifInstallerFactory;
		}

		#endregion

		/// <summary>
		/// Activates the given mod.
		/// </summary>
		/// <remarks>
		/// The given mod is either installed or upgraded, as appropriate.
		/// </remarks>
		/// <param name="p_modMod">The mod to install.</param>
		/// <param name="p_dlgUpgradeConfirmationDelegate">The delegate that is called to confirm whether an upgrade install should be performed.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet Activate(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods, bool p_booOverrideUpgrade)
		{
			ModMatcher mmcMatcher = new ModMatcher(InstallationLog.ActiveMods, true);
			IMod modOldVersion = mmcMatcher.FindAlternateVersion(p_modMod, true);
			ConfirmUpgradeResult curAction = ConfirmUpgradeResult.NormalActivation;

			if (!p_booOverrideUpgrade)
				curAction = (modOldVersion == null) ? ConfirmUpgradeResult.NormalActivation : p_dlgUpgradeConfirmationDelegate(modOldVersion, p_modMod);

			switch (curAction)
			{
				case ConfirmUpgradeResult.Upgrade:
					ModInstaller muiUpgrader = InstallerFactory.CreateUpgradeInstaller(modOldVersion, p_modMod, p_dlgOverwriteConfirmationDelegate);
					return muiUpgrader;
				case ConfirmUpgradeResult.NormalActivation:
					ModInstaller minInstaller = InstallerFactory.CreateInstaller(p_modMod, p_dlgOverwriteConfirmationDelegate, p_rolActiveMods);
					return minInstaller;
				case ConfirmUpgradeResult.Cancel:
					return null;
				default:
					throw new Exception(String.Format("Unrecognized value for ConfirmUpgradeResult: {0}", curAction));
			}
		}

		/// <summary>
		/// Forces an upgrade from one mod to another.
		/// </summary>
		/// <remarks>
		/// No checks as to whether the two mods are actually related are performed. The new mod is reactivated
		/// as if it were the old mod, and the old mod is replaced by the new mod.
		/// </remarks>
		/// <param name="p_modOldMod">The mod from which to upgrade.</param>
		/// <param name="p_modNewMod">The mod to which to upgrade.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <exception cref="InvalidOperationException">Thrown if <paramref name="p_modNewMod"/> is already active.</exception>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ForceUpgrade(IMod p_modOldMod, IMod p_modNewMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			if (InstallationLog.ActiveMods.Contains(p_modNewMod))
				throw new InvalidOperationException(String.Format("Cannot upgrade to a mod that is already active. (Trying to upgrade {0} {1} to {2} {3})", p_modOldMod.ModName, p_modOldMod.HumanReadableVersion, p_modNewMod.ModName, p_modNewMod.HumanReadableVersion));
			ModInstaller muiUpgrader = InstallerFactory.CreateUpgradeInstaller(p_modOldMod, p_modNewMod, p_dlgOverwriteConfirmationDelegate);
			muiUpgrader.Install();
			return muiUpgrader;
		}

		/// <summary>
		/// Reactivates the given mod.
		/// </summary>
		/// <remarks>
		/// A reactivation is an upgrade of a mod to itself. It re-runs the activation,
		/// without changing the installed precedence of its files and installed values.
		/// </remarks>
		/// <param name="p_modMod">The mod to reactivate.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet Reactivate(IMod p_modMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			ModInstaller muiUpgrader = InstallerFactory.CreateUpgradeInstaller(p_modMod, p_modMod, p_dlgOverwriteConfirmationDelegate);
			muiUpgrader.Install();
			return muiUpgrader;
		}
	}
}
