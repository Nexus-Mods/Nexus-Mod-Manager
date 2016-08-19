using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	public class LinkActivationTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Properties

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected VirtualModActivator VirtualModActivator { get; private set; }

		protected ConfirmActionMethod ConfirmActionMethod { get; private set; }

		protected bool MultiHDMode
		{
			get
			{
				return VirtualModActivator.MultiHDMode;
			}
		}

		protected IMod Mod { get; private set; }

		protected bool Disabling { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		/// <param name="p_lstMods">The mod list.</param>
		/// <param name="p_intNewValue">The new category id.</param>
		public LinkActivationTask(IPluginManager p_pmgPluginManager, VirtualModActivator p_vmaVirtualModActivator, IMod p_modMod, bool p_booDisable, ConfirmActionMethod p_camConfirm)
		{
			PluginManager = p_pmgPluginManager;
			VirtualModActivator = p_vmaVirtualModActivator;
			Mod = p_modMod;
			Disabling = p_booDisable;
			ConfirmActionMethod = p_camConfirm;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			base.OnTaskEnded(e);
		}
		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod p_camConfirm)
		{
			Start(p_camConfirm);
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
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			base.Cancel();
			m_booCancel = true;
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];
			bool booLotsOfLinks = false;
			int intProgress = 0;
			double dblRatio = 0;

			OverallMessage = String.Format("{0} Mod Links: {1}", Disabling ? "Disabling" : "Activating", Mod.ModName);
			ItemMessage = String.Format("{0}...", Disabling ? "Disabling" : "Activating");
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = 1;
			ShowItemProgress = true;
			ItemProgress = 0;

			if (!Disabling)
			{
				string strModFilenamePath = Path.Combine(VirtualModActivator.VirtualPath, Path.GetFileNameWithoutExtension(Mod.Filename));
				string strLinkFilenamePath = MultiHDMode ? Path.Combine(VirtualModActivator.HDLinkFolder, Path.GetFileNameWithoutExtension(Mod.Filename)) : string.Empty;
				string strModDownloadIDPath = (string.IsNullOrWhiteSpace(Mod.DownloadId) || (Mod.DownloadId.Length <= 1) || Mod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase)) ? string.Empty : Path.Combine(VirtualModActivator.VirtualPath, Mod.DownloadId);
				string strLinkDownloadIDPath = MultiHDMode ? ((string.IsNullOrWhiteSpace(Mod.DownloadId) || (Mod.DownloadId.Length <= 1) || Mod.DownloadId.Equals("-1", StringComparison.OrdinalIgnoreCase)) ? string.Empty : Path.Combine(VirtualModActivator.HDLinkFolder, Mod.DownloadId)) : string.Empty;
				string strModFolderPath = strModFilenamePath;
				string strLinkFolderPath = strLinkFilenamePath;

				if (!string.IsNullOrWhiteSpace(strModDownloadIDPath) && Directory.Exists(strModDownloadIDPath))
					strModFolderPath = strModDownloadIDPath;

				if (MultiHDMode && (!string.IsNullOrWhiteSpace(strLinkDownloadIDPath) && Directory.Exists(strLinkDownloadIDPath)))
					strLinkFolderPath = strLinkDownloadIDPath;

				if (Directory.Exists(strModFolderPath) || (MultiHDMode && Directory.Exists(strLinkFolderPath)))
				{
					string[] strFiles = null;
					
					if (MultiHDMode && Directory.Exists(strLinkFolderPath))
					{
						if (Directory.Exists(strModFolderPath))
							strFiles = Directory.GetFiles(strLinkFolderPath, "*", SearchOption.AllDirectories).Concat(Directory.GetFiles(strModFolderPath, "*", SearchOption.AllDirectories)).ToArray();
						else
							strFiles = Directory.GetFiles(strLinkFolderPath, "*", SearchOption.AllDirectories);
					}
					else
						strFiles = Directory.GetFiles(strModFolderPath, "*", SearchOption.AllDirectories);

					if (strFiles.Length <= 1000)
					{
						ItemProgressMaximum = strFiles.Length;
						ItemProgressStepSize = 1;
					}
					else
					{
						ItemProgressMaximum = 1000;
						booLotsOfLinks = true;
						dblRatio = 1000 / strFiles.Length;
					}

					if (strFiles.Length > 0)
					{
						IModLinkInstaller ModLinkInstaller = VirtualModActivator.GetModLinkInstaller();

						foreach (string File in strFiles)
						{
							//if (m_booCancel)
							//	break;
							ItemMessage = string.Format("{0}: {1}", Disabling ? "Disabling" : "Activating", File);

							string strFile = string.Empty;

							if (MultiHDMode && File.Contains(strLinkFolderPath))
								strFile = File.Replace((strLinkFolderPath + Path.DirectorySeparatorChar), string.Empty);
							else
								strFile = File.Replace((strModFolderPath + Path.DirectorySeparatorChar), string.Empty);

							string strFileLink = ModLinkInstaller.AddFileLink(Mod, strFile, null, false);

							if (!string.IsNullOrEmpty(strFileLink))
								if (PluginManager != null)
									if (PluginManager.IsActivatiblePluginFile(strFileLink))
									{
										PluginManager.AddPlugin(strFileLink);
										PluginManager.ActivatePlugin(strFileLink);
									}

							if (ItemProgress < ItemProgressMaximum)
							{
								if (booLotsOfLinks)
									ItemProgress = (int)Math.Floor(++intProgress * dblRatio);
								else
									StepItemProgress();
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
					List<IVirtualModLink> ivlLinks = VirtualModActivator.VirtualLinks.Where(x => (x.ModInfo != null) && (x.ModInfo.ModFileName.ToLowerInvariant() == Path.GetFileName(Mod.Filename).ToLowerInvariant())).ToList();
					if ((ivlLinks != null) && (ivlLinks.Count > 0))
					{
						if (ivlLinks.Count <= 1000)
						{
							ItemProgressMaximum = ivlLinks.Count;
							ItemProgressStepSize = 1;
						}
						else
						{
							ItemProgressMaximum = 1000;
							booLotsOfLinks = true;
							dblRatio = 1000 / ivlLinks.Count;
						}

						foreach (IVirtualModLink Link in ivlLinks)
						{
							ItemMessage = string.Format("{0}: {1}", Disabling ? "Disabling" : "Activating", Link.VirtualModPath);
							VirtualModActivator.RemoveFileLink(Link, Mod);

							if (ItemProgress < ItemProgressMaximum)
							{
								if (booLotsOfLinks)
									ItemProgress = (int)Math.Floor(++intProgress * dblRatio);
								else
									StepItemProgress();
							}
						}
					}

					VirtualModActivator.FinalizeModDeactivation(Mod);
				}
			}


			if (OverallProgress < OverallProgressMaximum)
				StepOverallProgress();

			return null;
		}
	}
}
