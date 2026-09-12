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
		private readonly IInstallLog m_ilgInstallLog;
		private readonly IModDeploymentManager m_mdmDeploymentManager;
		private readonly IPluginManager m_pmgPluginManager;
		private readonly TxFileManager m_tfmFileManager;
		private readonly ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmationDelegate;
		private readonly IEnvironmentInfo m_eifEnvironmentInfo;
		private readonly ModInstallContext m_micInstallContext;
		private readonly HashSet<string> m_hstDeployedPluginPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly List<string> m_lstOverwriteFolders = new List<string>();
		private readonly List<string> m_lstDontOverwriteFolders = new List<string>();
		private readonly HashSet<string> m_hstOverwriteOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> m_hstDontOverwriteOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private bool m_booOverwriteAll;
		private bool m_booDontOverwriteAll;

		/// <summary>
		/// Initializes a Direct file installer for one immutable install context.
		/// </summary>
		public DirectModFileInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IInstallLog p_ilgInstallLog,
			IModDeploymentManager p_mdmDeploymentManager, IPluginManager p_pmgPluginManager,
			TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate,
			IEnvironmentInfo p_eifEnvironmentInfo, ModInstallContext p_micInstallContext)
		{
			m_modMod = p_modMod ?? throw new ArgumentNullException(nameof(p_modMod));
			m_gmdGameMode = p_gmdGameMode ?? throw new ArgumentNullException(nameof(p_gmdGameMode));
			m_ilgInstallLog = p_ilgInstallLog ?? throw new ArgumentNullException(nameof(p_ilgInstallLog));
			m_mdmDeploymentManager = p_mdmDeploymentManager ?? throw new ArgumentNullException(nameof(p_mdmDeploymentManager));
			m_pmgPluginManager = p_pmgPluginManager;
			m_tfmFileManager = p_tfmFileManager ?? throw new ArgumentNullException(nameof(p_tfmFileManager));
			m_dlgOverwriteConfirmationDelegate = p_dlgOverwriteConfirmationDelegate ?? ((message, allowGroup, hasOwner) => OverwriteResult.No);
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_micInstallContext = p_micInstallContext ?? throw new ArgumentNullException(nameof(p_micInstallContext));
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
			if (!ResolveOverwrite(target))
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

		private bool ResolveOverwrite(ModDeploymentTarget p_mdtTarget)
		{
			string deploymentPath = m_mdmDeploymentManager.GetDeploymentPath(p_mdtTarget);
			if (!File.Exists(deploymentPath))
				return true;

			string currentOwnerKey = m_mdmDeploymentManager.GetCurrentOwnerKey(p_mdtTarget);
			string installingModKey = m_ilgInstallLog.GetModKey(m_modMod);
			if (!string.IsNullOrEmpty(currentOwnerKey) &&
				currentOwnerKey.Equals(installingModKey, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			string directory = (Path.GetDirectoryName(deploymentPath) ?? string.Empty).ToLowerInvariant();
			if (IsFolderCovered(m_lstOverwriteFolders, directory))
				return true;
			if (IsFolderCovered(m_lstDontOverwriteFolders, directory))
				return false;
			if (m_booOverwriteAll)
				return true;
			if (m_booDontOverwriteAll)
				return false;
			if (!string.IsNullOrEmpty(currentOwnerKey) && m_hstOverwriteOwners.Contains(currentOwnerKey))
				return true;
			if (!string.IsNullOrEmpty(currentOwnerKey) && m_hstDontOverwriteOwners.Contains(currentOwnerKey))
				return false;

			bool readOnly = new FileInfo(deploymentPath).IsReadOnly;
			bool hasManagedOwner = !string.IsNullOrEmpty(currentOwnerKey) &&
				!currentOwnerKey.Equals(m_ilgInstallLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase);
			string message = hasManagedOwner
				? "Game file '{0}' is already owned by another Direct mod"
				: "Game file '{0}' already exists";
			if (readOnly)
				message += " and is read-only.";
			else
				message += ".";
			message += Environment.NewLine + string.Format("Overwrite with {0} mod's file?", m_modMod.ModName);

			OverwriteResult result = m_dlgOverwriteConfirmationDelegate(
				string.Format(message, p_mdtTarget.RelativePath),
				true,
				hasManagedOwner);
			switch (result)
			{
				case OverwriteResult.Yes:
					return true;
				case OverwriteResult.No:
					return false;
				case OverwriteResult.YesToAll:
					m_booOverwriteAll = true;
					return true;
				case OverwriteResult.NoToAll:
					m_booDontOverwriteAll = true;
					return false;
				case OverwriteResult.YesToGroup:
					m_lstOverwriteFolders.Add(directory);
					return true;
				case OverwriteResult.NoToGroup:
					m_lstDontOverwriteFolders.Add(directory);
					return false;
				case OverwriteResult.YesToMod:
					if (!string.IsNullOrEmpty(currentOwnerKey))
						m_hstOverwriteOwners.Add(currentOwnerKey);
					return true;
				case OverwriteResult.NoToMod:
					if (!string.IsNullOrEmpty(currentOwnerKey))
						m_hstDontOverwriteOwners.Add(currentOwnerKey);
					return false;
				default:
					throw new InvalidOperationException("The overwrite dialog returned an unsupported result.");
			}
		}

		private static bool IsFolderCovered(IEnumerable<string> p_enmFolders, string p_strDirectory)
		{
			foreach (string folder in p_enmFolders)
			{
				if (p_strDirectory.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
					p_strDirectory.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
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
