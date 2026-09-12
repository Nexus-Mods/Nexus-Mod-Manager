namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.IO;

	using ChinhDo.Transactions;
	using Nexus.Client.Games;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using Nexus.Client.PluginManagement;

	/// <summary>
	/// Streams files from a basic mod archive directly to their final game destinations.
	/// </summary>
	public sealed class DirectModFileInstaller : IModFileInstaller, IModFileInstallDecisionSupport
	{
		private readonly IMod m_modMod;
		private readonly IMod m_modDeploymentOwner;
		private readonly IGameMode m_gmdGameMode;
		private readonly IInstallLog m_ilgInstallLog;
		private readonly IModDeploymentManager m_mdmDeploymentManager;
		private readonly IPluginManager m_pmgPluginManager;
		private readonly TxFileManager m_tfmFileManager;
		private readonly IEnvironmentInfo m_eifEnvironmentInfo;
		private readonly ModInstallContext m_micInstallContext;
		private readonly ModDeploymentOverwriteResolver m_dorOverwriteResolver;
		private readonly bool m_booUpgrade;
		private readonly HashSet<ModDeploymentTarget> m_hstUpgradeOwnedTargets;
		private readonly HashSet<ModDeploymentTarget> m_hstUpgradeRemainingTargets;
		private readonly HashSet<string> m_hstDeployedPluginPaths =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> m_hstRemovedPluginPaths =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Initializes a Direct file installer for one immutable install context.
		/// </summary>
		public DirectModFileInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IInstallLog p_ilgInstallLog,
			IModDeploymentManager p_mdmDeploymentManager, IPluginManager p_pmgPluginManager,
			TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate,
			IEnvironmentInfo p_eifEnvironmentInfo, ModInstallContext p_micInstallContext)
			: this(p_modMod, null, p_gmdGameMode, p_ilgInstallLog, p_mdmDeploymentManager, p_pmgPluginManager, p_tfmFileManager,
				p_eifEnvironmentInfo, p_micInstallContext,
				new ModDeploymentOverwriteResolver(p_modMod, p_ilgInstallLog, p_mdmDeploymentManager,
					p_dlgOverwriteConfirmationDelegate), false)
		{
		}

		/// <summary>
		/// Initializes a Direct file installer for an in-place upgrade that preserves existing owner precedence.
		/// </summary>
		public DirectModFileInstaller(IMod p_modMod, IMod p_modDeploymentOwner, IGameMode p_gmdGameMode, IInstallLog p_ilgInstallLog,
			IModDeploymentManager p_mdmDeploymentManager, IPluginManager p_pmgPluginManager,
			TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate,
			IEnvironmentInfo p_eifEnvironmentInfo, ModInstallContext p_micInstallContext, bool p_booUpgrade)
			: this(p_modMod, p_modDeploymentOwner, p_gmdGameMode, p_ilgInstallLog, p_mdmDeploymentManager, p_pmgPluginManager, p_tfmFileManager,
				p_eifEnvironmentInfo, p_micInstallContext,
				new ModDeploymentOverwriteResolver(p_modDeploymentOwner ?? p_modMod, p_ilgInstallLog, p_mdmDeploymentManager,
					p_dlgOverwriteConfirmationDelegate), p_booUpgrade)
		{
		}

		/// <summary>
		/// Initializes a Direct file installer with the shared overwrite state for the operation.
		/// </summary>
		public DirectModFileInstaller(IMod p_modMod, IGameMode p_gmdGameMode,
			IModDeploymentManager p_mdmDeploymentManager, IPluginManager p_pmgPluginManager,
			TxFileManager p_tfmFileManager, IEnvironmentInfo p_eifEnvironmentInfo,
			ModInstallContext p_micInstallContext, ModDeploymentOverwriteResolver p_dorOverwriteResolver)
			: this(p_modMod, null, p_gmdGameMode, null, p_mdmDeploymentManager, p_pmgPluginManager, p_tfmFileManager,
				p_eifEnvironmentInfo, p_micInstallContext, p_dorOverwriteResolver, false)
		{
		}

		private DirectModFileInstaller(IMod p_modMod, IMod p_modDeploymentOwner, IGameMode p_gmdGameMode, IInstallLog p_ilgInstallLog,
			IModDeploymentManager p_mdmDeploymentManager, IPluginManager p_pmgPluginManager,
			TxFileManager p_tfmFileManager, IEnvironmentInfo p_eifEnvironmentInfo,
			ModInstallContext p_micInstallContext, ModDeploymentOverwriteResolver p_dorOverwriteResolver, bool p_booUpgrade)
		{
			m_modMod = p_modMod ?? throw new ArgumentNullException(nameof(p_modMod));
			m_modDeploymentOwner = p_modDeploymentOwner ?? m_modMod;
			m_gmdGameMode = p_gmdGameMode ?? throw new ArgumentNullException(nameof(p_gmdGameMode));
			m_ilgInstallLog = p_ilgInstallLog;
			m_mdmDeploymentManager = p_mdmDeploymentManager ?? throw new ArgumentNullException(nameof(p_mdmDeploymentManager));
			m_pmgPluginManager = p_pmgPluginManager;
			m_tfmFileManager = p_tfmFileManager ?? throw new ArgumentNullException(nameof(p_tfmFileManager));
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_micInstallContext = p_micInstallContext ?? throw new ArgumentNullException(nameof(p_micInstallContext));
			m_dorOverwriteResolver = p_dorOverwriteResolver ?? throw new ArgumentNullException(nameof(p_dorOverwriteResolver));
			m_booUpgrade = p_booUpgrade;
			if (m_micInstallContext.Method != ModInstallMethod.Direct)
				throw new ArgumentException("A Direct file installer requires a Direct install context.", nameof(p_micInstallContext));
			if (m_booUpgrade && m_ilgInstallLog == null)
				throw new ArgumentNullException(nameof(p_ilgInstallLog));

			if (m_booUpgrade)
			{
				string modKey = m_ilgInstallLog.GetModKey(m_modDeploymentOwner);
				if (string.IsNullOrEmpty(modKey))
					throw new InvalidOperationException("The existing Direct deployment owner is not registered in InstallLog.");
				m_hstUpgradeOwnedTargets = new HashSet<ModDeploymentTarget>(
					m_ilgInstallLog.GetDeploymentTargetsForMod(modKey));
				m_hstUpgradeRemainingTargets = new HashSet<ModDeploymentTarget>(m_hstUpgradeOwnedTargets);
			}
		}

		/// <inheritdoc />
		public List<string> InstallErrors { get; } = new List<string>();

		/// <inheritdoc />
		public bool InstallFileFromMod(string p_strModFilePath, string p_strInstallPath)
		{
			ModDeploymentTarget target = ResolveTarget(p_strInstallPath);
			if (!IsExistingUpgradeTarget(target) && !m_dorOverwriteResolver.ShouldActivate(target))
				return false;
			return InstallFileFromModResolved(p_strModFilePath, target, true);
		}

		/// <summary>
		/// Resolves the shared deployment overwrite decision for a Direct destination.
		/// </summary>
		public bool ResolveDataFileOverwrite(string p_strPath)
		{
			ModDeploymentTarget target = ResolveTarget(p_strPath);
			return IsExistingUpgradeTarget(target) || m_dorOverwriteResolver.ShouldActivate(target);
		}

		/// <summary>
		/// Installs an archive file directly after its overwrite decision has already been resolved.
		/// </summary>
		public bool InstallFileFromModWithResolvedOverwrite(string p_strModFilePath, string p_strInstallPath)
		{
			return InstallFileFromModResolved(p_strModFilePath, ResolveTarget(p_strInstallPath), true);
		}

		/// <summary>
		/// Installs an archive file directly after overwrite resolution with explicit legacy plugin handling.
		/// </summary>
		public bool InstallFileFromModWithResolvedOverwrite(string p_strModFilePath, string p_strInstallPath, bool p_booHandlePlugin)
		{
			return InstallFileFromModResolved(p_strModFilePath, ResolveTarget(p_strInstallPath), p_booHandlePlugin);
		}

		/// <inheritdoc />
		public bool GenerateDataFile(string p_strPath, byte[] p_bteData)
		{
			ModDeploymentTarget target = ResolveTarget(p_strPath);
			if (!IsExistingUpgradeTarget(target) && !m_dorOverwriteResolver.ShouldActivate(target))
				return false;
			return GenerateDataFileResolved(target, p_bteData);
		}

		/// <summary>
		/// Writes generated bytes directly after the destination overwrite decision has already been resolved.
		/// </summary>
		public bool GenerateDataFileWithResolvedOverwrite(string p_strPath, byte[] p_bteData)
		{
			return GenerateDataFileResolved(ResolveTarget(p_strPath), p_bteData);
		}

		/// <inheritdoc />
		public bool PluginCheck(string p_strPath, bool p_booRemove)
		{
			return m_gmdGameMode.UsesPlugins && m_pmgPluginManager != null &&
				m_pmgPluginManager.IsActivatiblePluginFile(p_strPath);
		}

		/// <inheritdoc />
		public bool UninstallDataFile(string p_strPath)
		{
			throw new NotSupportedException("Direct uninstall must use the deployment registry inverse index.");
		}

		/// <inheritdoc />
		public void FinalizeInstall()
		{
			if (m_booUpgrade && m_hstUpgradeRemainingTargets != null)
			{
				foreach (ModDeploymentTarget target in new List<ModDeploymentTarget>(m_hstUpgradeRemainingTargets))
				{
					string absentPath = m_mdmDeploymentManager.RemoveOwnedTarget(m_modDeploymentOwner, target, m_tfmFileManager);
					if (!string.IsNullOrEmpty(absentPath) && m_gmdGameMode.UsesPlugins && m_pmgPluginManager != null &&
						m_pmgPluginManager.IsActivatiblePluginFile(absentPath))
					{
						m_hstRemovedPluginPaths.Add(absentPath);
					}
				}
				m_hstUpgradeRemainingTargets.Clear();
			}

			if (m_pmgPluginManager != null)
			{
				if (m_hstRemovedPluginPaths.Count > 0)
					m_pmgPluginManager.RemovePlugins(new List<string>(m_hstRemovedPluginPaths));
				if (m_hstDeployedPluginPaths.Count > 0)
					m_pmgPluginManager.IntegrateDeployedPlugins(new List<string>(m_hstDeployedPluginPaths));
			}
			m_hstRemovedPluginPaths.Clear();
			m_hstDeployedPluginPaths.Clear();
		}

		/// <summary>
		/// Resolves a scripted or basic-install destination through the canonical deployment resolver.
		/// </summary>
		private ModDeploymentTarget ResolveTarget(string p_strInstallPath)
		{
			return ModDeploymentTargetResolver.Resolve(m_gmdGameMode, m_modMod, p_strInstallPath, m_micInstallContext.InstallRoot);
		}

		/// <summary>
		/// Streams an archive file to a Direct target after overwrite approval has already been resolved.
		/// </summary>
		private bool InstallFileFromModResolved(string p_strModFilePath, ModDeploymentTarget p_mdtTarget, bool p_booHandlePlugin)
		{
			FileStream stream = null;
			string temporaryFilePath = null;
			try
			{
				stream = m_modMod.GetFileStream(p_strModFilePath);
				if (stream == null)
					throw new IOException(string.Format("Failed to open mod file '{0}' for Direct installation.", p_strModFilePath));

				temporaryFilePath = stream.Name;
				string deployedPath = m_booUpgrade && IsExistingUpgradeTarget(p_mdtTarget)
					? m_mdmDeploymentManager.UpgradeDirectFile(m_modDeploymentOwner, p_mdtTarget, stream, m_tfmFileManager)
					: m_mdmDeploymentManager.InstallDirectFile(m_modDeploymentOwner, p_mdtTarget, stream, m_tfmFileManager);
				m_hstUpgradeRemainingTargets?.Remove(p_mdtTarget);
				if (p_booHandlePlugin && !string.IsNullOrEmpty(deployedPath))
					TrackPlugin(deployedPath);
				return true;
			}
			finally
			{
				if (stream != null)
					stream.Dispose();
				DeleteTemporaryModStreamFile(temporaryFilePath);
			}
		}

		/// <summary>
		/// Writes generated bytes to a Direct target after overwrite approval has already been resolved.
		/// </summary>
		private bool GenerateDataFileResolved(ModDeploymentTarget p_mdtTarget, byte[] p_bteData)
		{
			if (p_bteData == null)
				throw new ArgumentNullException(nameof(p_bteData));

			if (m_booUpgrade && IsExistingUpgradeTarget(p_mdtTarget))
				m_mdmDeploymentManager.UpgradeDirectFile(m_modDeploymentOwner, p_mdtTarget, p_bteData, m_tfmFileManager);
			else
				m_mdmDeploymentManager.InstallDirectFile(m_modDeploymentOwner, p_mdtTarget, p_bteData, m_tfmFileManager);
			m_hstUpgradeRemainingTargets?.Remove(p_mdtTarget);
			return true;
		}

		/// <summary>
		/// Gets whether the target belonged to this Direct mod before the upgrade started.
		/// </summary>
		private bool IsExistingUpgradeTarget(ModDeploymentTarget p_mdtTarget)
		{
			return m_booUpgrade && m_hstUpgradeOwnedTargets != null && m_hstUpgradeOwnedTargets.Contains(p_mdtTarget);
		}

		/// <summary>
		/// Queues plugin registration for archive files that use the legacy handle-plugin path.
		/// </summary>
		private void TrackPlugin(string p_strDeployedPath)
		{
			if (!m_gmdGameMode.UsesPlugins || m_pmgPluginManager == null ||
				!m_pmgPluginManager.IsActivatiblePluginFile(p_strDeployedPath))
				return;

			m_hstDeployedPluginPaths.Add(p_strDeployedPath);
		}

		private void DeleteTemporaryModStreamFile(string p_strFilePath)
		{
			if (string.IsNullOrWhiteSpace(p_strFilePath) || m_eifEnvironmentInfo == null ||
				string.IsNullOrWhiteSpace(m_eifEnvironmentInfo.TemporaryPath))
			{
				return;
			}

			try
			{
				string temporaryRoot = Path.GetFullPath(m_eifEnvironmentInfo.TemporaryPath)
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string filePath = Path.GetFullPath(p_strFilePath);
				if (!filePath.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
					!Path.GetFileName(filePath).StartsWith("tempfile_", StringComparison.OrdinalIgnoreCase))
				{
					return;
				}

				if (File.Exists(filePath))
					File.Delete(filePath);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}
}
