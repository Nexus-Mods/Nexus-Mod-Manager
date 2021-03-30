using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using Nexus.Client.ModManagement.InstallationLog;
using ChinhDo.Transactions;

namespace Nexus.Client.ModManagement
{
	public class ProfileSwitchSetupTask : ThreadedBackgroundTask
	{
		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected VirtualModActivator VirtualModActivator { get; private set; }

		private IInstallLog _installLog = null;
		private ModInstallerFactory _modInstallerFactory = null;
		private ReadOnlyObservableList<IMod> _modsToDeactivate = null;
		private string _logPath = string.Empty;
		private bool _filesOnly = false;
		private List<IMod> _modsToInstall = null;
		private ConfirmItemOverwriteDelegate _overwriteConfirmationDelegate = null;
		private ConfirmActionMethod _confirmMethod = null;
		private IModProfile _profileToInstall = null;
		private IModProfile _profileToSwitch = null;
		private IProfileManager _profileManager = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public ProfileSwitchSetupTask(ReadOnlyObservableList<IMod> modsToDeactivate, List<IMod> modsToInstall, IProfileManager profileManager, IModProfile profileToInstall,
			IModProfile profileToSwitch, IInstallLog installLog, ModInstallerFactory modInstallerFactory, VirtualModActivator virtualModActivator, string scriptedLogPath, bool filesOnly,
			ConfirmActionMethod	confirmActionMethod, ConfirmItemOverwriteDelegate overwriteConfirmationDelegate)
		{
			_installLog = installLog;
			_modInstallerFactory = modInstallerFactory;
			_modsToDeactivate = modsToDeactivate;
			VirtualModActivator = virtualModActivator;
			_logPath = scriptedLogPath;
			_filesOnly = filesOnly;
			_profileToInstall = profileToInstall;
			_profileToSwitch = profileToSwitch;
			_profileManager = profileManager;
			_modsToInstall = modsToInstall;
			_confirmMethod = confirmActionMethod;
			_overwriteConfirmationDelegate = overwriteConfirmationDelegate;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			base.OnTaskEnded(e);
		}
		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod p_camConfirm)
		{
			Start(p_camConfirm);
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			//base.Cancel();
			//m_booCancel = true;
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			if (_modsToDeactivate != null && _modsToDeactivate.Count > 0)
				DeactivateMods(args);

			if (_modsToInstall != null && _modsToInstall.Count > 0)
				InstallMods(args);

			return null;
		}

		private bool DeactivateMods(object[] args)
		{
			OverallMessage = "Profile Switch Setup: Uninstalling selected active mods...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = _modsToDeactivate.Count;
			ShowItemProgress = true;
			ItemProgressStepSize = 1;
			ItemProgressMaximum = 4;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];

			foreach (IMod modMod in _modsToDeactivate)
			{
				OverallMessage = "Uninstalling: " + modMod.ModName;
				ItemProgress = 0;

				if (ItemProgress < ItemProgressMaximum)
				{
					ItemMessage = "Disabling: " + modMod.ModName;
					StepItemProgress();
				}

				if ((VirtualModActivator != null) && (VirtualModActivator.ModCount > 0))
				{
					if (_filesOnly)
						VirtualModActivator.DisableModFiles(modMod);
					else
						VirtualModActivator.DisableMod(modMod);
				}

				if (ItemProgress < ItemProgressMaximum)
				{
					ItemMessage = "Setting up uninstall: " + modMod.ModName;
					StepItemProgress();
				}

				modMod.InstallDate = null;
				if (!_installLog.ActiveMods.Contains(modMod))
					continue;
				ModUninstaller munUninstaller = _modInstallerFactory.CreateUninstaller(modMod, _modsToDeactivate);
				munUninstaller.Install();

				if (ItemProgress < ItemProgressMaximum)
				{
					ItemMessage = "Uninstalling: " + modMod.ModName;
					StepItemProgress();
				}

				while (!munUninstaller.IsCompleted)
				{ }

				if (ItemProgress < ItemProgressMaximum)
				{
					ItemMessage = "Removing XML logs: " + modMod.ModName;
					StepItemProgress();
				}

				DeleteXMLInstalledFile(modMod);

				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
			}

			return true;
		}

		private bool InstallMods(object[] args)
		{
			int modCounter = 0;
			if (_modsToInstall != null && _modsToInstall.Count > 0)
				modCounter = _modsToInstall.Count;

			OverallMessage = string.Format("Profile Switch Setup: Installing selected mods ({0})...", modCounter);
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = _modsToInstall.Count;
			ShowItemProgress = false;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];

			if (_profileToInstall != null && _profileManager != null)
				_profileManager.SetCurrentProfile(_profileToInstall);


			foreach (IMod modMod in _modsToInstall)
			{
				OverallMessage = "Profile Switch Setup: Installing selected mods: " + modMod.ModName;

				if (_installLog.ActiveMods.Contains(modMod))
					continue;

				ModInstaller minInstaller = _modInstallerFactory.CreateInstaller(modMod, _overwriteConfirmationDelegate, null);
				minInstaller.Install();

				while (!minInstaller.IsCompleted)
				{ }
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
			}

			if (_profileToSwitch != null && _profileManager != null)
				_profileManager.SetCurrentProfile(_profileToSwitch);

			return true;
		}

		/// <summary>
		/// If the mod is scripted, deletes the XMLInstalledFiles file inside the InstallInfo\Scripted folder.
		/// </summary>
		private void DeleteXMLInstalledFile(IMod p_modMod)
		{
			string strInstallFilesPath = Path.Combine(_logPath, "Scripted", Path.GetFileNameWithoutExtension(p_modMod.Filename)) + ".xml";
			if (File.Exists(strInstallFilesPath))
				FileUtil.ForceDelete(strInstallFilesPath);
		}
	}
}
