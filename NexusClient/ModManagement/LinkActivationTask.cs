namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using BackgroundTasks;

    using Nexus.Client.Mods;
    using Nexus.Client.PluginManagement;
    using Nexus.Client.UI;
    using Nexus.Client.Util;

    public class LinkActivationTask : ThreadedBackgroundTask
	{
		#region Properties

		/// <summary>
		/// Gets the current PluginManager.
		/// </summary>
		/// <value>The current PluginManager.</value>
		protected IPluginManager PluginManager { get; }

		/// <summary>
		/// Gets the current VirtualModActivator.
		/// </summary>
		/// <value>The current VirtualModActivator.</value>
		protected IVirtualModActivator VirtualModActivator { get; }

        protected IVirtualDeploymentService VirtualDeploymentService { get; }

		protected ConfirmActionMethod ConfirmActionMethod { get; }

		protected bool MultiHDMode => VirtualModActivator.MultiHDMode;

        protected IMod Mod { get; }

        protected ModInstallRoot InstallRoot { get; }

		public bool Disabling { get; }

		#endregion

		private readonly HashSet<string> m_hstDeployedPluginPaths =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private void UpdateActivationProgress(VirtualDeploymentProgress progress)
		{
			if (progress.IsStarting)
			{
				if (progress.FileCount <= 1000)
				{
					ItemProgressMaximum = progress.FileCount;
					ItemProgressStepSize = 1;
				}
				else
				{
					ItemProgressMaximum = 1000;
				}
				return;
			}

			ItemMessage = $"Activating: {progress.CurrentFilePath}";
			if (ItemProgress >= ItemProgressMaximum)
				return;

			if (progress.FileCount > 1000)
			{
				double ratio = 1000 / progress.FileCount;
				ItemProgress = (int)Math.Floor(progress.ProcessedFileCount * ratio);
			}
			else
			{
				StepItemProgress();
			}
		}
		private bool HandleLinkedFile(string filePath)
		{
			if (PluginManager == null || !PluginManager.IsActivatiblePluginFile(filePath))
				return false;

			m_hstDeployedPluginPaths.Add(filePath);
			return true;
		}

		/// <summary>
		/// Completes activation after deployment and plugin collection have finished.
		/// </summary>
		private void CompleteActivationAfterDeployment(VirtualDeploymentResult deploymentResult)
		{
			if (PluginManager != null && m_hstDeployedPluginPaths.Count > 0)
			{
				ItemMessage = "Updating plugin state...";
				PluginManager.IntegrateDeployedPlugins(m_hstDeployedPluginPaths.ToList());
			}

			if (deploymentResult.FileCount > 0)
				VirtualModActivator.FinalizeModActivation(Mod);
		}
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="pluginManager">The current <see cref="IPluginManager"/>.</param>
		/// <param name="virtualModActivator">The <see cref="IVirtualModActivator"/></param>
		/// <param name="virtualDeploymentService">The virtual deployment service.</param>
		/// <param name="mod">The mod.</param>
		/// <param name="disable">Whether or not we're disabling the given mod.</param>
		/// <param name="confirmActionMethod">The delegate to call to confirm an action.</param>
		public LinkActivationTask(IPluginManager pluginManager, IVirtualModActivator virtualModActivator, IVirtualDeploymentService virtualDeploymentService, IMod mod, bool disable, ConfirmActionMethod confirmActionMethod, ModInstallRoot installRoot)
		{
			PluginManager = pluginManager;
			VirtualModActivator = virtualModActivator;
            VirtualDeploymentService = virtualDeploymentService;
			Mod = mod;
            InstallRoot = installRoot;
			Disabling = disable;
			ConfirmActionMethod = confirmActionMethod;
		}

		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="confirmActionMethod">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod confirmActionMethod)
		{
			Start(confirmActionMethod);
		}

		/// <summary>
		/// Resumes the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task is not paused.</exception>
		public override void Resume()
		{
			Update(ConfirmActionMethod);
		}

		/// <summary>
		/// The method that is called to start the background task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
            var lotsOfLinks = false;
			var intProgress = 0;
			double dblRatio = 0;

			OverallMessage = $"{(Disabling ? "Disabling" : "Activating")} Mod Links: {Mod.ModName}";
			ItemMessage = $"{(Disabling ? "Disabling" : "Activating")}...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = 1;
			ShowItemProgress = true;
			ItemProgress = 0;

			if (!Disabling)
			{
				m_hstDeployedPluginPaths.Clear();

				VirtualDeploymentOptions deploymentOptions = new VirtualDeploymentOptions
				{
					LinkedFileHandler = HandleLinkedFile,
					Progress = UpdateActivationProgress,
                    InstallRoot = InstallRoot,
				};
				VirtualDeploymentResult deploymentResult = VirtualDeploymentService.ActivateModLinks(Mod, deploymentOptions);

				if (deploymentResult.Failure != null)
				{
					TraceUtil.TraceException(deploymentResult.Failure);
					OverallMessage = $"{nameof(LinkActivationTask)} failed: {deploymentResult.Failure.Message}";
					Status = TaskStatus.Error;
					return null;
				}

				CompleteActivationAfterDeployment(deploymentResult);
			}
			else
			{
				if (Mod != null)
				{
					var ivlLinks = VirtualModActivator.VirtualLinks
                        .Where(x => x.ModInfo != null && string.Equals(x.ModInfo.ModFileName, Path.GetFileName(Mod.Filename), StringComparison.OrdinalIgnoreCase))
                        .ToList();
					
                    if (ivlLinks.Count > 0)
					{
						if (ivlLinks.Count <= 1000)
						{
							ItemProgressMaximum = ivlLinks.Count;
							ItemProgressStepSize = 1;
						}
						else
						{
							ItemProgressMaximum = 1000;
							lotsOfLinks = true;
							dblRatio = 1000 / ivlLinks.Count;
						}

						foreach (var link in ivlLinks)
						{
							ItemMessage = $"{(Disabling ? "Disabling" : "Activating")}: {link.VirtualModPath}";
							VirtualModActivator.RemoveFileLink(link, Mod);

							if (ItemProgress < ItemProgressMaximum)
							{
								if (lotsOfLinks)
                                {
                                    ItemProgress = (int)Math.Floor(++intProgress * dblRatio);
                                }
                                else
                                {
                                    StepItemProgress();
                                }
                            }
						}
					}

					VirtualModActivator.FinalizeModDeactivation(Mod);
				}
			}


			if (OverallProgress < OverallProgressMaximum)
            {
                StepOverallProgress();
            }

            return null;
		}
	}
}
