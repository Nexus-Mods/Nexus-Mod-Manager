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
	public class ImmediateScriptedInstallOperationExecutor : IScriptedInstallOperationExecutor, IScriptedInstallOperationBatchExecutor
	{
		private readonly IMod m_modMod;
		private readonly IGameMode m_gmdGameMode;
		private readonly IEnvironmentInfo m_eifEnvironmentInfo;
		private readonly IVirtualModActivator m_ivaVirtualModActivator;
		private readonly IModLinkInstaller m_mliModLinkInstaller;
		private readonly InstallerGroup m_igpInstallers;
		private readonly Action<IBackgroundTask> m_actTaskStarted;
		private readonly IScriptedFileSelectionCache m_sfcFileSelectionCache;

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
		{
			m_modMod = p_modMod;
			m_gmdGameMode = p_gmdGameMode;
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_ivaVirtualModActivator = p_ivaVirtualModActivator;
			m_mliModLinkInstaller = p_mliModLinkInstaller;
			m_igpInstallers = p_igpInstallers;
			m_actTaskStarted = p_actTaskStarted;
			m_sfcFileSelectionCache = p_sfcFileSelectionCache;
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
			return new VirtualModDeploymentBatch(m_ivaVirtualModActivator, p_intExpectedFileOperations);
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
			BasicInstallTask bitTask = new BasicInstallTask(m_modMod, m_gmdGameMode, m_igpInstallers.FileInstaller, m_igpInstallers.PluginManager, m_ivaVirtualModActivator, m_eifEnvironmentInfo.Settings.SkipReadmeFiles, null, null);
			if (m_actTaskStarted != null)
				m_actTaskStarted(bitTask);
			return bitTask.Execute();
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
			string strVirtualPath = p_imoOperation.HasResolvedStagingOverwrite
				? p_imoOperation.StagingPath
				: ScriptedInstallStagingPathResolver.GetStagingPath(m_modMod, m_gmdGameMode, m_ivaVirtualModActivator, strTo, false);

			// Linking remains independent from the staging write because legacy scripts can reuse an existing staged file.
			if (!p_imoOperation.HasResolvedStagingOverwrite)
				m_igpInstallers.FileInstaller.InstallFileFromMod(strFrom, strVirtualPath);
			else if (p_imoOperation.StageFile)
				((IModFileInstallDecisionSupport)m_igpInstallers.FileInstaller).InstallFileFromModWithResolvedOverwrite(strFrom, strVirtualPath);

			string strLinkResult;
			IModLinkInstallDecisionSupport ldsLinkDecisionSupport = m_mliModLinkInstaller as IModLinkInstallDecisionSupport;
			if ((p_imoOperation.LinkDecision != null) && (ldsLinkDecisionSupport != null))
				strLinkResult = ldsLinkDecisionSupport.AddFileLinkWithResolvedDecision(m_modMod, strTo, strVirtualPath, true, true, ModInstallRoot.Default, p_imoOperation.LinkDecision);
			else
				strLinkResult = m_mliModLinkInstaller.AddFileLink(m_modMod, strTo, strVirtualPath, true, true);

			if (!String.IsNullOrEmpty(strLinkResult) && (m_sfcFileSelectionCache != null))
				m_sfcFileSelectionCache.RecordSelection(p_imoOperation.SourcePath, p_imoOperation.DestinationPath);

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
			string strVirtualPath = p_gdoOperation.HasResolvedStagingOverwrite
				? p_gdoOperation.StagingPath
				: ScriptedInstallStagingPathResolver.GetStagingPath(m_modMod, m_gmdGameMode, m_ivaVirtualModActivator, strPath, true);

			if (!p_gdoOperation.HasResolvedStagingOverwrite)
				m_igpInstallers.FileInstaller.GenerateDataFile(strVirtualPath, p_gdoOperation.Data);
			else if (p_gdoOperation.StageFile)
				((IModFileInstallDecisionSupport)m_igpInstallers.FileInstaller).GenerateDataFileWithResolvedOverwrite(strVirtualPath, p_gdoOperation.Data);

			IModLinkInstallDecisionSupport ldsLinkDecisionSupport = m_mliModLinkInstaller as IModLinkInstallDecisionSupport;
			if ((p_gdoOperation.LinkDecision != null) && (ldsLinkDecisionSupport != null))
				ldsLinkDecisionSupport.AddFileLinkWithResolvedDecision(m_modMod, strPath, strVirtualPath, true, false, ModInstallRoot.Default, p_gdoOperation.LinkDecision);
			else
				m_mliModLinkInstaller.AddFileLink(m_modMod, strPath, strVirtualPath, true);
			return true;
		}

		/// <summary>
		/// Applies a plugin activation change using the game-mode-adjusted plugin path.
		/// </summary>
		/// <param name="p_saoOperation">The plugin activation operation to execute.</param>
		/// <returns><c>true</c> after the activation request has been forwarded.</returns>
		private bool ExecuteSetPluginActivation(SetPluginActivationOperation p_saoOperation)
		{
			string strFixedPath = m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, p_saoOperation.PluginPath, false);
			m_igpInstallers.PluginManager.SetPluginActivation(strFixedPath, p_saoOperation.Activate);
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
	}
}
