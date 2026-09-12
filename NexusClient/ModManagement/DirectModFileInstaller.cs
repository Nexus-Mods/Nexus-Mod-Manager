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
	public sealed class DirectModFileInstaller : IModFileInstaller
	{
		private readonly IMod m_modMod;
		private readonly IGameMode m_gmdGameMode;
		private readonly IModDeploymentManager m_mdmDeploymentManager;
		private readonly IPluginManager m_pmgPluginManager;
		private readonly TxFileManager m_tfmFileManager;
		private readonly IEnvironmentInfo m_eifEnvironmentInfo;
		private readonly ModInstallContext m_micInstallContext;
		private readonly ModDeploymentOverwriteResolver m_dorOverwriteResolver;
		private readonly HashSet<string> m_hstDeployedPluginPaths =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Initializes a Direct file installer for one immutable install context.
		/// </summary>
		public DirectModFileInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IInstallLog p_ilgInstallLog,
			IModDeploymentManager p_mdmDeploymentManager, IPluginManager p_pmgPluginManager,
			TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate,
			IEnvironmentInfo p_eifEnvironmentInfo, ModInstallContext p_micInstallContext)
			: this(p_modMod, p_gmdGameMode, p_mdmDeploymentManager, p_pmgPluginManager, p_tfmFileManager,
				p_eifEnvironmentInfo, p_micInstallContext,
				new ModDeploymentOverwriteResolver(p_modMod, p_ilgInstallLog, p_mdmDeploymentManager,
					p_dlgOverwriteConfirmationDelegate))
		{
		}

		/// <summary>
		/// Initializes a Direct file installer with the shared overwrite state for the operation.
		/// </summary>
		public DirectModFileInstaller(IMod p_modMod, IGameMode p_gmdGameMode,
			IModDeploymentManager p_mdmDeploymentManager, IPluginManager p_pmgPluginManager,
			TxFileManager p_tfmFileManager, IEnvironmentInfo p_eifEnvironmentInfo,
			ModInstallContext p_micInstallContext, ModDeploymentOverwriteResolver p_dorOverwriteResolver)
		{
			m_modMod = p_modMod ?? throw new ArgumentNullException(nameof(p_modMod));
			m_gmdGameMode = p_gmdGameMode ?? throw new ArgumentNullException(nameof(p_gmdGameMode));
			m_mdmDeploymentManager = p_mdmDeploymentManager ?? throw new ArgumentNullException(nameof(p_mdmDeploymentManager));
			m_pmgPluginManager = p_pmgPluginManager;
			m_tfmFileManager = p_tfmFileManager ?? throw new ArgumentNullException(nameof(p_tfmFileManager));
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_micInstallContext = p_micInstallContext ?? throw new ArgumentNullException(nameof(p_micInstallContext));
			m_dorOverwriteResolver = p_dorOverwriteResolver ?? throw new ArgumentNullException(nameof(p_dorOverwriteResolver));
			if (m_micInstallContext.Method != ModInstallMethod.Direct)
				throw new ArgumentException("A Direct file installer requires a Direct install context.", nameof(p_micInstallContext));
		}

		/// <inheritdoc />
		public List<string> InstallErrors { get; } = new List<string>();

		/// <inheritdoc />
		public bool InstallFileFromMod(string p_strModFilePath, string p_strInstallPath)
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(
				m_gmdGameMode,
				m_modMod,
				p_strInstallPath,
				m_micInstallContext.InstallRoot);
			if (!m_dorOverwriteResolver.ShouldActivate(target))
				return false;

			FileStream stream = null;
			string temporaryFilePath = null;
			try
			{
				stream = m_modMod.GetFileStream(p_strModFilePath);
				if (stream == null)
					throw new IOException(string.Format("Failed to open mod file '{0}' for Direct installation.", p_strModFilePath));

				temporaryFilePath = stream.Name;
				string deployedPath = m_mdmDeploymentManager.InstallDirectFile(m_modMod, target, stream, m_tfmFileManager);
				if (m_gmdGameMode.UsesPlugins && m_pmgPluginManager != null &&
					m_pmgPluginManager.IsActivatiblePluginFile(deployedPath))
				{
					m_hstDeployedPluginPaths.Add(deployedPath);
				}
				return true;
			}
			finally
			{
				if (stream != null)
					stream.Dispose();
				DeleteTemporaryModStreamFile(temporaryFilePath);
			}
		}

		/// <inheritdoc />
		public bool GenerateDataFile(string p_strPath, byte[] p_bteData)
		{
			throw new NotSupportedException("Direct generated-file installation is implemented in Step 5.");
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
			if (m_pmgPluginManager != null && m_hstDeployedPluginPaths.Count > 0)
				m_pmgPluginManager.IntegrateDeployedPlugins(new List<string>(m_hstDeployedPluginPaths));
			m_hstDeployedPluginPaths.Clear();
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
