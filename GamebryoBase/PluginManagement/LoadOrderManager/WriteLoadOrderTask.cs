using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.BackgroundTasks;


namespace Nexus.Client.Games.Gamebryo.PluginManagement.LoadOrder
{
	public class WriteLoadOrderTask : ThreadedBackgroundTask
	{
		#region Fields

		private static readonly Object m_objLock = new Object();
		protected string FilePath { get; private set; }
		protected string[] Plugins { get; private set; }
		protected bool TimestampLoadOrder { get; private set; }
		protected DateTime MasterDate { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public WriteLoadOrderTask(string p_strFilePath, string[] p_strPlugins, bool p_booTimestamp, DateTime p_dtiMasterDate)
		{
			FilePath = p_strFilePath;
			Plugins = p_strPlugins;
			TimestampLoadOrder = p_booTimestamp;
			MasterDate = p_dtiMasterDate;
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
		public void Update()
		{
			Start();
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			base.Cancel();
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = Plugins.Count();
			ShowItemProgress = false;

			if (TimestampLoadOrder)
				SetTimestampLoadOrder(Plugins);
			else
				WriteLoadOrderFile(FilePath, Plugins);

			return null;
		}

		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
		/// <remarks>
		/// <param name="p_strPlugins">The list of plugins in the desired order.</param>
		private void SetTimestampLoadOrder(string[] p_strPlugins)
		{
			lock (m_objLock)
			{
				for (int i = 0; i < p_strPlugins.Length; i++)
				{
					string strPluginFile = p_strPlugins[i];
					if (!String.IsNullOrWhiteSpace(strPluginFile) && (File.Exists(strPluginFile)))
					{
						int intRepeat = 0;
						bool booLocked = true;

						while (!IsFileReady(strPluginFile))
						{
							Thread.Sleep(100);
							if (intRepeat++ > 10)
							{
								booLocked = true;
								break;
							}
						}

						if(!booLocked)
							File.SetLastWriteTime(strPluginFile, MasterDate.AddMinutes(i));
					}
				}
			}
		}

		/// <summary>
		/// Writes the plugin load order to the text file.
		/// </summary>
		private void WriteLoadOrderFile(string p_strFilePath, string[] p_strPlugins)
		{
			int intRepeat = 0;
			bool booLocked = false;

			while (!IsFileReady(p_strFilePath))
			{
				Thread.Sleep(500);
				if (intRepeat++ > 20)
				{
					booLocked = true;
					break;
				}
			}

			if (!booLocked)
			{
				int intRetries = 0;

				while (intRetries++ < 100)
				{
					try
					{
						lock (m_objLock)
						{
							using (StreamWriter swFile = new StreamWriter(p_strFilePath))
							{
								foreach (string plugin in p_strPlugins)
									swFile.WriteLine(plugin);
							}
						}
						break;
					}
					catch (IOException e)
					{
						var errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(e) & ((1 << 16) - 1);

						if (errorCode == 32 || errorCode == 33)
						{
							if (intRetries >= 100)
							{
								using (StreamWriter swFile = new StreamWriter(p_strFilePath + ".failed"))
								{
									foreach (string plugin in p_strPlugins)
										swFile.WriteLine(plugin);
								}
								throw e;
							}

							Thread.Sleep(100);
						}
						else
							throw e;
					}
				}
			}
		}

		/// <summary>
		/// Checks whether the file to write to is currently free for use.
		/// </summary>
		private static bool IsFileReady(String p_strFilePath)
		{
			try
			{
				using (FileStream inputStream = File.Open(p_strFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
				{
					return (inputStream.Length >= 0);
				}
			}
			catch (Exception)
			{
				return false;
			}
		}
	}
}

