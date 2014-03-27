using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModRepositories;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using System.Diagnostics;
using System.Linq;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	public partial class ModManager
	{
		/// <summary>
		/// A list of mods that are to be added to the mod manager.
		/// </summary>
		protected class AddModQueue : IDisposable
		{
			private ModManager m_mmgModManager = null;
			private IEnvironmentInfo m_eifEnvironmentInfo = null;
			private Dictionary<Uri, AddModTask> m_dicActiveTasks = new Dictionary<Uri, AddModTask>();

			/// <summary>
			/// The number of running local addmod tasks.
			/// </summary>
			/// <remarks>
			/// The number of running local addmod tasks.
			/// </remarks>
			private int LocalTaskCount
			{
				get
				{
					lock (m_dicActiveTasks)
					{
						return m_dicActiveTasks.Values.Where(x => !x.IsRemote).Count();
					}
				}
			} 

			#region Constructors

			/// <summary>
			/// A sipmle constructor that initializes that object with the required dependencies.
			/// </summary>
			/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
			/// <param name="p_mmgModManager">The mod manager for which we are queing mods to be added.</param>
			public AddModQueue(IEnvironmentInfo p_eifEnvironmentInfo, ModManager p_mmgModManager)
			{
				m_eifEnvironmentInfo = p_eifEnvironmentInfo;
				m_mmgModManager = p_mmgModManager;
			}

			#endregion

			/// <summary>
			/// Loads the list of mods that are queued to be added to the mod manager.
			/// </summary>
			public void LoadQueuedMods()
			{
				Trace.TraceInformation("Loading mods that are queued to be added.");
				if (!m_eifEnvironmentInfo.Settings.QueuedModsToAdd.ContainsKey(m_mmgModManager.GameMode.ModeId))
					return;
				if (m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_mmgModManager.GameMode.ModeId] == null)
				{
					m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_mmgModManager.GameMode.ModeId] = new KeyedSettings<AddModDescriptor>();
					m_eifEnvironmentInfo.Settings.Save();
					return;
				}
				foreach (KeyValuePair<string, AddModDescriptor> kvpMod in new List<KeyValuePair<string, AddModDescriptor>>(m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_mmgModManager.GameMode.ModeId]))
				{
					Trace.TraceInformation(String.Format("[{0}] Adding from serialized queue", kvpMod.Key.ToString()));
					kvpMod.Value.Status = TaskStatus.Paused;
					AddMod(new Uri(kvpMod.Key), ConfirmFileOverwrite);
				}
			}

			/// <summary>
			/// Adds the specified mod to the queue.
			/// </summary>
			/// <remarks>
			/// The specified mod is downloaded, and then added to the mod manager.
			/// </remarks>
			/// <param name="p_uriPath">The URL of the mod to add to the manager.</param>
			/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
			public IBackgroundTask AddMod(Uri p_uriPath, ConfirmOverwriteCallback p_cocConfirmOverwrite)
			{
				AddModTask amtModAdder = null;
				
				if (p_uriPath.Scheme.ToLowerInvariant().ToString() == "nxm")
				{
					if (m_dicActiveTasks.ContainsKey(p_uriPath))
						return m_dicActiveTasks[p_uriPath];
					Trace.TraceInformation(String.Format("[{0}] Adding Mod to AddModQueue", p_uriPath.ToString()));
					amtModAdder = new AddModTask(m_mmgModManager.GameMode, m_mmgModManager.ReadMeManager, m_mmgModManager.EnvironmentInfo, m_mmgModManager.ManagedModRegistry, m_mmgModManager.FormatRegistry, m_mmgModManager.ModRepository, p_uriPath, p_cocConfirmOverwrite);
					amtModAdder.TaskEnded += new EventHandler<TaskEndedEventArgs>(ModAdder_TaskEnded);
					amtModAdder.IsRemote = true;
					m_dicActiveTasks[p_uriPath] = amtModAdder;
					m_mmgModManager.DownloadMonitor.AddActivity(amtModAdder);
					amtModAdder.AddMod(false);
				}
				else
				{
					if (m_dicActiveTasks.ContainsKey(p_uriPath))
						return m_dicActiveTasks[p_uriPath];
					Trace.TraceInformation(String.Format("[{0}] Adding Mod to AddModQueue", p_uriPath.ToString()));
					amtModAdder = new AddModTask(m_mmgModManager.GameMode, m_mmgModManager.ReadMeManager, m_mmgModManager.EnvironmentInfo, m_mmgModManager.ManagedModRegistry, m_mmgModManager.FormatRegistry, m_mmgModManager.ModRepository, p_uriPath, p_cocConfirmOverwrite);
					amtModAdder.TaskEnded += new EventHandler<TaskEndedEventArgs>(ModAdder_TaskEnded);
					amtModAdder.IsRemote = false;
					m_dicActiveTasks[p_uriPath] = amtModAdder;
					m_mmgModManager.DownloadMonitor.AddActivity(amtModAdder);

					if (LocalTaskCount > 1)
						amtModAdder.AddMod(true);
					else
						amtModAdder.AddMod(false);
				}
				return amtModAdder;
			}

			/// <summary>
			/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of the mod adding task.
			/// </summary>
			/// <remarks>
			/// This retrieves the paths of the added mods.
			/// </remarks>
			/// <param name="sender">The object that raised the event.</param>
			/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
			private void ModAdder_TaskEnded(object sender, TaskEndedEventArgs e)
			{
				if ((e.Status != TaskStatus.Incomplete) && (e.Status != TaskStatus.Paused) && (e.Status != TaskStatus.Queued))
				{
					lock (m_dicActiveTasks)
					{
						Uri uriKey = (from k in m_dicActiveTasks
									  where (k.Value == sender)
									  select k.Key).FirstOrDefault();
						if (uriKey != null)
							m_dicActiveTasks.Remove(uriKey);
						if(m_dicActiveTasks.Count > 0)
							ResumeQueued();
					}
				}
			}

			/// <summary>
			/// The callback that confirms a file overwrite.
			/// </summary>
			/// <param name="p_strOldFilePath">The path to the file that is to be overwritten.</param>
			/// <param name="p_strNewFilePath">An out parameter specifying the file to to which to
			/// write the file.</param>
			/// <returns><c>true</c> if the file should be written;
			/// <c>false</c> otherwise.</returns>
			private bool ConfirmFileOverwrite(string p_strOldFilePath, out string p_strNewFilePath)
			{
				string strNewFileName = p_strOldFilePath;
				string strExtension = Path.GetExtension(p_strOldFilePath);
				string strDirectory = Path.GetDirectoryName(p_strOldFilePath);
				for (Int32 i = 2; i < Int32.MaxValue && File.Exists(strNewFileName); i++)
					strNewFileName = Path.Combine(strDirectory, String.Format("{0} ({1}){2}", Path.GetFileNameWithoutExtension(p_strOldFilePath), i, strExtension));
				if (File.Exists(strNewFileName))
					throw new Exception("Cannot write file. Unable to find unused file name.");
				p_strNewFilePath = strNewFileName;
				return true;
			}

			/// <summary>
			/// Resumes the queued running tasks.
			/// </summary>
			/// <remarks>
			/// Resumes the queued running tasks.
			/// </remarks>
			private void ResumeQueued()
			{
				AddModTask amtTask = m_dicActiveTasks.Values.Where(x => (x.Status == TaskStatus.Queued) && !x.IsRemote).FirstOrDefault();
				if (amtTask != null)
					amtTask.Resume();
			}

			#region IDisposable Members

			/// <summary>
			/// Terminates all running tasks.
			/// </summary>
			/// <remarks>
			/// After being disposed, further interaction with the object is undefined.
			/// </remarks>
			public void Dispose()
			{
				foreach (AddModTask amtTask in m_dicActiveTasks.Values.ToArray())
					amtTask.Dispose();
			}

			#endregion
		}
	}
}
