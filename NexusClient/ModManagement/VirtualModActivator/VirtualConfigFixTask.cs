using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	public class VirtualConfigFixTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Properties

		protected List<string> ConfigFilePaths { get; set; }

		protected ModManager ModManager { get; set; }

		protected IVirtualModActivator VirtualModActivator { get; set; }

		protected IModProfile ModProfile { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public VirtualConfigFixTask(List<string> p_lstConfigFilePath, ModManager p_mmgModManager, IVirtualModActivator p_vmaVirtualModActivator, IModProfile p_mprProfile)
		{
			ConfigFilePaths = p_lstConfigFilePath;
			ModProfile = p_mprProfile;
			ModManager = p_mmgModManager;
			VirtualModActivator = p_vmaVirtualModActivator;
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
			string strOverallRoot = "Fixing config file";
			OverallMessage = "Parsing config file...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = false;
			int i = 0;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			foreach (string FilePath in ConfigFilePaths)
			{
				List<string> lstAddedModInfo = new List<string>();
				List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
				List<IVirtualModInfo> lstVirtualMods = new List<IVirtualModInfo>();

				if (File.Exists(FilePath))
				{
					XDocument docVirtual;
					using (var sr = new StreamReader(FilePath))
					{
						docVirtual = XDocument.Load(sr);
					}
					
					strOverallRoot = string.Format("Fixing config file {0}/{1}", ++i, ConfigFilePaths.Count());

					try
					{
						XElement xelModList = docVirtual.Descendants("modList").FirstOrDefault();
						if ((xelModList != null) && xelModList.HasElements)
						{
							OverallProgressMaximum = xelModList.Elements("modId").Count();

							foreach (XElement xelMod in xelModList.Elements("modInfo"))
							{
								string strModId = xelMod.Attribute("modId").Value;
								string strDownloadId = string.Empty;
								string strUpdatedDownloadId = string.Empty;
								string strNewFileName = string.Empty;
								string strFileVersion = string.Empty;

								if (OverallProgress < OverallProgressMaximum)
									StepOverallProgress();
								OverallMessage = string.Format("{0} - element: {1}/{2}", strOverallRoot, OverallProgress, OverallProgressMaximum);

								try
								{
									strDownloadId = xelMod.Attribute("downloadId").Value;
								}
								catch { }

								try
								{
									strUpdatedDownloadId = xelMod.Attribute("updatedDownloadId").Value;
								}
								catch { }

								string strModName = xelMod.Attribute("modName").Value;
								string strModFileName = xelMod.Attribute("modFileName").Value;

								if (lstAddedModInfo.Contains(strModFileName, StringComparer.InvariantCultureIgnoreCase))
									continue;

								try
								{
									strNewFileName = xelMod.Attribute("modNewFileName").Value;
								}
								catch { }

								string strModFilePath = xelMod.Attribute("modFilePath").Value;

								try
								{
									strFileVersion = xelMod.Attribute("FileVersion").Value;
								}
								catch
								{
									IMod mod = ModManager.GetModByFilename(strModFileName);
									strFileVersion = mod.HumanReadableVersion;
								}

								VirtualModInfo vmiMod = new VirtualModInfo(strModId, strDownloadId, strUpdatedDownloadId, strModName, strModFileName, strNewFileName, strModFilePath, strFileVersion);

								bool booNoFileLink = true;

								foreach (XElement xelLink in xelMod.Elements("fileLink"))
								{
									string strRealPath = xelLink.Attribute("realPath").Value;
									string strVirtualPath = xelLink.Attribute("virtualPath").Value;
									Int32 intPriority = 0;
									try
									{
										intPriority = Convert.ToInt32(xelLink.Element("linkPriority").Value);
									}
									catch { }
									bool booActive = false;
									try
									{
										booActive = Convert.ToBoolean(xelLink.Element("isActive").Value);
									}
									catch { }

									if (booNoFileLink)
									{
										booNoFileLink = false;
										lstVirtualMods.Add(vmiMod);
										lstAddedModInfo.Add(strModFileName);
									}

									lstVirtualLinks.Add(new VirtualModLink(strRealPath, strVirtualPath, intPriority, booActive, vmiMod));
								}
							}
						}
					}
					catch { }
				}

				if ((lstVirtualLinks.Count > 0) && (lstVirtualMods.Count > 0))
				{
					OverallMessage = "Saving fixed config file...";
					VirtualModActivator.SaveModList(FilePath, lstVirtualMods, lstVirtualLinks);
				}
			}

			return ModProfile;
		}
	}
}
