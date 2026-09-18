using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModRepositories;
using Nexus.Client.OnlineServices.Infrastructure;
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
			private Dictionary<Guid, AddModTask> m_dicActiveTasksByOperationId = new Dictionary<Guid, AddModTask>();

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
				try
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
						if (kvpMod.Value != null && kvpMod.Value.QueueOperationId.HasValue)
						{
							Trace.TraceInformation("Deferring externally correlated AddMod restart state for explicit reconciliation.");
							continue;
						}
						Trace.TraceInformation(String.Format("[{0}] Adding from serialized queue", SanitizeDownloadUri(new Uri(kvpMod.Key))));
						kvpMod.Value.Status = TaskStatus.Paused;
						AddMod(new Uri(kvpMod.Key), ConfirmFileOverwrite);
					}
				}
				catch {	}
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
				return AddMod(p_uriPath, p_cocConfirmOverwrite, null);
			}

			/// <summary>
			/// Adds the specified mod to the queue with an optional explicit category assignment.
			/// </summary>
			/// <param name="p_uriPath">The URL of the mod to add to the manager.</param>
			/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
			/// <param name="p_intCategoryOverrideId">The explicit category ID, or <c>null</c> to keep normal Nexus category resolution.</param>
			public IBackgroundTask AddMod(Uri p_uriPath, ConfirmOverwriteCallback p_cocConfirmOverwrite, Int32? p_intCategoryOverrideId)
			{
				return AddMod(p_uriPath, p_cocConfirmOverwrite, p_intCategoryOverrideId, null);
			}

			/// <summary>
			/// Adds a mod with an optional explicit queue-operation identity for external request correlation.
			/// </summary>
			public IBackgroundTask AddMod(Uri p_uriPath, ConfirmOverwriteCallback p_cocConfirmOverwrite, Int32? p_intCategoryOverrideId, Guid? p_gudQueueOperationId)
			{
				if (p_gudQueueOperationId.HasValue && p_gudQueueOperationId.Value == Guid.Empty)
					throw new ArgumentException("A non-empty AddMod queue-operation identifier is required when correlation is requested.", nameof(p_gudQueueOperationId));

				AddModTask amtModAdder = null;
				bool booIsRemote = p_uriPath.Scheme.ToLowerInvariant().ToString() == "nxm";
				bool booQueueTask = false;

				lock (m_dicActiveTasks)
				{
					AddModTask existingTask;
					if (p_gudQueueOperationId.HasValue && m_dicActiveTasksByOperationId.TryGetValue(p_gudQueueOperationId.Value, out existingTask))
						return existingTask;

					if (m_dicActiveTasks.TryGetValue(p_uriPath, out existingTask))
					{
						if (p_gudQueueOperationId.HasValue && existingTask.QueueOperationId != p_gudQueueOperationId.Value)
							throw new InvalidOperationException("The requested source is already owned by another active AddMod queue operation. Shared acquisition ownership is not enabled at this stage.");
						return existingTask;
					}

					// Only replace persisted restart correlation when this call is actually creating a new producer.
					// A live producer with the same operation ID remains authoritative and must not lose its restart descriptor.
					if (p_gudQueueOperationId.HasValue)
						RemoveStalePersistedCorrelation(p_gudQueueOperationId.Value, p_uriPath);

					Trace.TraceInformation(String.Format("[{0}] Adding Mod to AddModQueue", SanitizeDownloadUri(p_uriPath)));
					Guid queueOperationId = p_gudQueueOperationId ?? Guid.NewGuid();
					amtModAdder = new AddModTask(m_mmgModManager.GameMode, m_mmgModManager.ReadMeManager, m_mmgModManager.EnvironmentInfo, m_mmgModManager.ManagedModRegistry, m_mmgModManager.FormatRegistry, m_mmgModManager.ModRepository, p_uriPath, p_cocConfirmOverwrite, p_intCategoryOverrideId, m_mmgModManager.SortOrderService, queueOperationId);
					amtModAdder.TaskEnded += new EventHandler<TaskEndedEventArgs>(ModAdder_TaskEnded);
					amtModAdder.IsRemote = booIsRemote;
					m_dicActiveTasks[p_uriPath] = amtModAdder;
					m_dicActiveTasksByOperationId[queueOperationId] = amtModAdder;
					booQueueTask = booIsRemote || LocalTaskCount > 1;
				}

				m_mmgModManager.DownloadMonitor.AddActivity(amtModAdder);
				amtModAdder.AddMod(booQueueTask);
				return amtModAdder;
			}

			/// <summary>
			/// Removes an older persisted URI for the same external queue operation before fresh authorization is queued.
			/// </summary>
			private void RemoveStalePersistedCorrelation(Guid queueOperationId, Uri currentSource)
			{
				if (!m_eifEnvironmentInfo.Settings.QueuedModsToAdd.ContainsKey(m_mmgModManager.GameMode.ModeId))
					return;
				KeyedSettings<AddModDescriptor> queued = m_eifEnvironmentInfo.Settings.QueuedModsToAdd[m_mmgModManager.GameMode.ModeId];
				if (queued == null)
					return;
				string currentKey = currentSource == null ? null : currentSource.ToString();
				List<string> staleKeys = queued
					.Where(pair => pair.Value != null && pair.Value.QueueOperationId.HasValue &&
						pair.Value.QueueOperationId.Value == queueOperationId &&
						!StringComparer.Ordinal.Equals(pair.Key, currentKey))
					.Select(pair => pair.Key).ToList();
				if (staleKeys.Count == 0)
					return;
				foreach (string staleKey in staleKeys)
					queued.Remove(staleKey);
				lock (m_eifEnvironmentInfo.Settings)
					m_eifEnvironmentInfo.Settings.Save();
			}

			/// <summary>
			/// Redacts Nexus temporary download authorization before a URI reaches diagnostics.
			/// </summary>
			private static string SanitizeDownloadUri(Uri uri)
			{
				return ApiDiagnosticSanitizer.SanitizeUri(uri, null, true);
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
				if ((e.Status != TaskStatus.Paused) && (e.Status != TaskStatus.Queued))
				{
					lock (m_dicActiveTasks)
					{
						Uri uriKey = (from k in m_dicActiveTasks
									  where (k.Value == sender)
									  select k.Key).FirstOrDefault();
						if (uriKey != null)
							m_dicActiveTasks.Remove(uriKey);
						AddModTask endedTask = sender as AddModTask;
						if (endedTask != null)
							m_dicActiveTasksByOperationId.Remove(endedTask.QueueOperationId);
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
