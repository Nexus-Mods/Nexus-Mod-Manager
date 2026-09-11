using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Provides the effective installation state visible to a deferred scripted installer after applying its pending operations.
	/// </summary>
	/// <remarks>
	/// The projected state overlays planned file, INI, and plugin changes on top of the current installation without committing
	/// those changes to the game directory. Archive-backed files are resolved lazily when their contents are requested.
	/// </remarks>
	public sealed class ScriptedInstallationProjectedState
	{
		private readonly IMod m_modMod;
		private readonly IGameMode m_gmdGameMode;
		private readonly InstallerGroup m_igpInstallers;
		private readonly Dictionary<string, ProjectedDataFile> m_dicProjectedFiles = new Dictionary<string, ProjectedDataFile>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Plugin> m_dicProjectedPluginInfo = new Dictionary<string, Plugin>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> m_dicIniValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private List<string> m_lstManagedPlugins;
		private HashSet<string> m_hstActivePlugins;

		#region Constructors

		/// <summary>
		/// Initializes a projected-state view for the supplied scripted installation context.
		/// </summary>
		/// <param name="p_modMod">The mod whose scripted installer is being evaluated.</param>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_igpInstallers">The installer group used to read the current installation state.</param>
		public ScriptedInstallationProjectedState(IMod p_modMod, IGameMode p_gmdGameMode, InstallerGroup p_igpInstallers)
		{
			if (p_modMod == null)
				throw new ArgumentNullException(nameof(p_modMod));
			if (p_gmdGameMode == null)
				throw new ArgumentNullException(nameof(p_gmdGameMode));
			if (p_igpInstallers == null)
				throw new ArgumentNullException(nameof(p_igpInstallers));

			m_modMod = p_modMod;
			m_gmdGameMode = p_gmdGameMode;
			m_igpInstallers = p_igpInstallers;
		}

		#endregion

		#region Operation Projection

		/// <summary>
		/// Applies the observable effect of a planned operation to the projected state.
		/// </summary>
		/// <param name="p_sioOperation">The operation whose projected effect should be recorded.</param>
		public void Apply(ScriptedInstallOperation p_sioOperation)
		{
			if (p_sioOperation == null)
				throw new ArgumentNullException(nameof(p_sioOperation));

			if (p_sioOperation is PerformBasicInstallOperation)
				throw new NotSupportedException("Basic-install projection requires expansion into explicit file operations before deferred read-after-write state can be provided.");

			InstallModFileOperation imoInstallFile = p_sioOperation as InstallModFileOperation;
			if (imoInstallFile != null)
			{
				ApplyInstallModFile(imoInstallFile);
				return;
			}

			GenerateDataFileOperation gdoGenerateFile = p_sioOperation as GenerateDataFileOperation;
			if (gdoGenerateFile != null)
			{
				ApplyGenerateDataFile(gdoGenerateFile);
				return;
			}

			EditIniOperation eioEditIni = p_sioOperation as EditIniOperation;
			if (eioEditIni != null)
			{
				m_dicIniValues[GetIniKey(eioEditIni.SettingsFileName, eioEditIni.Section, eioEditIni.Key)] = eioEditIni.Value ?? String.Empty;
				return;
			}

			SetPluginActivationOperation saoActivation = p_sioOperation as SetPluginActivationOperation;
			if (saoActivation != null)
			{
				ApplyPluginActivation(saoActivation);
				return;
			}

			SetPluginOrderIndexOperation soiOrderIndex = p_sioOperation as SetPluginOrderIndexOperation;
			if (soiOrderIndex != null)
			{
				ApplyPluginOrderIndex(soiOrderIndex);
				return;
			}

			SetLoadOrderOperation sloLoadOrder = p_sioOperation as SetLoadOrderOperation;
			if (sloLoadOrder != null)
			{
				ValidateLegacyLoadOrder(sloLoadOrder);
				return;
			}

			MovePluginsInLoadOrderOperation mloMovePlugins = p_sioOperation as MovePluginsInLoadOrderOperation;
			if (mloMovePlugins != null)
			{
				ApplyMovePlugins(mloMovePlugins);
				return;
			}

			SetRelativeLoadOrderOperation rloRelativeOrder = p_sioOperation as SetRelativeLoadOrderOperation;
			if (rloRelativeOrder != null)
				ApplyRelativeLoadOrder(rloRelativeOrder);
		}

		/// <summary>
		/// Projects the installation of a file from the current mod archive.
		/// </summary>
		/// <param name="p_imoOperation">The archive-file installation operation.</param>
		private void ApplyInstallModFile(InstallModFileOperation p_imoOperation)
		{
			if (!ShouldProjectFileDestination(p_imoOperation.LinkDecision))
				return;

			string strDestination = GetAdjustedDataPath(p_imoOperation.DestinationPath);
			if (p_imoOperation.HasResolvedStagingOverwrite && !p_imoOperation.StageFile)
			{
				if (String.IsNullOrEmpty(p_imoOperation.StagingPath) || !File.Exists(p_imoOperation.StagingPath))
					return;

				m_dicProjectedFiles[strDestination] = new ProjectedDataFile(() => File.ReadAllBytes(p_imoOperation.StagingPath));
			}
			else
			{
				string strSource = p_imoOperation.SourcePath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
				m_dicProjectedFiles[strDestination] = new ProjectedDataFile(() => m_modMod.GetFile(strSource));
			}

			RegisterProjectedPlugin(p_imoOperation.DestinationPath, true);
		}

		/// <summary>
		/// Projects the creation or replacement of a generated data file.
		/// </summary>
		/// <param name="p_gdoOperation">The generated-file operation.</param>
		private void ApplyGenerateDataFile(GenerateDataFileOperation p_gdoOperation)
		{
			if (!ShouldProjectFileDestination(p_gdoOperation.LinkDecision))
				return;

			string strDestination = GetAdjustedDataPath(p_gdoOperation.DestinationPath);
			if (p_gdoOperation.HasResolvedStagingOverwrite && !p_gdoOperation.StageFile)
			{
				if (String.IsNullOrEmpty(p_gdoOperation.StagingPath) || !File.Exists(p_gdoOperation.StagingPath))
					return;

				m_dicProjectedFiles[strDestination] = new ProjectedDataFile(() => File.ReadAllBytes(p_gdoOperation.StagingPath));
			}
			else
				m_dicProjectedFiles[strDestination] = new ProjectedDataFile(p_gdoOperation.Data);

			RegisterProjectedPlugin(p_gdoOperation.DestinationPath, false);
		}

		/// <summary>
		/// Determines whether a planned file operation changes the data visible to subsequent script queries.
		/// </summary>
		/// <param name="p_midDecision">The virtual-link decision associated with the operation.</param>
		/// <returns><c>true</c> when the destination should expose the incoming staged file in projected state.</returns>
		private static bool ShouldProjectFileDestination(ModLinkInstallDecision p_midDecision)
		{
			return (p_midDecision == null) || !p_midDecision.HasLinkOutcome || (p_midDecision.LinkOutcome == true);
		}

		/// <summary>
		/// Projects a plugin activation or deactivation request.
		/// </summary>
		/// <param name="p_saoOperation">The plugin activation operation.</param>
		private void ApplyPluginActivation(SetPluginActivationOperation p_saoOperation)
		{
			EnsurePluginSnapshot();
			string strPlugin = NormalizeRelativePath(p_saoOperation.PluginPath);
			if (!ContainsPlugin(strPlugin) && IsPotentialPluginPath(strPlugin) && DataFileExists(strPlugin) && FindEffectivePlugin(strPlugin) != null)
			{
				m_lstManagedPlugins.Add(strPlugin);
				ApplyPolicyCorrectedPluginOrder();
			}
			if (!ContainsPlugin(strPlugin))
				return;

			// Do not short-circuit protected plugins here. The plugin manager normalizes protected plugins
			// to their required active state when resolving the requested snapshot.
			List<string> lstPreviousOrder = new List<string>(m_lstManagedPlugins);
			HashSet<string> hstPreviousActivePlugins = new HashSet<string>(m_hstActivePlugins, StringComparer.OrdinalIgnoreCase);
			if (p_saoOperation.Activate)
				m_hstActivePlugins.Add(strPlugin);
			else
				m_hstActivePlugins.Remove(strPlugin);

			NormalizeProjectedPluginState(lstPreviousOrder, hstPreviousActivePlugins);
		}

		/// <summary>
		/// Projects an absolute plugin-order change while honoring policy restrictions for registered plugins.
		/// </summary>
		/// <param name="p_soiOperation">The plugin-order operation.</param>
		private void ApplyPluginOrderIndex(SetPluginOrderIndexOperation p_soiOperation)
		{
			EnsurePluginSnapshot();
			TryMovePluginToIndex(NormalizeRelativePath(p_soiOperation.PluginPath), p_soiOperation.NewIndex);
		}

		/// <summary>
		/// Projects the legacy request to move selected plugin indices to a load-order position.
		/// </summary>
		/// <param name="p_mloOperation">The plugin move operation.</param>
		private void ApplyMovePlugins(MovePluginsInLoadOrderOperation p_mloOperation)
		{
			EnsurePluginSnapshot();
			List<string> lstOriginal = new List<string>(m_lstManagedPlugins);
			int[] intPlugins = p_mloOperation.PluginIndices.ToArray();
			Array.Sort(intPlugins);

			foreach (int intIndex in intPlugins)
				if ((intIndex < 0) || (intIndex >= lstOriginal.Count))
					throw new IndexOutOfRangeException("A plugin index was out of range");

			// Replay the same sequence of SetPluginOrderIndex calls used by the legacy implementation so policy-rejected
			// moves leave the subsequent projected indices in the same state that the immediate path would observe.
			int intLoadOrder = 0;
			for (int i = 0; i < p_mloOperation.Position; i++)
			{
				if (Array.BinarySearch(intPlugins, i) >= 0)
					continue;
				TryMovePluginToIndex(lstOriginal[i], intLoadOrder++);
			}
			foreach (int intIndex in intPlugins)
				TryMovePluginToIndex(lstOriginal[intIndex], intLoadOrder++);
			for (int i = p_mloOperation.Position; i < lstOriginal.Count; i++)
			{
				if (Array.BinarySearch(intPlugins, i) >= 0)
					continue;
				TryMovePluginToIndex(lstOriginal[i], intLoadOrder++);
			}
		}

		/// <summary>
		/// Projects relative plugin ordering while preserving the positions of unrelated plugins where possible.
		/// </summary>
		/// <param name="p_rloOperation">The relative load-order operation.</param>
		private void ApplyRelativeLoadOrder(SetRelativeLoadOrderOperation p_rloOperation)
		{
			EnsurePluginSnapshot();
			if ((p_rloOperation.PluginPaths == null) || (p_rloOperation.PluginPaths.Count == 0))
				return;

			string strCurrent = null;
			foreach (string strRequestedPlugin in p_rloOperation.PluginPaths)
			{
				string strPlugin = NormalizeRelativePath(strRequestedPlugin);
				if (!ContainsPlugin(strPlugin))
					continue;

				if (strCurrent == null)
				{
					strCurrent = strPlugin;
					continue;
				}

				int intCurrentIndex = IndexOfPlugin(strCurrent);
				int intNextIndex = IndexOfPlugin(strPlugin);
				if (intNextIndex > intCurrentIndex)
				{
					strCurrent = strPlugin;
					continue;
				}

				if (TryMovePluginToIndex(strPlugin, intCurrentIndex + 1))
					strCurrent = strPlugin;
			}
		}

		/// <summary>
		/// Validates a legacy full load-order operation against the projected plugin snapshot.
		/// </summary>
		/// <param name="p_sloOperation">The legacy load-order operation.</param>
		private void ValidateLegacyLoadOrder(SetLoadOrderOperation p_sloOperation)
		{
			EnsurePluginSnapshot();
			if ((p_sloOperation.PluginIndices == null) || (p_sloOperation.PluginIndices.Count != m_lstManagedPlugins.Count))
				throw new ArgumentException("Length of new load order array was different to the total number of plugins");

			foreach (int intIndex in p_sloOperation.PluginIndices)
				if ((intIndex < 0) || (intIndex >= p_sloOperation.PluginIndices.Count))
					throw new IndexOutOfRangeException("A plugin index was out of range");
		}

		#endregion

		#region Data File State

		/// <summary>
		/// Determines whether a data file exists in the effective projected installation state.
		/// </summary>
		/// <param name="p_strPath">The game-relative data-file path.</param>
		/// <returns><c>true</c> when the file exists either in the current installation or in the projected overlay.</returns>
		public bool DataFileExists(string p_strPath)
		{
			string strPath = GetAdjustedDataPath(p_strPath);
			return m_dicProjectedFiles.ContainsKey(strPath) || m_igpInstallers.DataFileUtility.DataFileExists(strPath);
		}

		/// <summary>
		/// Gets a data file from the effective projected installation state.
		/// </summary>
		/// <param name="p_strPath">The game-relative data-file path.</param>
		/// <returns>The projected contents when the file has been planned, otherwise the current installed contents.</returns>
		public byte[] GetExistingDataFile(string p_strPath)
		{
			string strPath = GetAdjustedDataPath(p_strPath);
			ProjectedDataFile pdfFile;
			return m_dicProjectedFiles.TryGetValue(strPath, out pdfFile) ? pdfFile.GetData() : m_igpInstallers.DataFileUtility.GetExistingDataFile(strPath);
		}

		/// <summary>
		/// Gets the files visible in a directory after merging current files with planned file operations.
		/// </summary>
		/// <param name="p_strPath">The game-relative directory path.</param>
		/// <param name="p_strPattern">The file-name pattern used to filter results.</param>
		/// <param name="p_booAllFolders">Whether subdirectories should be included.</param>
		/// <returns>The merged file list using the same absolute-path shape returned by the current data-file utility.</returns>
		public string[] GetExistingDataFileList(string p_strPath, string p_strPattern, bool p_booAllFolders)
		{
			string strPath = GetAdjustedDataPath(p_strPath);
			HashSet<string> hstFiles;
			DirectoryNotFoundException dnfBaselineException = null;
			try
			{
				hstFiles = new HashSet<string>(m_igpInstallers.DataFileUtility.GetExistingDataFileList(strPath, p_strPattern, p_booAllFolders), StringComparer.OrdinalIgnoreCase);
			}
			catch (DirectoryNotFoundException ex)
			{
				// A deferred operation may create the requested directory even though it is absent from the current installation.
				dnfBaselineException = ex;
				hstFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}

			int intProjectedMatchCount = 0;
			// Baseline results already satisfy the caller's filter; only projected additions need local filtering.
			foreach (string strProjectedPath in m_dicProjectedFiles.Keys)
			{
				if (!IsPathInDirectory(strProjectedPath, strPath, p_booAllFolders) || !MatchesPattern(Path.GetFileName(strProjectedPath), p_strPattern))
					continue;

				string strInstallationPath = m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath.TrimEnd(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				hstFiles.Add(strInstallationPath + Path.DirectorySeparatorChar + strProjectedPath);
				intProjectedMatchCount++;
			}
			if ((dnfBaselineException != null) && (intProjectedMatchCount == 0))
				throw dnfBaselineException;

			return hstFiles.ToArray();
		}

		#endregion

		#region INI State

		/// <summary>
		/// Gets an INI value from the effective projected installation state.
		/// </summary>
		/// <param name="p_strSettingsFileName">The settings file name.</param>
		/// <param name="p_strSection">The INI section.</param>
		/// <param name="p_strKey">The INI key.</param>
		/// <returns>The projected value when one has been planned; otherwise the current installed value.</returns>
		public string GetIniString(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			string strValue;
			return m_dicIniValues.TryGetValue(GetIniKey(p_strSettingsFileName, p_strSection, p_strKey), out strValue)
				? strValue
				: m_igpInstallers.IniInstaller.GetIniString(p_strSettingsFileName, p_strSection, p_strKey);
		}

		/// <summary>
		/// Gets an integer INI value from the effective projected installation state.
		/// </summary>
		/// <param name="p_strSettingsFileName">The settings file name.</param>
		/// <param name="p_strSection">The INI section.</param>
		/// <param name="p_strKey">The INI key.</param>
		/// <returns>The projected integer value, or the current installed value when no projected edit exists.</returns>
		public int GetIniInt(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			string strValue;
			if (!m_dicIniValues.TryGetValue(GetIniKey(p_strSettingsFileName, p_strSection, p_strKey), out strValue))
				return m_igpInstallers.IniInstaller.GetIniInt(p_strSettingsFileName, p_strSection, p_strKey);

			return Int32.Parse(strValue);
		}

		#endregion

		#region Plugin State

		/// <summary>
		/// Gets all plugins visible in the projected managed-plugin snapshot.
		/// </summary>
		/// <returns>The projected managed plugins in load-order sequence.</returns>
		public string[] GetAllPlugins()
		{
			EnsurePluginSnapshot();
			return m_lstManagedPlugins.ToArray();
		}

		/// <summary>
		/// Gets all plugins active in the projected plugin snapshot.
		/// </summary>
		/// <returns>The projected active plugins in load-order sequence.</returns>
		public string[] GetActivePlugins()
		{
			EnsurePluginSnapshot();
			return m_lstManagedPlugins.Where(p_strPlugin => m_hstActivePlugins.Contains(p_strPlugin)).ToArray();
		}

		/// <summary>
		/// Initializes the projected plugin snapshot from the current plugin manager when first required.
		/// </summary>
		private void EnsurePluginSnapshot()
		{
			if (m_lstManagedPlugins != null)
				return;

			m_lstManagedPlugins = RelativizePluginPaths(m_igpInstallers.PluginManager.ManagedPlugins).ToList();
			m_hstActivePlugins = new HashSet<string>(RelativizePluginPaths(m_igpInstallers.PluginManager.ActivePlugins), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Adds a planned plugin file to the projected managed-plugin snapshot when its extension is supported by the game mode.
		/// </summary>
		/// <param name="p_strDestinationPath">The logical destination path of the planned file.</param>
		/// <param name="p_booActivate">Whether the legacy file-link path requests activation after registration.</param>
		private void RegisterProjectedPlugin(string p_strDestinationPath, bool p_booActivate)
		{
			string strPlugin = NormalizeRelativePath(p_strDestinationPath);
			if (!IsPotentialPluginPath(strPlugin))
				return;

			// A plugin deployed by InstallFileFromMod is registered and activation is requested by the legacy link path.
			// Generated files are registered as plugins but are not automatically activated.
			EnsurePluginSnapshot();
			if (!ContainsPlugin(strPlugin))
			{
				m_dicProjectedPluginInfo.Remove(strPlugin);
				if (FindEffectivePlugin(strPlugin) == null)
					return;

				m_lstManagedPlugins.Add(strPlugin);
				ApplyPolicyCorrectedPluginOrder();
			}

			if (p_booActivate)
				ApplyPluginActivation(new SetPluginActivationOperation(strPlugin, true));
		}

		/// <summary>
		/// Applies the same policy-corrected ordering used when the real plugin manager registers a new plugin.
		/// </summary>
		private void ApplyPolicyCorrectedPluginOrder()
		{
			List<Plugin> lstEffectivePlugins = new List<Plugin>();
			foreach (string strPlugin in m_lstManagedPlugins)
			{
				Plugin plgPlugin = FindEffectivePlugin(strPlugin);
				if (plgPlugin == null)
					return;
				lstEffectivePlugins.Add(plgPlugin);
			}

			IList<Plugin> lstResolvedOrder = m_igpInstallers.PluginManager.ResolvePluginOrder(lstEffectivePlugins);
			if (lstResolvedOrder != null)
				m_lstManagedPlugins = RelativizePluginPaths(lstResolvedOrder).ToList();
		}

		/// <summary>
		/// Validates and policy-corrects the current projected plugin snapshot when all projected plugins are registered.
		/// </summary>
		/// <param name="p_lstPreviousOrder">The plugin order to restore when the requested state is rejected.</param>
		/// <param name="p_hstPreviousActivePlugins">The active plugin set to restore when the requested state is rejected.</param>
		private void NormalizeProjectedPluginState(List<string> p_lstPreviousOrder, HashSet<string> p_hstPreviousActivePlugins)
		{
			List<Plugin> lstPreviousOrderedPlugins;
			List<Plugin> lstPreviousActivePlugins;
			List<Plugin> lstRequestedOrderedPlugins;
			List<Plugin> lstRequestedActivePlugins;
			if (!TryResolveEffectivePlugins(p_lstPreviousOrder, p_hstPreviousActivePlugins, out lstPreviousOrderedPlugins, out lstPreviousActivePlugins) ||
				!TryResolveEffectivePlugins(m_lstManagedPlugins, m_hstActivePlugins, out lstRequestedOrderedPlugins, out lstRequestedActivePlugins))
				return;

			PluginStateResolution psrResolution = m_igpInstallers.PluginManager.ResolvePluginState(lstPreviousOrderedPlugins, lstPreviousActivePlugins, lstRequestedOrderedPlugins, lstRequestedActivePlugins);
			if (psrResolution == null)
				return;
			if (!psrResolution.IsAllowed)
			{
				m_lstManagedPlugins = p_lstPreviousOrder;
				m_hstActivePlugins = p_hstPreviousActivePlugins;
				return;
			}

			m_lstManagedPlugins = RelativizePluginPaths(psrResolution.OrderedPlugins).ToList();
			m_hstActivePlugins = new HashSet<string>(RelativizePluginPaths(psrResolution.ActivePlugins), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Resolves a projected plugin order and active set to effective plugin instances, including plugins planned by the current script.
		/// </summary>
		/// <param name="p_lstOrderedPluginPaths">The projected ordered plugin paths.</param>
		/// <param name="p_hstActivePluginPaths">The projected active plugin paths.</param>
		/// <param name="p_lstOrderedPlugins">Receives the corresponding registered plugin order.</param>
		/// <param name="p_lstActivePlugins">Receives the corresponding registered active plugin set.</param>
		/// <returns><c>true</c> when every projected plugin can be represented for policy evaluation; otherwise, <c>false</c>.</returns>
		private bool TryResolveEffectivePlugins(IEnumerable<string> p_lstOrderedPluginPaths, IEnumerable<string> p_hstActivePluginPaths, out List<Plugin> p_lstOrderedPlugins, out List<Plugin> p_lstActivePlugins)
		{
			p_lstOrderedPlugins = new List<Plugin>();
			p_lstActivePlugins = new List<Plugin>();
			foreach (string strPlugin in p_lstOrderedPluginPaths)
			{
				Plugin plgPlugin = FindEffectivePlugin(strPlugin);
				if (plgPlugin == null)
					return false;
				p_lstOrderedPlugins.Add(plgPlugin);
			}

			foreach (string strPlugin in p_hstActivePluginPaths)
			{
				Plugin plgPlugin = FindEffectivePlugin(strPlugin);
				if (plgPlugin == null)
					return false;
				p_lstActivePlugins.Add(plgPlugin);
			}
			return true;
		}

		/// <summary>
		/// Resolves a projected script-visible plugin path to the currently registered plugin instance.
		/// </summary>
		/// <param name="p_strPluginPath">The script-visible relative plugin path.</param>
		/// <returns>The matching registered plugin, or <c>null</c> when the plugin exists only in projected state.</returns>
		private Plugin FindRegisteredPlugin(string p_strPluginPath)
		{
			string strAdjustedPath = m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, p_strPluginPath, false);
			string strAbsolutePath = Path.Combine(m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath, strAdjustedPath);
			return m_igpInstallers.PluginManager.ManagedPlugins.FirstOrDefault(p_plgPlugin => p_plgPlugin != null && String.Equals(p_plgPlugin.Filename, strAbsolutePath, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Resolves a plugin from either the current registry or the projected file overlay.
		/// </summary>
		/// <param name="p_strPluginPath">The script-visible relative plugin path.</param>
		/// <returns>The effective plugin information used for policy evaluation, or <c>null</c> when the file cannot be represented as a plugin.</returns>
		private Plugin FindEffectivePlugin(string p_strPluginPath)
		{
			Plugin plgRegisteredPlugin = FindRegisteredPlugin(p_strPluginPath);
			if (plgRegisteredPlugin != null)
				return plgRegisteredPlugin;

			string strPlugin = NormalizeRelativePath(p_strPluginPath);
			Plugin plgProjectedPlugin;
			if (m_dicProjectedPluginInfo.TryGetValue(strPlugin, out plgProjectedPlugin))
				return plgProjectedPlugin;

			string strAdjustedPath = GetAdjustedDataPath(strPlugin);
			ProjectedDataFile pdfProjectedFile;
			if (!m_dicProjectedFiles.TryGetValue(strAdjustedPath, out pdfProjectedFile))
				return null;

			plgProjectedPlugin = CreateProjectedPlugin(strPlugin, pdfProjectedFile.GetData());
			if (plgProjectedPlugin != null)
				m_dicProjectedPluginInfo[strPlugin] = plgProjectedPlugin;
			return plgProjectedPlugin;
		}

		/// <summary>
		/// Creates non-registered plugin information from projected file content without modifying the game installation or plugin registry.
		/// </summary>
		/// <param name="p_strPluginPath">The script-visible relative plugin path.</param>
		/// <param name="p_bteData">The projected plugin contents.</param>
		/// <returns>A plugin instance whose filename represents the eventual deployment path, or <c>null</c> when the projected file is not an activatable plugin.</returns>
		private Plugin CreateProjectedPlugin(string p_strPluginPath, byte[] p_bteData)
		{
			if (p_bteData == null)
				return null;

			IPluginFactory pgfPluginFactory = m_gmdGameMode.GetPluginFactory();
			if (pgfPluginFactory == null)
				return null;

			string strAdjustedPath = m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, p_strPluginPath, false);
			string strAbsolutePath = Path.Combine(m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath, strAdjustedPath);
			string strTemporaryDirectory = Path.Combine(Path.GetTempPath(), "NMMCE", "ProjectedPlugins", Guid.NewGuid().ToString("N"));
			string strTemporaryPath = Path.Combine(strTemporaryDirectory, Path.GetFileName(strAbsolutePath));

			try
			{
				Directory.CreateDirectory(strTemporaryDirectory);
				File.WriteAllBytes(strTemporaryPath, p_bteData);

				Plugin plgParsedPlugin = pgfPluginFactory.CreatePlugin(strTemporaryPath);
				if (plgParsedPlugin == null)
					return null;

				// Policy evaluation depends on the eventual game path for critical/fixed plugin rules,
				// while metadata and masters must come from the projected file contents.
				Plugin plgProjectedPlugin = new Plugin(strAbsolutePath, plgParsedPlugin.Description, null);
				plgProjectedPlugin.SetMetadata(plgParsedPlugin.Metadata);
				return plgProjectedPlugin;
			}
			finally
			{
				try
				{
					if (Directory.Exists(strTemporaryDirectory))
						Directory.Delete(strTemporaryDirectory, true);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}

		/// <summary>
		/// Determines whether a logical file path uses one of the plugin extensions declared by the current game mode.
		/// </summary>
		/// <param name="p_strPath">The logical file path.</param>
		/// <returns><c>true</c> when the path has a configured plugin extension; otherwise, <c>false</c>.</returns>
		private bool IsPotentialPluginPath(string p_strPath)
		{
			string strExtension = Path.GetExtension(p_strPath);
			if ((m_gmdGameMode.PluginExtensions == null) || !m_gmdGameMode.PluginExtensions.Any(p_strExtension => String.Equals(p_strExtension, strExtension, StringComparison.OrdinalIgnoreCase)))
				return false;

			string strPluginDirectory = m_gmdGameMode.PluginDirectory;
			if (String.IsNullOrWhiteSpace(strPluginDirectory))
				return true;

			string strAdjustedPath = GetAdjustedDataPath(p_strPath);
			string strAbsolutePath = Path.GetFullPath(Path.Combine(m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath, strAdjustedPath));
			return String.Equals(Path.GetDirectoryName(strAbsolutePath), Path.GetFullPath(strPluginDirectory).TrimEnd(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Converts plugin filenames into the relative path shape exposed by the scripted installer API.
		/// </summary>
		/// <param name="p_enmPlugins">The plugins whose paths should be converted.</param>
		/// <returns>The relative plugin paths in their original sequence.</returns>
		private IEnumerable<string> RelativizePluginPaths(IEnumerable<Plugin> p_enmPlugins)
		{
			if (p_enmPlugins == null)
				yield break;

			string strAdjustedRoot = m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, null, false) ?? String.Empty;
			string strInstallationPath = Path.Combine(m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath, strAdjustedRoot);
			string strNormalizedRoot = strInstallationPath.TrimEnd(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			foreach (Plugin plgPlugin in p_enmPlugins)
			{
				if (plgPlugin == null || String.IsNullOrEmpty(plgPlugin.Filename))
					continue;
				// Managed plugins normally live under the adjusted data root; the filename fallback protects unusual registries.
				if (plgPlugin.Filename.StartsWith(strNormalizedRoot, StringComparison.OrdinalIgnoreCase))
					yield return NormalizeRelativePath(plgPlugin.Filename.Substring(strNormalizedRoot.Length));
				else
					yield return NormalizeRelativePath(Path.GetFileName(plgPlugin.Filename));
			}
		}

		/// <summary>
		/// Attempts to move a projected plugin through the same policy restrictions used by the real plugin manager.
		/// </summary>
		/// <param name="p_strPlugin">The plugin path to move.</param>
		/// <param name="p_intNewIndex">The requested load-order index.</param>
		/// <returns><c>true</c> when the plugin's projected index changed; otherwise, <c>false</c>.</returns>
		private bool TryMovePluginToIndex(string p_strPlugin, int p_intNewIndex)
		{
			int intPreviousIndex = IndexOfPlugin(p_strPlugin);
			if (intPreviousIndex < 0)
				return false;

			Plugin plgEffectivePlugin = FindEffectivePlugin(p_strPlugin);
			if ((plgEffectivePlugin != null) && !m_igpInstallers.PluginManager.CanChangePluginOrder(plgEffectivePlugin))
				return false;

			List<string> lstPreviousOrder = new List<string>(m_lstManagedPlugins);
			HashSet<string> hstPreviousActivePlugins = new HashSet<string>(m_hstActivePlugins, StringComparer.OrdinalIgnoreCase);
			MovePluginToIndex(p_strPlugin, p_intNewIndex);
			NormalizeProjectedPluginState(lstPreviousOrder, hstPreviousActivePlugins);
			return IndexOfPlugin(p_strPlugin) != intPreviousIndex;
		}

		/// <summary>
		/// Moves a projected plugin to the requested load-order index.
		/// </summary>
		/// <param name="p_strPlugin">The plugin path to move.</param>
		/// <param name="p_intNewIndex">The requested load-order index.</param>
		private void MovePluginToIndex(string p_strPlugin, int p_intNewIndex)
		{
			int intCurrentIndex = IndexOfPlugin(p_strPlugin);
			if (intCurrentIndex < 0)
				return;

			string strPlugin = m_lstManagedPlugins[intCurrentIndex];
			m_lstManagedPlugins.RemoveAt(intCurrentIndex);
			int intNewIndex = Math.Max(0, Math.Min(p_intNewIndex, m_lstManagedPlugins.Count));
			m_lstManagedPlugins.Insert(intNewIndex, strPlugin);
		}

		/// <summary>
		/// Gets the current projected index of a plugin path.
		/// </summary>
		/// <param name="p_strPlugin">The plugin path to locate.</param>
		/// <returns>The projected index, or <c>-1</c> when the plugin is not present.</returns>
		private int IndexOfPlugin(string p_strPlugin)
		{
			for (int i = 0; i < m_lstManagedPlugins.Count; i++)
				if (String.Equals(m_lstManagedPlugins[i], p_strPlugin, StringComparison.OrdinalIgnoreCase))
					return i;
			return -1;
		}

		/// <summary>
		/// Determines whether a plugin exists in the projected managed-plugin snapshot.
		/// </summary>
		/// <param name="p_strPlugin">The plugin path to locate.</param>
		/// <returns><c>true</c> when the plugin is present; otherwise, <c>false</c>.</returns>
		private bool ContainsPlugin(string p_strPlugin)
		{
			return IndexOfPlugin(p_strPlugin) >= 0;
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Converts a script-visible data path into the game-mode-adjusted path used by the current data-file utility.
		/// </summary>
		/// <param name="p_strPath">The script-visible data path.</param>
		/// <returns>The normalized, game-mode-adjusted relative path.</returns>
		private string GetAdjustedDataPath(string p_strPath)
		{
			string strPath = (p_strPath ?? String.Empty).Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return NormalizeRelativePath(m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, strPath, false));
		}

		/// <summary>
		/// Normalizes a relative path for case-insensitive projected-state comparisons.
		/// </summary>
		/// <param name="p_strPath">The path to normalize.</param>
		/// <returns>The normalized relative path.</returns>
		private static string NormalizeRelativePath(string p_strPath)
		{
			return (p_strPath ?? String.Empty).Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
		}

		/// <summary>
		/// Builds a case-insensitive dictionary key for a projected INI value.
		/// </summary>
		/// <param name="p_strSettingsFileName">The settings file name.</param>
		/// <param name="p_strSection">The INI section.</param>
		/// <param name="p_strKey">The INI key.</param>
		/// <returns>The composite projected-state key.</returns>
		private static string GetIniKey(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			return String.Concat(p_strSettingsFileName ?? String.Empty, "\0", p_strSection ?? String.Empty, "\0", p_strKey ?? String.Empty);
		}

		/// <summary>
		/// Determines whether a projected relative path belongs to the requested directory scope.
		/// </summary>
		/// <param name="p_strFilePath">The projected relative file path.</param>
		/// <param name="p_strDirectoryPath">The requested relative directory path.</param>
		/// <param name="p_booAllFolders">Whether subdirectories should be included.</param>
		/// <returns><c>true</c> when the file belongs to the requested scope; otherwise, <c>false</c>.</returns>
		private static bool IsPathInDirectory(string p_strFilePath, string p_strDirectoryPath, bool p_booAllFolders)
		{
			string strDirectory = NormalizeRelativePath(p_strDirectoryPath).TrimEnd(Path.DirectorySeparatorChar);
			string strNormalizedFilePath = NormalizeRelativePath(p_strFilePath);
			int intSeparatorIndex = strNormalizedFilePath.LastIndexOf(Path.DirectorySeparatorChar);
			string strFileDirectory = intSeparatorIndex < 0 ? String.Empty : strNormalizedFilePath.Substring(0, intSeparatorIndex).TrimEnd(Path.DirectorySeparatorChar);
			if (!p_booAllFolders)
				return String.Equals(strFileDirectory, strDirectory, StringComparison.OrdinalIgnoreCase);
			if (String.IsNullOrEmpty(strDirectory))
				return true;
			return String.Equals(strFileDirectory, strDirectory, StringComparison.OrdinalIgnoreCase) || strFileDirectory.StartsWith(strDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Applies a simple Windows-style wildcard pattern to a file name.
		/// </summary>
		/// <param name="p_strFileName">The file name to evaluate.</param>
		/// <param name="p_strPattern">The wildcard pattern supplied by the script.</param>
		/// <returns><c>true</c> when the file name matches the requested pattern; otherwise, <c>false</c>.</returns>
		private static bool MatchesPattern(string p_strFileName, string p_strPattern)
		{
			string strPattern = String.IsNullOrEmpty(p_strPattern) ? "*" : p_strPattern;
			if (strPattern.Equals("*", StringComparison.Ordinal) || strPattern.Equals("*.*", StringComparison.Ordinal))
				return true;

			string strRegex = "^" + Regex.Escape(strPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
			return Regex.IsMatch(p_strFileName ?? String.Empty, strRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		}

		#endregion

		/// <summary>
		/// Stores either generated file data or a lazy archive-file resolver for a projected data file.
		/// </summary>
		private sealed class ProjectedDataFile
		{
			private readonly Func<byte[]> m_fncDataResolver;
			private byte[] m_bteData;
			private bool m_booResolved;

			/// <summary>
			/// Initializes a projected file backed by an already available data buffer.
			/// </summary>
			/// <param name="p_bteData">The projected file contents.</param>
			public ProjectedDataFile(byte[] p_bteData)
			{
				m_bteData = p_bteData == null ? null : (byte[])p_bteData.Clone();
				m_booResolved = true;
			}

			/// <summary>
			/// Initializes a projected file whose archive data will be resolved on first access.
			/// </summary>
			/// <param name="p_fncDataResolver">The archive data resolver.</param>
			public ProjectedDataFile(Func<byte[]> p_fncDataResolver)
			{
				if (p_fncDataResolver == null)
					throw new ArgumentNullException(nameof(p_fncDataResolver));
				m_fncDataResolver = p_fncDataResolver;
			}

			/// <summary>
			/// Gets the projected file contents, resolving archive-backed data only when required.
			/// </summary>
			/// <returns>The projected file contents.</returns>
			public byte[] GetData()
			{
				if (!m_booResolved)
				{
					byte[] bteResolvedData = m_fncDataResolver();
					m_bteData = bteResolvedData == null ? null : (byte[])bteResolvedData.Clone();
					m_booResolved = true;
				}
				return m_bteData == null ? null : (byte[])m_bteData.Clone();
			}
		}
	}
}
