using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting.XmlScript.UI;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// Executes an XML script.
	/// </summary>
	public class XmlScriptExecutor : ScriptExecutorBase
	{
		private SynchronizationContext m_scxSyncContext = null;
		private IVirtualModActivator m_ivaVirtualModActivator = null;
				
		#region Properties

		/// <summary>
		/// Gets the mod for which the script is running.
		/// </summary>
		/// <value>The mod for which the script is running.</value>
		protected IMod Mod { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the installer group to use to install mod items.
		/// </summary>
		/// <value>The installer group to use to install mod items.</value>
		protected InstallerGroup Installers { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the required dependencies.
		/// </summary>
		/// <param name="p_modMod">The mod for which the script is running.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>		
		public XmlScriptExecutor(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, SynchronizationContext p_scxUIContext)
		{
			m_scxSyncContext = p_scxUIContext;
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			Installers = p_igpInstallers;
			m_ivaVirtualModActivator = p_ivaVirtualModActivator;
		}

		#endregion
		
		#region ScriptExecutorBase Members

		/// <summary>
		/// Executes the script.
		/// </summary>
		/// <param name="p_scpScript">The XMl Script to execute.</param>
		/// <returns><c>true</c> if the script completes successfully;
		/// <c>false</c> otherwise.</returns>
		/// <exception cref="ArgumentException">Thrown if <paramref name="p_scpScript"/> is not an
		/// <see cref="XmlScript"/>.</exception>
		public override bool DoExecute(IScript p_scpScript)
		{
			if (!(p_scpScript is XmlScript))
				throw new ArgumentException("The given script must be of type XmlScript.", "p_scpScript");

			XmlScript xscScript = (XmlScript)p_scpScript;

			ConditionStateManager csmStateManager = ((XmlScriptType)xscScript.Type).CreateConditionStateManager(Mod, GameMode, Installers.PluginManager, EnvironmentInfo);
			if ((xscScript.ModPrerequisites != null) && !xscScript.ModPrerequisites.GetIsFulfilled(csmStateManager))
				throw new DependencyException(xscScript.ModPrerequisites.GetMessage(csmStateManager));

			IList<InstallStep> lstSteps = xscScript.InstallSteps;
			HeaderInfo hifHeaderInfo = xscScript.HeaderInfo;
			if (String.IsNullOrEmpty(hifHeaderInfo.ImagePath))
				hifHeaderInfo.ImagePath = Mod.ScreenshotPath;
			if ((hifHeaderInfo.Height < 0) && hifHeaderInfo.ShowImage)
				hifHeaderInfo.Height = 75;
			OptionsForm ofmOptions = null;
			if (m_scxSyncContext == null)
				ofmOptions = new OptionsForm(xscScript, hifHeaderInfo, csmStateManager, lstSteps);
			else
				m_scxSyncContext.Send(x => ofmOptions = new OptionsForm(xscScript, hifHeaderInfo, csmStateManager, lstSteps), null);
			ofmOptions.Name = "OptionForm";
			bool booPerformInstall = false;
			if (lstSteps.Count == 0)
				booPerformInstall = true;
			else
			{
				if (m_scxSyncContext == null)
					booPerformInstall = (ofmOptions.ShowDialog() == DialogResult.OK);
				else
					m_scxSyncContext.Send(x => booPerformInstall = (ofmOptions.ShowDialog() == DialogResult.OK), null);
			}

			if (booPerformInstall)
			{
				XmlScriptInstaller xsiInstaller = new XmlScriptInstaller(Mod, GameMode, Installers, m_ivaVirtualModActivator);
				OnTaskStarted(xsiInstaller);
				return xsiInstaller.Install(hifHeaderInfo.Title, xscScript, csmStateManager, ofmOptions.FilesToInstall, ofmOptions.PluginsToActivate);				
			}
			return false;
		}

		#endregion
	}
}
