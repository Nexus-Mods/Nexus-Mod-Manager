using System;
using System.IO;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// Executes XML scripted-installer operations using the deployment semantics of the legacy XML installer.
	/// </summary>
	internal sealed class XmlScriptedInstallOperationExecutor : IScriptedInstallOperationExecutor, IScriptedInstallOperationBatchExecutor
	{
		private readonly IMod m_modMod;
		private readonly IGameMode m_gmdGameMode;
		private readonly InstallerGroup m_igpInstallers;
		private readonly IVirtualModActivator m_ivaVirtualModActivator;
		private readonly IModLinkInstaller m_mliModLinkInstaller;
		private readonly IScriptedFileSelectionCache m_sfcFileSelectionCache;

		/// <summary>
		/// Initializes an executor for the current XML scripted installation context.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_igpInstallers">The installer group used to apply installation changes.</param>
		/// <param name="p_ivaVirtualModActivator">The virtual mod activator used to resolve staging locations.</param>
		/// <param name="p_mliModLinkInstaller">The mod link installer used to deploy staged files.</param>
		/// <param name="p_sfcFileSelectionCache">The cache used to persist XML scripted file selections.</param>
		public XmlScriptedInstallOperationExecutor(IMod p_modMod, IGameMode p_gmdGameMode, InstallerGroup p_igpInstallers, IVirtualModActivator p_ivaVirtualModActivator, IModLinkInstaller p_mliModLinkInstaller, IScriptedFileSelectionCache p_sfcFileSelectionCache)
		{
			m_modMod = p_modMod;
			m_gmdGameMode = p_gmdGameMode;
			m_igpInstallers = p_igpInstallers;
			m_ivaVirtualModActivator = p_ivaVirtualModActivator;
			m_mliModLinkInstaller = p_mliModLinkInstaller;
			m_sfcFileSelectionCache = p_sfcFileSelectionCache;
		}

		/// <summary>
		/// Begins a deployment batch for the pending XML scripted file operations.
		/// </summary>
		/// <param name="p_intExpectedFileOperations">The number of pending operations expected to create or update virtual links.</param>
		/// <returns>A scope that flushes deferred deployment maintenance when disposed.</returns>
		public IDisposable BeginExecutionBatch(int p_intExpectedFileOperations)
		{
			try
			{
				return m_igpInstallers.InstallContext.Method == ModInstallMethod.Virtual
					? new LegacyXmlDeploymentBatch(new VirtualModDeploymentBatch(m_ivaVirtualModActivator, p_intExpectedFileOperations))
					: null;
			}
			catch
			{
				// XML installers historically absorb deployment-maintenance failures at the file-operation boundary.
				return null;
			}
		}

		/// <summary>
		/// Executes an operation produced by the XML scripted installer.
		/// </summary>
		/// <param name="p_sioOperation">The logical installation operation to execute.</param>
		/// <returns><c>true</c> when the operation preserves the legacy XML installer success semantics; otherwise, <c>false</c>.</returns>
		public bool Execute(ScriptedInstallOperation p_sioOperation)
		{
			if (p_sioOperation == null)
				throw new ArgumentNullException(nameof(p_sioOperation));

			InstallModFileOperation imoInstallFile = p_sioOperation as InstallModFileOperation;
			if (imoInstallFile != null)
				return ExecuteInstallModFile(imoInstallFile);

			SetPluginActivationOperation saoSetActivation = p_sioOperation as SetPluginActivationOperation;
			if (saoSetActivation != null)
				return ExecuteSetPluginActivation(saoSetActivation);

			throw new NotSupportedException(String.Format("Unsupported XML scripted installation operation type: {0}", p_sioOperation.GetType().FullName));
		}

		/// <summary>
		/// Installs and links a file using the staging and error-handling behavior of the legacy XML installer.
		/// </summary>
		/// <param name="p_imoOperation">The archive-file installation operation to execute.</param>
		/// <returns><c>true</c> after the legacy XML file-install attempt has been processed.</returns>
		private bool ExecuteInstallModFile(InstallModFileOperation p_imoOperation)
		{
			if (ModInstallFileFilter.IsIgnored(p_imoOperation.SourcePath) || ModInstallFileFilter.IsIgnored(p_imoOperation.DestinationPath))
				return true;

			string strInstallDestination = String.IsNullOrEmpty(p_imoOperation.DestinationPath) || p_imoOperation.DestinationPath.Equals(".")
				? Path.GetFileName(p_imoOperation.SourcePath)
				: p_imoOperation.DestinationPath;

			try
			{
				if (m_igpInstallers.InstallContext.Method == ModInstallMethod.Direct)
				{
					ScriptedFileDeploymentDecision decision = p_imoOperation.DeploymentDecision;
					if (decision == null || decision.WritePayload)
					{
						if (decision != null)
							((IModFileInstallDecisionSupport)m_igpInstallers.FileInstaller).InstallFileFromModWithResolvedOverwrite(p_imoOperation.SourcePath, strInstallDestination, false);
						else
							m_igpInstallers.FileInstaller.InstallFileFromMod(p_imoOperation.SourcePath, strInstallDestination);
					}
				}
				else
				{
					string strVirtualPath = ScriptedInstallStagingPathResolver.GetStagingPath(m_modMod, m_gmdGameMode, m_ivaVirtualModActivator, strInstallDestination, true);
					m_igpInstallers.FileInstaller.InstallFileFromMod(p_imoOperation.SourcePath, strVirtualPath);

					ModDeploymentTarget target = GetPromotedTarget(strInstallDestination);
					if (target != null)
					{
						if (m_igpInstallers.TransactionalFileManager == null || m_igpInstallers.DeploymentOverwriteResolver == null)
							throw new InvalidOperationException("Promoted XML deployment requires transactional deployment services.");
						bool activate = m_igpInstallers.DeploymentOverwriteResolver.ShouldActivate(target);
						m_igpInstallers.DeploymentManager.InstallVirtualFile(
							m_modMod, target, strInstallDestination, strVirtualPath, m_igpInstallers.InstallContext.InstallRoot,
							activate, m_igpInstallers.TransactionalFileManager);
						m_igpInstallers.MarkPromotedDeploymentUsed();
					}
					else
						m_mliModLinkInstaller.AddFileLink(m_modMod, strInstallDestination, strVirtualPath, true, false, m_igpInstallers.InstallContext.InstallRoot);
				}
			}
			catch
			{
				// The legacy XML installer intentionally ignored file-install and deployment failures at this level.
			}

			// The legacy XML path persisted the selected mapping even when its internal file-install helper returned false.
			m_sfcFileSelectionCache.RecordSelection(p_imoOperation.SourcePath, p_imoOperation.DestinationPath);
			return true;
		}

		/// <summary>
		/// Resolves a destination only when the XML operation targets an already promoted file.
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
		/// Applies a plugin activation change using the XML installer's legacy path normalization rules.
		/// </summary>
		/// <param name="p_saoOperation">The plugin activation operation to execute.</param>
		/// <returns><c>true</c> after the activation request has been applied.</returns>
		private bool ExecuteSetPluginActivation(SetPluginActivationOperation p_saoOperation)
		{
			if (m_igpInstallers.PluginManager == null)
				return true;

			string strPluginPath = m_gmdGameMode.GetModFormatAdjustedPath(m_modMod.Format, p_saoOperation.PluginPath, false);
			if (m_igpInstallers.PluginManager.IsActivatiblePluginFile(strPluginPath))
				m_igpInstallers.PluginManager.SetPluginActivation(strPluginPath, p_saoOperation.Activate);
			return true;
		}

		/// <summary>
		/// Preserves the XML installer's legacy error-tolerance when deferred deployment maintenance is flushed.
		/// </summary>
		private sealed class LegacyXmlDeploymentBatch : IDisposable
		{
			private IDisposable m_dspDeploymentBatch;

			/// <summary>
			/// Initializes a compatibility wrapper around a deployment batch.
			/// </summary>
			/// <param name="p_dspDeploymentBatch">The deployment batch whose completion errors should follow legacy XML semantics.</param>
			public LegacyXmlDeploymentBatch(IDisposable p_dspDeploymentBatch)
			{
				m_dspDeploymentBatch = p_dspDeploymentBatch;
			}

			/// <summary>
			/// Completes the wrapped batch while preserving the XML installer's historical error-tolerance.
			/// </summary>
			public void Dispose()
			{
				IDisposable dspDeploymentBatch = m_dspDeploymentBatch;
				m_dspDeploymentBatch = null;
				try
				{
					if (dspDeploymentBatch != null)
						dspDeploymentBatch.Dispose();
				}
				catch
				{
					// File deployment failures in the legacy XML path are intentionally non-fatal at this level.
				}
			}
		}

	}
}
