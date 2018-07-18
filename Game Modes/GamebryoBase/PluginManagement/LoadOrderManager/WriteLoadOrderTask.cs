using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ChinhDo.Transactions;
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
		protected bool ForcedReadOnly { get; private set; }
		protected DateTime MasterDate { get; private set; }

		#endregion

		TxFileManager txFileManager = new TxFileManager();

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public WriteLoadOrderTask(string p_strFilePath, string[] p_strPlugins, bool p_booTimestamp, bool p_booReadOnly, DateTime p_dtiMasterDate)
		{
			FilePath = p_strFilePath;
			Plugins = p_strPlugins;
			TimestampLoadOrder = p_booTimestamp;
			ForcedReadOnly = p_booReadOnly;
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
			KeyValuePair<string, string> kvpMD5 = new KeyValuePair<string, string>(null, null);

			if (TimestampLoadOrder)
			{
				SetTimestampLoadOrder(Plugins);
			}
			else
			{
				try
				{
					if (WriteLoadOrderFile(FilePath, Plugins))
					{
						using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
						{
							SHA256CryptoServiceProvider sha = new SHA256CryptoServiceProvider();
							byte[] hash = sha.ComputeHash(fs);
							string strSHA = BitConverter.ToString(hash).Replace("-", string.Empty);

							kvpMD5 = new KeyValuePair<string, string>(Path.GetFileName(FilePath), strSHA);
						}
					}
				}
				catch (UnauthorizedAccessException)
				{
					Trace.TraceError("Could not write load order to file \"{0}\".", FilePath);
				}
			}

			return kvpMD5;
		}

		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
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
						bool booLocked = false;

						while (!IsFileReady(strPluginFile, false))
						{
							Thread.Sleep(100);
							if (intRepeat++ > 10)
							{
								booLocked = true;
								break;
							}
						}

						if (!booLocked)
							File.SetLastWriteTime(strPluginFile, MasterDate.AddMinutes(i));
					}
				}
			}
		}

		/// <summary>
		/// Writes the plugin load order to the text file.
		/// </summary>
		private bool WriteLoadOrderFile(string p_strFilePath, string[] p_strPlugins)
		{
			int intRepeat = 0;
			bool booWritten = false;
			bool booLocked = false;

			Trace.TraceInformation("Preparing to write load order to file \"{0}\".", p_strFilePath);

			while (!IsFileReady(p_strFilePath, ForcedReadOnly))
			{
				Thread.Sleep(500);
				if (intRepeat++ > 20)
				{
					Trace.TraceWarning("Could not get access to \"{0}\".", p_strFilePath);
					booLocked = true;
					break;
				}
			}

			if (!booLocked)
			{
				Trace.TraceInformation("Got access to \"{0}\".", p_strFilePath);
				int intRetries = 0;

				while (intRetries++ < 100)
				{
					try
					{
						lock (m_objLock)
						{
							int intReadOnly = 0;

							while (IsFileReadOnly(p_strFilePath) && (intRetries < 10))
							{

								SetFileReadOnlyAccess(p_strFilePath, false);
								intReadOnly++;
								Thread.Sleep(100);
							}

							if (IsFileReadOnly(p_strFilePath) && (intRetries >= 10))
							{
								throw new Exception(string.Format("Unable to remove read-only flag from the {0} file.", p_strFilePath));
							}

							StringBuilder sbPlugins = new StringBuilder();

							foreach (string plugin in p_strPlugins)
							{
								sbPlugins.AppendLine(plugin);
							}

							using (StreamWriter swFile = new StreamWriter(p_strFilePath))
							{
								swFile.Write(sbPlugins.ToString());
							}

							booWritten = true;
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
								StringBuilder sbPlugins = new StringBuilder();

								foreach (string plugin in p_strPlugins)
								{
									sbPlugins.AppendLine(plugin);
								}

								using (StreamWriter swFile = new StreamWriter(p_strFilePath + ".failed"))
								{
									swFile.Write(sbPlugins.ToString());
								}

								throw e;
							}

							Thread.Sleep(100);
						}
						else
						{
							throw e;
						}
					}
					catch (UnauthorizedAccessException e)
					{
						if (intRetries >= 100)
						{
							StringBuilder sbPlugins = new StringBuilder();

							foreach (string plugin in p_strPlugins)
							{
								sbPlugins.AppendLine(plugin);
							}

							using (StreamWriter swFile = new StreamWriter(p_strFilePath + ".failed"))
							{
								swFile.Write(sbPlugins.ToString());
							}

							throw e;
						}

						Thread.Sleep(100);
					}
					finally
					{
						if (ForcedReadOnly)
						{
							SetFileReadOnlyAccess(p_strFilePath, true);
						}
					}
				}
			}

			return booWritten;
		}

		/// <summary>
		/// Checks whether the file to write to is currently free for use.
		/// </summary>
		private static bool IsFileReady(string p_strFilePath, bool p_booReadOnly)
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
				SetFileReadOnlyAccess(p_strFilePath, false);

				return false;
			}
		}

		/// <summary>
		/// Returns whether a file is read-only.
		/// </summary>
		private static bool IsFileReadOnly(string p_strFileName)
		{
			// Create a new FileInfo object.
			FileInfo fiInfo = new FileInfo(p_strFileName);

			// Return the IsReadOnly property value.
			return fiInfo.IsReadOnly;
		}

		/// <summary>
		/// Sets the read-only value of a file.
		/// </summary>
		private static void SetFileReadOnlyAccess(string p_strFileName, bool p_booSetReadOnly)
		{
			try
			{
				// Create a new FileInfo object.
				FileInfo fInfo = new FileInfo(p_strFileName);

				// Set the IsReadOnly property.
				fInfo.IsReadOnly = p_booSetReadOnly;
			}
			catch (Exception e)
			{
				Trace.TraceError("Could not set file read access of {0} to {1}: {2} - {3}", p_strFileName, p_booSetReadOnly, e.GetType(), e.Message);
			}
		}
	}
}
