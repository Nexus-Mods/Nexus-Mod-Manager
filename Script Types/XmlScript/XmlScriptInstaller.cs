using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// Plans and performs the mod installation described by an XML script.
	/// </summary>
	public class XmlScriptInstaller : BackgroundTask
	{
		private readonly IScriptedInstallOperationExecutor m_sioOperationExecutor;

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
		/// Gets the installer group to use to install mod items.
		/// </summary>
		/// <value>The installer group to use to install mod items.</value>
		protected InstallerGroup Installers { get; private set; }

		/// <summary>
		/// Gets the deferred scripted installation session for the current XML installation.
		/// </summary>
		protected ScriptedInstallationSession InstallationSession { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes the XML installer with the dependencies required to plan and execute scripted operations.
		/// </summary>
		/// <param name="p_modMod">The mod for which the script is running.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		/// <param name="p_ivaVirtualModActivator">The virtual mod activator used to stage and deploy files.</param>
		public XmlScriptInstaller(IMod p_modMod, IGameMode p_gmdGameMode, InstallerGroup p_igpInstallers, IVirtualModActivator p_ivaVirtualModActivator)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			Installers = p_igpInstallers;

			IScriptedFileSelectionCache sfcFileSelectionCache = new ScriptedFileSelectionCache(Mod, GameMode);
			m_sioOperationExecutor = new XmlScriptedInstallOperationExecutor(Mod, GameMode, Installers, p_ivaVirtualModActivator, p_ivaVirtualModActivator.GetModLinkInstaller(), sfcFileSelectionCache);
			ResetInstallationSession();
		}

		#endregion

		/// <summary>
		/// Performs the mod installation based on the XML script.
		/// </summary>
		/// <param name="p_strModName">The name of the mod whose script is executing.</param>
		/// <param name="p_xscScript">The script that is executing.</param>
		/// <param name="p_csmStateManager">The state manager managing the install state.</param>
		/// <param name="p_colFilesToInstall">The list of files to install.</param>
		/// <param name="p_colPluginsToActivate">The list of plugins to activate.</param>
		/// <returns><c>true</c> if the installation succeeded; <c>false</c> otherwise.</returns>
		public bool Install(string p_strModName, XmlScript p_xscScript, ConditionStateManager p_csmStateManager, ICollection<InstallableFile> p_colFilesToInstall, ICollection<InstallableFile> p_colPluginsToActivate)
		{
			OverallMessage = String.Format("Installing {0}", p_strModName);
			OverallProgressStepSize = 1;
			ItemProgressStepSize = 1;
			ShowItemProgress = false;
			ResetInstallationSession();

			bool booSuccess = false;
			try
			{
				booSuccess = InstallFiles(p_xscScript, p_csmStateManager, p_colFilesToInstall, p_colPluginsToActivate);
				Status = Status == TaskStatus.Cancelling ? TaskStatus.Cancelled : TaskStatus.Complete;
			}
			catch (Exception ex)
			{
				booSuccess = false;
				Status = TaskStatus.Error;
				throw new Exception(ex.Message);
			}
			OnTaskEnded(booSuccess);
			return booSuccess;
		}

		/// <summary>
		/// Builds the XML installation plan and executes it only after all file selections have been resolved.
		/// </summary>
		/// <param name="p_xscScript">The XML script to execute.</param>
		/// <param name="p_csmStateManager">The state manager used to evaluate conditional file sets.</param>
		/// <param name="p_colFilesToInstall">The files selected by the installer options.</param>
		/// <param name="p_colPluginsToActivate">The selected files whose plugins should be activated.</param>
		/// <returns><c>true</c> if planning and execution complete successfully; otherwise, <c>false</c>.</returns>
		protected bool InstallFiles(XmlScript p_xscScript, ConditionStateManager p_csmStateManager, ICollection<InstallableFile> p_colFilesToInstall, ICollection<InstallableFile> p_colPluginsToActivate)
		{
			IList<InstallableFile> lstRequiredFiles = p_xscScript.RequiredInstallFiles;
			IList<ConditionallyInstalledFileSet> lstConditionallyInstalledFileSets = p_xscScript.ConditionallyInstalledFileSets;
			ISet<string> setSelectableSources = GetSelectableOptionSources(p_xscScript);

			foreach (InstallableFile iflRequiredFile in lstRequiredFiles)
			{
				if (Status == TaskStatus.Cancelling)
					return false;
				if (!InstallFile(iflRequiredFile, true))
					return false;
			}

			foreach (InstallableFile ilfFile in p_colFilesToInstall)
			{
				if (Status == TaskStatus.Cancelling)
					return false;
				if (!InstallFile(ilfFile, p_colPluginsToActivate.Contains(ilfFile)))
					return false;
			}

			foreach (ConditionallyInstalledFileSet cisFileSet in lstConditionallyInstalledFileSets)
			{
				if (!cisFileSet.Condition.GetIsFulfilled(p_csmStateManager))
					continue;

				foreach (InstallableFile ilfFile in cisFileSet.Files)
				{
					if (IsUnselectedOptionFallback(cisFileSet, ilfFile, setSelectableSources))
						continue;
					if (Status == TaskStatus.Cancelling)
						return false;
					if (!InstallFile(ilfFile, true))
						return false;
				}
			}

			return ExecuteInstallationPlan();
		}

		/// <summary>
		/// Gets the file sources declared by selectable installer options.
		/// </summary>
		/// <param name="p_xscScript">The XML script whose selectable options should be inspected.</param>
		/// <returns>The normalized set of archive sources exposed by selectable options.</returns>
		private ISet<string> GetSelectableOptionSources(XmlScript p_xscScript)
		{
			HashSet<string> setSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (InstallStep stpStep in p_xscScript.InstallSteps)
				foreach (OptionGroup ogpGroup in stpStep.OptionGroups)
					foreach (Option optOption in ogpGroup.Options)
						foreach (InstallableFile ilfFile in optOption.Files)
							if (!String.IsNullOrEmpty(ilfFile.Source))
								setSources.Add(NormalizeInstallerPath(ilfFile.Source));

			return setSources;
		}

		/// <summary>
		/// Detects conditional files that only preserve an unselected option's archive contents.
		/// </summary>
		/// <param name="p_cisFileSet">The conditional file set being evaluated.</param>
		/// <param name="p_ilfFile">The conditional file to inspect.</param>
		/// <param name="p_setSelectableSources">The archive sources declared by selectable options.</param>
		/// <returns><c>true</c> when the file is an inactive-option fallback; otherwise, <c>false</c>.</returns>
		private bool IsUnselectedOptionFallback(ConditionallyInstalledFileSet p_cisFileSet, InstallableFile p_ilfFile, ISet<string> p_setSelectableSources)
		{
			if ((p_cisFileSet == null) || (p_ilfFile == null) || String.IsNullOrEmpty(p_ilfFile.Source))
				return false;

			return IsInactiveFlagCondition(p_cisFileSet.Condition) && p_setSelectableSources.Contains(NormalizeInstallerPath(p_ilfFile.Source));
		}

		/// <summary>
		/// Identifies flag conditions that represent an option being left unselected.
		/// </summary>
		/// <param name="p_cndCondition">The condition to inspect.</param>
		/// <returns><c>true</c> when the condition represents an inactive option; otherwise, <c>false</c>.</returns>
		private bool IsInactiveFlagCondition(ICondition p_cndCondition)
		{
			if (p_cndCondition is FlagCondition)
				return ((FlagCondition)p_cndCondition).Value.Equals("Inactive", StringComparison.OrdinalIgnoreCase);

			CompositeCondition cpcCondition = p_cndCondition as CompositeCondition;
			if (cpcCondition == null)
				return false;

			if (cpcCondition.Operator == ConditionOperator.Or)
				return false;

			return cpcCondition.Conditions.Count > 0 && cpcCondition.Conditions.All(IsInactiveFlagCondition);
		}

		/// <summary>
		/// Normalizes an installer path to the platform directory separator.
		/// </summary>
		/// <param name="p_strPath">The installer path to normalize.</param>
		/// <returns>The normalized installer path.</returns>
		private string NormalizeInstallerPath(string p_strPath)
		{
			return p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		}

		/// <summary>
		/// Adds the requested file installation and plugin activation operations to the deferred installation plan.
		/// </summary>
		/// <param name="p_ilfFile">The file or folder to install.</param>
		/// <param name="p_booActivate">Whether plugins represented by the file should be activated.</param>
		/// <returns><c>false</c> if planning was cancelled; otherwise, <c>true</c>.</returns>
		protected bool InstallFile(InstallableFile p_ilfFile, bool p_booActivate)
		{
			string strSource = p_ilfFile.Source;
			string strDest = p_ilfFile.Destination;
			if (!p_ilfFile.IsFolder && (ModInstallFileFilter.IsIgnored(strSource) || ModInstallFileFilter.IsIgnored(strDest)))
				return true;

			if (p_ilfFile.IsFolder)
			{
				if (!InstallFolderFromMod(p_ilfFile))
					return false;

				// Files installed below a non-empty destination cannot represent plugins in the Data root.
				if (strDest.Length == 0)
				{
					List<string> lstFiles = Mod.GetFileList(strSource, true).Where(x => !ModInstallFileFilter.IsIgnored(x)).ToList();
					string strDirectorySeparatorChar = Path.DirectorySeparatorChar.ToString();
					if (!strSource.EndsWith(strDirectorySeparatorChar) && !strSource.EndsWith("/"))
						strSource += strDirectorySeparatorChar;

					foreach (string strFile in lstFiles)
					{
						if (Status == TaskStatus.Cancelling)
							return false;

						string strPluginPath = strFile.Substring(strSource.Length, strFile.Length - strSource.Length);
						QueuePluginActivation(strPluginPath, p_booActivate);
					}
				}
			}
			else
			{
				if (!InstallFileFromMod(strSource, strDest))
					return false;

				QueuePluginActivation(String.IsNullOrEmpty(strDest) ? strSource : strDest, p_booActivate);
			}
			return true;
		}

		#region Helper Methods

		/// <summary>
		/// Adds all files from the specified archive folder to the deferred installation plan.
		/// </summary>
		/// <param name="p_ilfFile">The folder to install.</param>
		/// <returns><c>false</c> if planning was cancelled; otherwise, <c>true</c>.</returns>
		protected bool InstallFolderFromMod(InstallableFile p_ilfFile)
		{
			List<string> lstModFiles = Mod.GetFileList(p_ilfFile.Source, true).Where(x => !ModInstallFileFilter.IsIgnored(x)).ToList();
			String strFrom = p_ilfFile.Source.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
			if (!strFrom.EndsWith(Path.DirectorySeparatorChar.ToString()))
				strFrom += Path.DirectorySeparatorChar;
			String strTo = p_ilfFile.Destination.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if ((strTo.Length > 0) && (!strTo.EndsWith(Path.DirectorySeparatorChar.ToString())))
				strTo += Path.DirectorySeparatorChar;

			for (Int32 i = 0; i < lstModFiles.Count; i++)
			{
				if (Status == TaskStatus.Cancelling)
					return false;

				string strModFile = lstModFiles[i];
				string strNewFileName = strModFile.Substring(strFrom.Length, strModFile.Length - strFrom.Length);
				if (strTo.Length > 0)
					strNewFileName = Path.Combine(strTo, strNewFileName);
				if (!InstallFileFromMod(strModFile, strNewFileName))
					return false;
			}
			return true;
		}

		/// <summary>
		/// Adds a logical archive-file installation operation to the deferred XML installation plan.
		/// </summary>
		/// <param name="p_strFrom">The path of the file inside the mod archive.</param>
		/// <param name="p_strTo">The logical destination path requested by the XML installer.</param>
		/// <returns><c>true</c> when the operation is accepted by the deferred session.</returns>
		protected bool InstallFileFromMod(string p_strFrom, string p_strTo)
		{
			if (ModInstallFileFilter.IsIgnored(p_strFrom) || ModInstallFileFilter.IsIgnored(p_strTo))
				return true;

			return InstallationSession.Submit(new InstallModFileOperation(p_strFrom, p_strTo));
		}

		/// <summary>
		/// Adds a plugin activation candidate for execution after the corresponding file deployment.
		/// </summary>
		/// <param name="p_strPluginPath">The logical plugin path to evaluate during execution.</param>
		/// <param name="p_booActivate">Whether the plugin should be activated.</param>
		private void QueuePluginActivation(string p_strPluginPath, bool p_booActivate)
		{
			if (Installers.PluginManager != null)
				InstallationSession.Submit(new SetPluginActivationOperation(p_strPluginPath, p_booActivate));
		}

		/// <summary>
		/// Executes the deferred XML installation plan in submission order.
		/// </summary>
		/// <returns><c>true</c> when all planned operations complete successfully; otherwise, <c>false</c>.</returns>
		protected bool ExecuteInstallationPlan()
		{
			OverallProgress = 0;
			OverallProgressMaximum = InstallationSession.Plan.Count;
			using (InstallationSession.BeginPendingOperationBatch())
			{
				while (InstallationSession.HasPendingOperations)
				{
					if (Status == TaskStatus.Cancelling)
						return false;

					ItemMessage = GetOperationMessage(InstallationSession.NextPendingOperation);
					if (!InstallationSession.ExecuteNext())
						return false;
					StepOverallProgress();
				}
				return true;
			}
		}

		/// <summary>
		/// Creates a user-facing progress message for the specified planned operation.
		/// </summary>
		/// <param name="p_sioOperation">The operation about to be executed.</param>
		/// <returns>A concise progress message describing the operation.</returns>
		private string GetOperationMessage(ScriptedInstallOperation p_sioOperation)
		{
			InstallModFileOperation imoInstallFile = p_sioOperation as InstallModFileOperation;
			if (imoInstallFile != null)
				return "Installing " + (String.IsNullOrEmpty(imoInstallFile.DestinationPath) ? imoInstallFile.SourcePath : imoInstallFile.DestinationPath);

			SetPluginActivationOperation saoActivation = p_sioOperation as SetPluginActivationOperation;
			if (saoActivation != null)
				return (saoActivation.Activate ? "Activating " : "Deactivating ") + saoActivation.PluginPath;

			return "Applying scripted installation operation";
		}

		/// <summary>
		/// Creates a new deferred installation session for an XML installation run.
		/// </summary>
		private void ResetInstallationSession()
		{
			InstallationSession = new ScriptedInstallationSession(m_sioOperationExecutor, ScriptedInstallationSessionMode.Deferred);
		}

		#endregion
	}
}
