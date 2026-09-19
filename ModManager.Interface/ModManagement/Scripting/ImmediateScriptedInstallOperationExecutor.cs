using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Executes scripted installation operations immediately using the current NMM installation infrastructure.
	/// </summary>
	/// <remarks>
	/// The executor applies one operation synchronously when invoked. The owning <see cref="ScriptedInstallationSession"/>
	/// determines whether that invocation occurs immediately or after deferred planning has completed.
	/// </remarks>
	public class ImmediateScriptedInstallOperationExecutor : IScriptedInstallOperationExecutor, IScriptedInstallOperationBatchExecutor, IScriptedInstallOperationCompletionExecutor
	{
		private readonly IMod m_modMod;
		private readonly IGameMode m_gmdGameMode;
		private readonly IEnvironmentInfo m_eifEnvironmentInfo;
		private readonly IVirtualModActivator m_ivaVirtualModActivator;
		private readonly IModLinkInstaller m_mliModLinkInstaller;
		private readonly InstallerGroup m_igpInstallers;
		private readonly Action<IBackgroundTask> m_actTaskStarted;
		private readonly IScriptedFileSelectionCache m_sfcFileSelectionCache;
		private readonly ScriptedPluginActivationState m_spaPluginActivationState;
		private readonly bool m_booRequireSuccessfulPluginReconciliation;
		private readonly HashSet<string> m_hstCoordinatorPluginPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		#region Constructors

		/// <summary>
		/// Initializes an executor for the current scripted installation context.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application environment information.</param>
		/// <param name="p_ivaVirtualModActivator">The virtual mod activator used for deployment.</param>
		/// <param name="p_mliModLinkInstaller">The mod link installer used to deploy staged files.</param>
		/// <param name="p_igpInstallers">The installer group used to apply installation changes.</param>
		/// <param name="p_actTaskStarted">The optional callback invoked when a background installation task starts.</param>
		/// <param name="p_sfcFileSelectionCache">The optional cache used to record successfully linked scripted file selections.</param>
		public ImmediateScriptedInstallOperationExecutor(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, IModLinkInstaller p_mliModLinkInstaller, InstallerGroup p_igpInstallers, Action<IBackgroundTask> p_actTaskStarted, IScriptedFileSelectionCache p_sfcFileSelectionCache)
			: this(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_mliModLinkInstaller, p_igpInstallers, p_actTaskStarted, p_sfcFileSelectionCache, new ScriptedPluginActivationState(), false)
		{
		}

		/// <summary>
		/// Initializes an executor whose final plugin reconciliation may be required to succeed.
		/// </summary>
		/// <param name="p_booRequireSuccessfulPluginReconciliation">Whether an invalid final plugin state must fail completion instead of retaining legacy best-effort behavior.</param>
		public ImmediateScriptedInstallOperationExecutor(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, IModLinkInstaller p_mliModLinkInstaller, InstallerGroup p_igpInstallers, Action<IBackgroundTask> p_actTaskStarted, IScriptedFileSelectionCache p_sfcFileSelectionCache, bool p_booRequireSuccessfulPluginReconciliation)
			: this(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_mliModLinkInstaller, p_igpInstallers, p_actTaskStarted, p_sfcFileSelectionCache, new ScriptedPluginActivationState(), p_booRequireSuccessfulPluginReconciliation)
		{
		}

		/// <summary>
		/// Initializes an executor using activation state shared across all sessions in one scripted installation.
		/// </summary>
		public ImmediateScriptedInstallOperationExecutor(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, IModLinkInstaller p_mliModLinkInstaller, InstallerGroup p_igpInstallers, Action<IBackgroundTask> p_actTaskStarted, IScriptedFileSelectionCache p_sfcFileSelectionCache, ScriptedPluginActivationState p_spaPluginActivationState)
			: this(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_mliModLinkInstaller, p_igpInstallers, p_actTaskStarted, p_sfcFileSelectionCache, p_spaPluginActivationState, false)
		{
		}

		/// <summary>
		/// Initializes an executor using shared activation state and an explicit final-reconciliation policy.
		/// </summary>
		public ImmediateScriptedInstallOperationExecutor(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, IModLinkInstaller p_mliModLinkInstaller, InstallerGroup p_igpInstallers, Action<IBackgroundTask> p_actTaskStarted, IScriptedFileSelectionCache p_sfcFileSelectionCache, ScriptedPluginActivationState p_spaPluginActivationState, bool p_booRequireSuccessfulPluginReconciliation)
		{
			if (p_spaPluginActivationState == null)
				throw new ArgumentNullException(nameof(p_spaPluginActivationState));

			m_modMod = p_modMod;
			m_gmdGameMode = p_gmdGameMode;
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_ivaVirtualModActivator = p_ivaVirtualModActivator;
			m_mliModLinkInstaller = p_mliModLinkInstaller;
			m_igpInstallers = p_igpInstallers;
			m_actTaskStarted = p_actTaskStarted;
			m_sfcFileSelectionCache = p_sfcFileSelectionCache;
			m_spaPluginActivationState = p_spaPluginActivationState;
			m_booRequireSuccessfulPluginReconciliation = p_booRequireSuccessfulPluginReconciliation;
		}

		#endregion

		#region Execution Batching

		/// <summary>
		/// Begins a deployment batch for a set of pending scripted file operations.
		/// </summary>
		/// <param name="p_intExpectedFileOperations">The number of pending operations expected to create or update virtual links.</param>
		/// <returns>A scope that flushes deferred deployment maintenance when disposed.</returns>
		public IDisposable BeginExecutionBatch(int p_intExpectedFileOperations)
		{
			IDisposable dspVirtualBatch = m_igpInstallers.InstallContext.Method == ModInstallMethod.Virtual
				? new VirtualModDeploymentBatch(m_ivaVirtualModActivator, p_intExpectedFileOperations)
				: null;
			return new ScriptedDeploymentBatch(dspVirtualBatch, FlushPendingPluginRegistrations);
		}

		#endregion

		#region Successful Completion

		/// <summary>
		/// Completes retained scripted installation work after the owning installation has succeeded.
		/// </summary>
		public void CompleteExecution()
		{
			FlushPendingPluginRegistrations();
			if (!m_booRequireSuccessfulPluginReconciliation)
			{
				m_spaPluginActivationState.Reconcile(m_igpInstallers.PluginManager);
				return;
			}

			if (!m_spaPluginActivationState.TryReconcile(m_igpInstallers.PluginManager))
				throw new InvalidOperationException("The final plugin state requested by the installation recipe could not be reconciled safely.");
		}

		#endregion

		#region Operation Execution

		/// <summary>
		/// Executes the specified scripted installation operation immediately.
		/// </summary>
		/// <param name="p_sioOperation">The logical installation operation to execute.</param>
		/// <returns><c>true</c> when the operation completed successfully; otherwise, <c>false</c>.</returns>
		public bool Execute(ScriptedInstallOperation p_sioOperation)
		{
			if (p_sioOperation == null)
				throw new ArgumentNullException(nameof(p_sioOperation));

			if (IsPluginStateOperation(p_sioOperation))
				FlushPendingPluginRegistrations();

			PerformBasicInstallOperation bioBasicInstall = p_sioOperation as PerformBasicInstallOperation;
			if (bioBasicInstall != null)
				return ExecutePerformBasicInstall();

			InstallModFileOperation imoInstallFile = p_sioOperation as InstallModFileOperation;
			if (imoInstallFile != null)
				return ExecuteInstallModFile(imoInstallFile);

			GenerateDataFileOperation gdoGenerateFile = p_sioOperation as GenerateDataFileOperation;
			if (gdoGenerateFile != null)
				return ExecuteGenerateDataFile(gdoGenerateFile);

			SetPluginActivationOperation saoSetActivation = p_sioOperation as SetPluginActivationOperation;
			if (saoSetActivation != null)
				return ExecuteSetPluginActivation(saoSetActivation);

			SetPluginOrderIndexOperation sooSetOrderIndex = p_sioOperation as SetPluginOrderIndexOperation;
			if (sooSetOrderIndex != null)
				return ExecuteSetPluginOrderIndex(sooSetOrderIndex);

			SetLoadOrderOperation sloSetLoadOrder = p_sioOperation as SetLoadOrderOperation;
			if (sloSetLoadOrder != null)
				return ExecuteSetLoadOrder(sloSetLoadOrder);

			MovePluginsInLoadOrderOperation mloMovePlugins = p_sioOperation as MovePluginsInLoadOrderOperation;
			if (mloMovePlugins != null)
				return ExecuteMovePluginsInLoadOrder(mloMovePlugins);

			SetRelativeLoadOrderOperation rloRelativeLoadOrder = p_sioOperation as SetRelativeLoadOrderOperation;
			if (rloRelativeLoadOrder != null)
				return ExecuteSetRelativeLoadOrder(rloRelativeLoadOrder);

			EditIniOperation eioEditIni = p_sioOperation as EditIniOperation;
			if (eioEditIni != null)
				return ExecuteEditIni(eioEditIni);

			EditGameSpecificValueOperation egoGameSpecificValue = p_sioOperation as EditGameSpecificValueOperation;
			if (egoGameSpecificValue != null)
				return ExecuteEditGameSpecificValue(egoGameSpecificValue);

			throw new NotSupportedException(String.Format("Unsupported scripted installation operation type: {0}", p_sioOperation.GetType().FullName));
		}

		/// <summary>
		/// Performs the standard basic installation for the current mod.
		/// </summary>
		/// <returns><c>true</c> when the basic installation succeeds; otherwise, <c>false</c>.</returns>
		private bool ExecutePerformBasicInstall()
		{
			BasicInstallTask bitTask = new BasicInstallTask(
				m_modMod, m_gmdGameMode, m_igpInstallers.FileInstaller, m_igpInstallers.PluginManager,
				m_ivaVirtualModActivator, m_eifEnvironmentInfo.Settings.SkipReadmeFiles, null, null,
				m_igpInstallers.InstallContext, m_igpInstallers.DeploymentManager,
				m_igpInstallers.TransactionalFileManager, m_igpInstallers.DeploymentOverwriteResolver);
			if (m_actTaskStarted != null)
				m_actTaskStarted(bitTask);

			bool installed;
			try
			{
				installed = bitTask.Execute();
			}
			catch (Exception ex)
			{
				if (m_igpInstallers.InstallContext.Method == ModInstallMethod.Direct || bitTask.UsedPromotedDeployment)
					throw CreateDeploymentException("Scripted basic-install deployment failed.", ex);
				throw;
			}

			if (bitTask.UsedPromotedDeployment)
				m_igpInstallers.MarkPromotedDeploymentUsed();
			if (!installed && bitTask.Status != TaskStatus.Cancelling &&
				(m_igpInstallers.InstallContext.Method == ModInstallMethod.Direct || bitTask.UsedPromotedDeployment))
			{
				throw new ScriptedDeploymentException("Scripted basic-install deployment did not complete successfully.");
			}
			if (installed)
			{
				foreach (string strDeployedPluginPath in bitTask.DeployedPluginPaths)
					TrackDeployedPlugin(strDeployedPluginPath, true);
				if (m_sfcFileSelectionCache != null)
					m_sfcFileSelectionCache.RecordBasicInstall();
			}
			return installed;
		}

		/// <summary>
		/// Installs a mod archive file using the legacy scripted-installer staging and linking rules.
		/// </summary>
		/// <param name="p_imoOperation">The file installation operation to execute.</param>
		/// <returns><c>true</c> when the legacy installation path completes; otherwise, <c>false</c>.</returns>
		private bool ExecuteInstallModFile(InstallModFileOperation p_imoOperation)
		{
			if (ModInstallFileFilter.IsIgnored(p_imoOperation.SourcePath) || ModInstallFileFilter.IsIgnored(p_imoOperation.DestinationPath))
				return true;

			string strFrom = p_imoOperation.SourcePath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
			string strTo = p_imoOperation.DestinationPath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			ScriptedFileDeploymentDecision decision = p_imoOperation.DeploymentDecision;

			if (m_igpInstallers.InstallContext.Method == ModInstallMethod.Direct)
			{
				if (decision != null && !decision.WritePayload)
					return false;

				bool installed;
				try
				{
					installed = decision != null
						? ((IModFileInstallDecisionSupport)m_igpInstallers.FileInstaller).InstallFileFromModWithResolvedOverwrite(strFrom, strTo)
						: m_igpInstallers.FileInstaller.InstallFileFromMod(strFrom, strTo);
				}
				catch (Exception ex)
				{
					throw CreateDeploymentException("Scripted Direct file deployment failed.", ex);
				}

				if (installed)
				{
					TrackDeployedPlugin(GetPhysicalDeploymentPath(strTo));
					if (m_sfcFileSelectionCache != null)
						m_sfcFileSelectionCache.RecordSelection(p_imoOperation.SourcePath, p_imoOperation.DestinationPath);
				}
				return installed;
			}

			string strVirtualPath = p_imoOperation.HasResolvedStagingOverwrite
				? p_imoOperation.StagingPath
				: ScriptedInstallStagingPathResolver.GetStagingPath(m_modMod, m_gmdGameMode, m_ivaVirtualModActivator, strTo, false);

			if (!p_imoOperation.HasResolvedStagingOverwrite)
				m_igpInstallers.FileInstaller.InstallFileFromMod(strFrom, strVirtualPath);
			else if (p_imoOperation.StageFile)
				((IModFileInstallDecisionSupport)m_igpInstallers.FileInstaller).InstallFileFromModWithResolvedOverwrite(strFrom, strVirtualPath);

			string strLinkResult;
			ModDeploymentTarget target = GetPromotedTarget(strTo);
			if ((decision != null && decision.UseDeploymentCoordinator) || target != null)
			{
				RequireCoordinatorServices();
				if (target == null)
					target = ModDeploymentTargetResolver.Resolve(m_gmdGameMode, m_modMod, strTo, m_igpInstallers.InstallContext.InstallRoot);
				bool activate = decision != null && decision.UseDeploymentCoordinator
					? decision.Activate
					: m_igpInstallers.DeploymentOverwriteResolver.ShouldActivate(target);
				m_igpInstallers.MarkPromotedDeploymentUsed();
				try
				{
					strLinkResult = m_igpInstallers.DeploymentManager.InstallVirtualFile(
						m_modMod, target, strTo, strVirtualPath, m_igpInstallers.InstallContext.InstallRoot,
						activate, m_igpInstallers.TransactionalFileManager);
				}
				catch (Exception ex)
				{
					throw CreateDeploymentException("Scripted promoted Virtual file deployment failed.", ex);
				}
				TrackCoordinatorPlugin(strLinkResult);
			}
			else
			{
				IModLinkInstallDecisionSupport ldsLinkDecisionSupport = m_mliModLinkInstaller as IModLinkInstallDecisionSupport;
				if ((p_imoOperation.LinkDecision != null) && (ldsLinkDecisionSupport != null))
					strLinkResult = ldsLinkDecisionSupport.AddFileLinkWithResolvedDecision(m_modMod, strTo, strVirtualPath, true, true, m_igpInstallers.InstallContext.InstallRoot, p_imoOperation.LinkDecision);
				else
					strLinkResult = m_mliModLinkInstaller.AddFileLink(m_modMod, strTo, strVirtualPath, true, true, m_igpInstallers.InstallContext.InstallRoot);
			}

			if (!String.IsNullOrEmpty(strLinkResult))
			{
				TrackDeployedPlugin(strLinkResult);
				if (m_sfcFileSelectionCache != null)
					m_sfcFileSelectionCache.RecordSelection(p_imoOperation.SourcePath, p_imoOperation.DestinationPath);
			}

			return true;
		}

		/// <summary>
		/// Generates a data file using the legacy scripted-installer staging and linking rules.
		/// </summary>
		/// <param name="p_gdoOperation">The generated-file operation to execute.</param>
		/// <returns><c>true</c> when the generated file is staged and linked.</returns>
		private bool ExecuteGenerateDataFile(GenerateDataFileOperation p_gdoOperation)
		{
			string strPath = p_gdoOperation.DestinationPath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			ScriptedFileDeploymentDecision decision = p_gdoOperation.DeploymentDecision;

			if (m_igpInstallers.InstallContext.Method == ModInstallMethod.Direct)
			{
				if (decision != null && !decision.WritePayload)
					return false;

				bool generated;
				try
				{
					generated = decision != null
						? ((IModFileInstallDecisionSupport)m_igpInstallers.FileInstaller).GenerateDataFileWithResolvedOverwrite(strPath, p_gdoOperation.Data)
						: m_igpInstallers.FileInstaller.GenerateDataFile(strPath, p_gdoOperation.Data);
				}
				catch (Exception ex)
				{
					throw CreateDeploymentException("Scripted Direct generated-file deployment failed.", ex);
				}

				if (generated)
				{
					TrackDeployedPlugin(GetPhysicalDeploymentPath(strPath), false);
					if (m_sfcFileSelectionCache != null)
						m_sfcFileSelectionCache.RecordGeneratedFile(p_gdoOperation.DestinationPath, p_gdoOperation.Data);
				}
				return generated;
			}

			string strVirtualPath = p_gdoOperation.HasResolvedStagingOverwrite
				? p_gdoOperation.StagingPath
				: ScriptedInstallStagingPathResolver.GetStagingPath(m_modMod, m_gmdGameMode, m_ivaVirtualModActivator, strPath, true);

			if (!p_gdoOperation.HasResolvedStagingOverwrite)
				m_igpInstallers.FileInstaller.GenerateDataFile(strVirtualPath, p_gdoOperation.Data);
			else if (p_gdoOperation.StageFile)
				((IModFileInstallDecisionSupport)m_igpInstallers.FileInstaller).GenerateDataFileWithResolvedOverwrite(strVirtualPath, p_gdoOperation.Data);

			string strDeployedPath;
			ModDeploymentTarget target = GetPromotedTarget(strPath);
			if ((decision != null && decision.UseDeploymentCoordinator) || target != null)
			{
				RequireCoordinatorServices();
				if (target == null)
					target = ModDeploymentTargetResolver.Resolve(m_gmdGameMode, m_modMod, strPath, m_igpInstallers.InstallContext.InstallRoot);
				bool activate = decision != null && decision.UseDeploymentCoordinator
					? decision.Activate
					: m_igpInstallers.DeploymentOverwriteResolver.ShouldActivate(target);
				m_igpInstallers.MarkPromotedDeploymentUsed();
				try
				{
					strDeployedPath = m_igpInstallers.DeploymentManager.InstallVirtualFile(
						m_modMod, target, strPath, strVirtualPath, m_igpInstallers.InstallContext.InstallRoot,
						activate, m_igpInstallers.TransactionalFileManager);
				}
				catch (Exception ex)
				{
					throw CreateDeploymentException("Scripted promoted Virtual generated-file deployment failed.", ex);
				}
			}
			else
			{
				IModLinkInstallDecisionSupport ldsLinkDecisionSupport = m_mliModLinkInstaller as IModLinkInstallDecisionSupport;
				if ((p_gdoOperation.LinkDecision != null) && (ldsLinkDecisionSupport != null))
					strDeployedPath = ldsLinkDecisionSupport.AddFileLinkWithResolvedDecision(m_modMod, strPath, strVirtualPath, true, false, m_igpInstallers.InstallContext.InstallRoot, p_gdoOperation.LinkDecision);
				else
					strDeployedPath = m_mliModLinkInstaller.AddFileLink(m_modMod, strPath, strVirtualPath, true, false, m_igpInstallers.InstallContext.InstallRoot);
			}
			TrackDeployedPlugin(strDeployedPath, false);
			if (m_sfcFileSelectionCache != null)
				m_sfcFileSelectionCache.RecordGeneratedFile(p_gdoOperation.DestinationPath, p_gdoOperation.Data);
			return true;
		}

		/// <summary>
		/// Wraps a coordinator/filesystem exception in the fatal scripted-deployment marker without double wrapping it.
		/// </summary>
		private static ScriptedDeploymentException CreateDeploymentException(string p_strMessage, Exception p_exException)
		{
			ScriptedDeploymentException deploymentException = p_exException as ScriptedDeploymentException;
			return deploymentException ?? new ScriptedDeploymentException(p_strMessage, p_exException);
		}

		/// <summary>
		/// Applies a plugin activation change using the game-mode-adjusted plugin path.
		/// </summary>
		/// <param name="p_saoOperation">The plugin activation operation to execute.</param>
		/// <returns><c>true</c> after the activation request has been evaluated and, when applicable, forwarded.</returns>
		private bool ExecuteSetPluginActivation(SetPluginActivationOperation p_saoOperation)
		{
			if (m_igpInstallers.PluginManager == null)
				return true;

			string strFixedPath = m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, p_saoOperation.PluginPath, false);
			if (p_saoOperation.RequireActivatablePlugin && !m_igpInstallers.PluginManager.IsActivatiblePluginFile(strFixedPath))
				return true;

			m_igpInstallers.PluginManager.SetPluginActivation(strFixedPath, p_saoOperation.Activate);
			m_spaPluginActivationState.RecordActivationRequest(GetPhysicalPluginPath(strFixedPath), p_saoOperation.Activate);
			return true;
		}

		/// <summary>
		/// Applies an absolute load-order index to the requested plugin.
		/// </summary>
		/// <param name="p_sooOperation">The plugin-order operation to execute.</param>
		/// <returns><c>true</c> after the reorder request has been forwarded.</returns>
		private bool ExecuteSetPluginOrderIndex(SetPluginOrderIndexOperation p_sooOperation)
		{
			string strFixedPath = Path.Combine(m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath, m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, p_sooOperation.PluginPath, false));
			Plugin plgPlugin = m_igpInstallers.PluginManager.ManagedPlugins.Find(x => x.Filename.Equals(strFixedPath, StringComparison.OrdinalIgnoreCase));
			m_igpInstallers.PluginManager.SetPluginOrderIndex(plgPlugin, p_sooOperation.NewIndex);
			return true;
		}

		/// <summary>
		/// Applies the legacy full load-order request represented by plugin indices.
		/// </summary>
		/// <param name="p_sloOperation">The legacy load-order operation to execute.</param>
		/// <returns><c>true</c> after all plugin order requests have been applied.</returns>
		private bool ExecuteSetLoadOrder(SetLoadOrderOperation p_sloOperation)
		{
			List<Plugin> lstPlugins = new List<Plugin>(m_igpInstallers.PluginManager.ManagedPlugins);
			int[] intPlugins = p_sloOperation.PluginIndices.ToArray();
			if (intPlugins.Length != lstPlugins.Count)
				throw new ArgumentException("Length of new load order array was different to the total number of plugins");

			for (int i = 0; i < intPlugins.Length; i++)
				if ((intPlugins[i] < 0) || (intPlugins[i] >= intPlugins.Length))
					throw new IndexOutOfRangeException("A plugin index was out of range");

			// The legacy implementation validates the supplied permutation but reapplies the current sequence by index.
			for (int i = 0; i < lstPlugins.Count; i++)
				m_igpInstallers.PluginManager.SetPluginOrderIndex(lstPlugins[i], i);

			return true;
		}

		/// <summary>
		/// Moves the selected plugin indices to the requested position using the legacy ordering rules.
		/// </summary>
		/// <param name="p_mloOperation">The load-order move operation to execute.</param>
		/// <returns><c>true</c> after all plugin order requests have been applied.</returns>
		private bool ExecuteMovePluginsInLoadOrder(MovePluginsInLoadOrderOperation p_mloOperation)
		{
			List<Plugin> lstPlugins = new List<Plugin>(m_igpInstallers.PluginManager.ManagedPlugins);
			int[] intPlugins = p_mloOperation.PluginIndices.ToArray();
			Array.Sort(intPlugins);

			int intLoadOrder = 0;
			for (int i = 0; i < p_mloOperation.Position; i++)
			{
				if (Array.BinarySearch(intPlugins, i) >= 0)
					continue;
				m_igpInstallers.PluginManager.SetPluginOrderIndex(lstPlugins[i], intLoadOrder++);
			}
			for (int i = 0; i < intPlugins.Length; i++)
				m_igpInstallers.PluginManager.SetPluginOrderIndex(lstPlugins[intPlugins[i]], intLoadOrder++);
			for (int i = p_mloOperation.Position; i < lstPlugins.Count; i++)
			{
				if (Array.BinarySearch(intPlugins, i) >= 0)
					continue;
				m_igpInstallers.PluginManager.SetPluginOrderIndex(lstPlugins[i], intLoadOrder++);
			}
			return true;
		}

		/// <summary>
		/// Applies the requested relative plugin ordering using the legacy registered-plugin behavior.
		/// </summary>
		/// <param name="p_rloOperation">The relative load-order operation to execute.</param>
		/// <returns><c>true</c> after the relative ordering request has been processed.</returns>
		private bool ExecuteSetRelativeLoadOrder(SetRelativeLoadOrderOperation p_rloOperation)
		{
			if (p_rloOperation.PluginPaths.Count == 0)
				return true;

			List<string> lstRelativelyOrderedPlugins = new List<string>();
			foreach (string strPlugin in p_rloOperation.PluginPaths)
				lstRelativelyOrderedPlugins.Add(m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, strPlugin, false));

			Plugin plgCurrent = null;
			int intInitialIndex = 0;
			while ((intInitialIndex < lstRelativelyOrderedPlugins.Count) &&
				((plgCurrent = m_igpInstallers.PluginManager.GetRegisteredPlugin(lstRelativelyOrderedPlugins[intInitialIndex])) == null))
				intInitialIndex++;
			if (plgCurrent == null)
				return true;

			for (int i = intInitialIndex + 1; i < lstRelativelyOrderedPlugins.Count; i++)
			{
				Plugin plgNext = m_igpInstallers.PluginManager.GetRegisteredPlugin(lstRelativelyOrderedPlugins[i]);
				if (plgNext == null)
					continue;

				int intNextPosition = m_igpInstallers.PluginManager.GetPluginOrderIndex(plgNext);
				// Query the current position on every iteration because plugin restrictions may reject a reorder request.
				int intCurrentPosition = m_igpInstallers.PluginManager.GetPluginOrderIndex(plgCurrent);
				if (intNextPosition > intCurrentPosition)
				{
					plgCurrent = plgNext;
					continue;
				}

				m_igpInstallers.PluginManager.SetPluginOrderIndex(plgNext, intCurrentPosition + 1);
				if (intNextPosition != m_igpInstallers.PluginManager.GetPluginOrderIndex(plgNext))
					plgCurrent = plgNext;
			}
			return true;
		}

		/// <summary>
		/// Applies an INI edit through the current INI installer.
		/// </summary>
		/// <param name="p_eioOperation">The INI operation to execute.</param>
		/// <returns>The result returned by the INI installer.</returns>
		private bool ExecuteEditIni(EditIniOperation p_eioOperation)
		{
			if (p_eioOperation.HasResolvedOverwriteDecision)
				return ((IIniEditDecisionSupport)m_igpInstallers.IniInstaller).ApplyResolvedIniEdit(p_eioOperation.SettingsFileName, p_eioOperation.Section, p_eioOperation.Key, p_eioOperation.Value);

			return m_igpInstallers.IniInstaller.EditIni(p_eioOperation.SettingsFileName, p_eioOperation.Section, p_eioOperation.Key, p_eioOperation.Value);
		}

		/// <summary>
		/// Applies a game-specific value edit through the current game-specific value installer.
		/// </summary>
		/// <param name="p_egoOperation">The game-specific value operation to execute.</param>
		/// <returns>The result returned by the game-specific value installer.</returns>
		private bool ExecuteEditGameSpecificValue(EditGameSpecificValueOperation p_egoOperation)
		{
			if (p_egoOperation.HasResolvedOverwriteDecision)
				return ((IGameSpecificValueInstallDecisionSupport)m_igpInstallers.GameSpecificValueInstaller).ApplyResolvedGameSpecificValueEdit(p_egoOperation.Key, p_egoOperation.Value);

			return m_igpInstallers.GameSpecificValueInstaller.EditGameSpecificValue(p_egoOperation.Key, p_egoOperation.Value);
		}


		#endregion
		/// <summary>
		/// Resolves a destination only when it is already owned by the promoted deployment registry.
		/// </summary>
		private ModDeploymentTarget GetPromotedTarget(string p_strDestinationPath)
		{
			if (m_igpInstallers.DeploymentManager == null || !m_igpInstallers.DeploymentManager.HasPromotedTargets)
				return null;

			ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(
				m_gmdGameMode, m_modMod, p_strDestinationPath, m_igpInstallers.InstallContext.InstallRoot);
			return m_igpInstallers.DeploymentManager.IsPromoted(target) ? target : null;
		}

		/// <summary>
		/// Verifies that promoted scripted deployment has all transactional coordinator services.
		/// </summary>
		private void RequireCoordinatorServices()
		{
			if (m_igpInstallers.DeploymentManager == null || m_igpInstallers.TransactionalFileManager == null ||
				m_igpInstallers.DeploymentOverwriteResolver == null)
				throw new InvalidOperationException("Promoted scripted deployment requires transactional deployment services.");
		}

		/// <summary>
		/// Resolves a scripted destination to the physical path deployed by the current install method.
		/// </summary>
		private string GetPhysicalDeploymentPath(string p_strDestinationPath)
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(
				m_gmdGameMode, m_modMod, p_strDestinationPath, m_igpInstallers.InstallContext.InstallRoot);
			return ModDeploymentTargetResolver.GetPhysicalPath(m_gmdGameMode, target);
		}

		/// <summary>
		/// Resolves the game-mode-adjusted plugin path to the physical path used by plugin management.
		/// </summary>
		private string GetPhysicalPluginPath(string p_strPluginPath)
		{
			return Path.IsPathRooted(p_strPluginPath)
				? p_strPluginPath
				: Path.Combine(m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath, p_strPluginPath);
		}

		/// <summary>
		/// Records a successfully deployed plugin as an implicit activation request for this scripted installation.
		/// </summary>
		private void TrackDeployedPlugin(string p_strDeployedPath)
		{
			TrackDeployedPlugin(p_strDeployedPath, true);
		}

		/// <summary>
		/// Records a successfully deployed plugin and whether that deployment implicitly requests activation.
		/// </summary>
		private void TrackDeployedPlugin(string p_strDeployedPath, bool p_booRequestActivation)
		{
			if (String.IsNullOrEmpty(p_strDeployedPath) || m_igpInstallers.PluginManager == null ||
				!m_igpInstallers.PluginManager.IsActivatiblePluginFile(p_strDeployedPath))
				return;

			m_spaPluginActivationState.RecordDeployedPlugin(p_strDeployedPath, p_booRequestActivation);
		}

		/// <summary>
		/// Adds a promoted Virtual plugin winner to the current scripted registration batch.
		/// </summary>
		private void TrackCoordinatorPlugin(string p_strDeployedPath)
		{
			if (String.IsNullOrEmpty(p_strDeployedPath) || m_igpInstallers.PluginManager == null ||
				!m_igpInstallers.PluginManager.IsActivatiblePluginFile(p_strDeployedPath))
				return;
			m_hstCoordinatorPluginPaths.Add(p_strDeployedPath);
		}

		/// <summary>
		/// Flushes plugin registrations needed by scripted plugin-state operations without prematurely finalizing Direct upgrade cleanup.
		/// </summary>
		private void FlushPendingPluginRegistrations()
		{
			if (m_igpInstallers.InstallContext.Method == ModInstallMethod.Direct)
			{
				IModFilePluginRegistrationSupport prsPluginRegistration = m_igpInstallers.FileInstaller as IModFilePluginRegistrationSupport;
				if (prsPluginRegistration == null)
					throw new InvalidOperationException("Direct scripted installation requires plugin-registration flush support.");
				prsPluginRegistration.FlushPendingPluginRegistrations();
			}
			else
			{
				m_igpInstallers.FileInstaller.FinalizeInstall();
			}

			if (m_igpInstallers.PluginManager != null && m_hstCoordinatorPluginPaths.Count > 0)
				m_igpInstallers.PluginManager.IntegrateDeployedPlugins(new List<string>(m_hstCoordinatorPluginPaths));
			m_hstCoordinatorPluginPaths.Clear();
		}

		/// <summary>
		/// Determines whether an operation requires newly deployed plugins to be registered first.
		/// </summary>
		private static bool IsPluginStateOperation(ScriptedInstallOperation p_sioOperation)
		{
			return p_sioOperation is SetPluginActivationOperation ||
				p_sioOperation is SetPluginOrderIndexOperation ||
				p_sioOperation is SetLoadOrderOperation ||
				p_sioOperation is MovePluginsInLoadOrderOperation ||
				p_sioOperation is SetRelativeLoadOrderOperation;
		}

		/// <summary>
		/// Completes scripted plugin batching together with any active Virtual deployment batch.
		/// </summary>
		private sealed class ScriptedDeploymentBatch : IDisposable
		{
			private IDisposable m_dspVirtualBatch;
			private Action m_actFlush;

			/// <summary>
			/// Initializes a scripted deployment batch.
			/// </summary>
			public ScriptedDeploymentBatch(IDisposable p_dspVirtualBatch, Action p_actFlush)
			{
				m_dspVirtualBatch = p_dspVirtualBatch;
				m_actFlush = p_actFlush;
			}

			/// <summary>
			/// Flushes pending plugin work and completes the wrapped Virtual batch.
			/// </summary>
			public void Dispose()
			{
				Action flush = m_actFlush;
				m_actFlush = null;
				try
				{
					if (flush != null)
						flush();
				}
				finally
				{
					IDisposable virtualBatch = m_dspVirtualBatch;
					m_dspVirtualBatch = null;
					if (virtualBatch != null)
						virtualBatch.Dispose();
				}
			}
		}

	}
}
