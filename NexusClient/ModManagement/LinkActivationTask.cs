namespace Nexus.Client.ModManagement
{
    using System;
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

		protected ConfirmActionMethod ConfirmActionMethod { get; }

		protected bool MultiHDMode => VirtualModActivator.MultiHDMode;

        protected IMod Mod { get; }

		public bool Disabling { get; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="pluginManager">The current <see cref="IPluginManager"/>.</param>
		/// <param name="virtualModActivator">The <see cref="IVirtualModActivator"/></param>
		/// <param name="mod">The mod.</param>
		/// <param name="disable">Whether or not we're disabling the given mod.</param>
		/// <param name="confirmActionMethod">The delegate to call to confirm an action.</param>
		public LinkActivationTask(IPluginManager pluginManager, IVirtualModActivator virtualModActivator, IMod mod, bool disable, ConfirmActionMethod confirmActionMethod)
		{
			PluginManager = pluginManager;
			VirtualModActivator = virtualModActivator;
			Mod = mod;
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
				string modFilenamePath = Path.Combine(VirtualModActivator.VirtualPath, Path.GetFileNameWithoutExtension(Mod.Filename).Trim());
				string linkFilenamePath = MultiHDMode ? Path.Combine(VirtualModActivator.HDLinkFolder, Path.GetFileNameWithoutExtension(Mod.Filename).Trim()) : string.Empty;
				string modDownloadIdPath = string.IsNullOrWhiteSpace(Mod.DownloadId) || Mod.DownloadId.Length <= 1 || Mod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase) ? string.Empty : Path.Combine(VirtualModActivator.VirtualPath, Mod.DownloadId);
				string linkDownloadIdPath = MultiHDMode ? string.IsNullOrWhiteSpace(Mod.DownloadId) || Mod.DownloadId.Length <= 1 || Mod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase) ? string.Empty : Path.Combine(VirtualModActivator.HDLinkFolder, Mod.DownloadId) : string.Empty;
				string modFolderPath = modFilenamePath;
				string linkFolderPath = linkFilenamePath;

				if (!string.IsNullOrWhiteSpace(modDownloadIdPath) && Directory.Exists(modDownloadIdPath))
                {
                    modFolderPath = modDownloadIdPath;
                }

                if (MultiHDMode && !string.IsNullOrWhiteSpace(linkDownloadIdPath) && Directory.Exists(linkDownloadIdPath))
                {
                    linkFolderPath = linkDownloadIdPath;
                }

                if (Directory.Exists(modFolderPath) || MultiHDMode && Directory.Exists(linkFolderPath))
				{
					string[] files;

                    try
                    {
                        if (MultiHDMode && Directory.Exists(linkFolderPath))
                        {
                            files = Directory.Exists(modFolderPath) ?
                                Directory.GetFiles(linkFolderPath, "*", SearchOption.AllDirectories).Concat(Directory.GetFiles(modFolderPath, "*", SearchOption.AllDirectories)).ToArray() :
                                Directory.GetFiles(linkFolderPath, "*", SearchOption.AllDirectories);
                        }
                        else
                        {
                            files = Directory.GetFiles(modFolderPath, "*", SearchOption.AllDirectories);
                        }
					}
                    catch (Exception ex)
                    {
						TraceUtil.TraceException(ex);
                        OverallMessage = $"{nameof(LinkActivationTask)} failed: {ex.Message}";
                        Status = TaskStatus.Error;

                        return null;
					}

                    if (files.Length <= 1000)
					{
						ItemProgressMaximum = files.Length;
						ItemProgressStepSize = 1;
					}
					else
					{
						ItemProgressMaximum = 1000;
						lotsOfLinks = true;
						dblRatio = 1000 / files.Length;
					}

					if (files.Length > 0)
					{
						IModLinkInstaller modLinkInstaller = VirtualModActivator.GetModLinkInstaller();

						foreach (string file in files)
						{
							ItemMessage = $"{(Disabling ? "Disabling" : "Activating")}: {file}";

                            string strFile = MultiHDMode && file.Contains(linkFolderPath)
                                ? file.Replace(linkFolderPath + Path.DirectorySeparatorChar, string.Empty)
                                : file.Replace(modFolderPath + Path.DirectorySeparatorChar, string.Empty);

							string fileLink = string.Empty;

                            try
                            {
                                fileLink = modLinkInstaller.AddFileLink(Mod, strFile, null, false);
							}
                            catch (Exception ex)
                            {
                                TraceUtil.TraceException(ex);
                                OverallMessage = $"{nameof(LinkActivationTask)} failed: {ex.Message}";
                                Status = TaskStatus.Error;

                                return null;
                            }

                            if (!string.IsNullOrEmpty(fileLink) && PluginManager != null)
                            {
                                if (PluginManager.IsActivatiblePluginFile(fileLink))
                                {
                                    PluginManager.AddPlugin(fileLink);
                                    PluginManager.ActivatePlugin(fileLink);
                                }
                            }

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

						VirtualModActivator.FinalizeModActivation(Mod);
					}
                }
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
