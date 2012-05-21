using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;

namespace Nexus.Client
{
	/// <summary>
	/// This class enables communication between different instance of the mod manager.
	/// </summary>
	/// <remarks>
	/// Only one instance of the client can be running per game mode. This class listens for messages sent from
	/// other instances of the mod manager to be processed.
	/// </remarks>
	public class MessagerServer : MarshalByRefObject, IMessager
	{
		private static IpcServerChannel m_schMessagerChannel = null;

		#region IPC Channel Setup

		/// <summary>
		/// Starts up the IPC listner channel to wait for message from other instances.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor of the game mode for which mods are being managed.</param>
		/// <param name="p_mmgModManager">The mod manager to use to manage mods.</param>
		/// <param name="p_frmMainForm">The main application form.</param>
		public static IMessager InitializeListener(EnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo, ModManager p_mmgModManager, MainForm p_frmMainForm)
		{
			if (m_schMessagerChannel != null)
				throw new InvalidOperationException("The IPC Channel has already been created as a SERVER.");

			string strUri = String.Format("{0}-{1}IpcServer", p_eifEnvironmentInfo.Settings.ModManagerName, p_gmdGameModeInfo.ModeId);
			m_schMessagerChannel = new IpcServerChannel(strUri);
			ChannelServices.RegisterChannel(m_schMessagerChannel, true);
			MessagerServer msgMessager = new MessagerServer(p_mmgModManager, p_frmMainForm);
			string strEndpoint = String.Format("{0}Listener", p_gmdGameModeInfo.ModeId);
			RemotingServices.Marshal(msgMessager, strEndpoint, typeof(IMessager));

			strUri += "/" + strEndpoint;
			string strTraceInfo = String.Format("Setting up listener on {0} at {1}", strUri, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			Trace.TraceInformation(strTraceInfo);

			return msgMessager;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the mod manager to use to manage mods.
		/// </summary>
		/// <value>The mod manager to use to manage mods.</value>
		protected ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets the main application form.
		/// </summary>
		/// <value>The main application form.</value>
		protected MainForm MainForm { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor the initializes the object with the required dependencies.
		/// </summary>
		/// <param name="p_mmgModManager">The mod manager to use to manage mods.</param>
		/// <param name="p_frmMainForm">The main form of the client for which we listening for messages.</param>
		private MessagerServer(ModManager p_mmgModManager, MainForm p_frmMainForm)
		{
			ModManager = p_mmgModManager;
			MainForm = p_frmMainForm;
		}

		#endregion

		#region IDisposable Members

		/// <summary>
		/// Disposes of the object.
		/// </summary>
		/// <remarks>
		/// This shuts down any open IPC channels.
		/// </remarks>
		public void Dispose()
		{
			if (m_schMessagerChannel == null)
				return;
			m_schMessagerChannel.StopListening(null);
			ChannelServices.UnregisterChannel(m_schMessagerChannel);
			m_schMessagerChannel = null;
			RemotingServices.Disconnect(this);
		}

		#endregion

		/// <summary>
		/// Creates the liftime object that determines how long the object lives.
		/// </summary>
		/// <returns>Always <c>null</c>, so that the object never dies.</returns>
		public override object InitializeLifetimeService()
		{
			return null;
		}

		#region Mod Addition

		/// <summary>
		/// Adds the specified mod to the mod manager.
		/// </summary>
		/// <param name="p_strFilePath">The path or URL of the mod to add to the mod manager.</param>
		public void AddMod(string p_strFilePath)
		{
			Trace.TraceInformation("Adding Mod to running instance of client: " + p_strFilePath);
			ModManager.AddMod(p_strFilePath, ConfirmFileOverwrite);
			BringToFront();
		}

		/// <summary>
		/// The callback that confirm a file overwrite.
		/// </summary>
		/// <param name="p_strOldFilePath">The path to the file that is to be overwritten.</param>
		/// <param name="p_strNewFilePath">An out parameter specifying the file to to which to
		/// write the file.</param>
		/// <returns><c>true</c> if the file should be written;
		/// <c>false</c> otherwise.</returns>
		protected bool ConfirmFileOverwrite(string p_strOldFilePath, out string p_strNewFilePath)
		{
			string strNewFileName = p_strOldFilePath;
			string strExtension = Path.GetExtension(p_strOldFilePath);
			string strDirectory = Path.GetDirectoryName(p_strOldFilePath);
			for (Int32 i = 2; i < Int32.MaxValue && File.Exists(strNewFileName); i++)
				strNewFileName = Path.Combine(strDirectory, String.Format("{0} ({1}){2}", Path.GetFileNameWithoutExtension(p_strOldFilePath), i, strExtension));
			if (File.Exists(strNewFileName))
				throw new Exception("Cannot write file. Unable to find unused file name.");
			p_strNewFilePath = ConfirmModFileOverwrite(p_strOldFilePath, strNewFileName);
			return (p_strNewFilePath != null);
		}

		/// <summary>
		/// This asks the use to confirm the overwriting of the specified mod file.
		/// </summary>
		/// <param name="p_strFileName">The name of the file that is to be overwritten.</param>
		/// <param name="p_strNewFileName">An alternate name the file can be renamed to, instead of being overwritten.</param>
		/// <returns>The name of the file to use to write the file, or <c>null</c> if the operation
		/// should be cancelled.</returns>
		private string ConfirmModFileOverwrite(string p_strFileName, string p_strNewFileName)
		{
			if (MainForm.InvokeRequired)
			{
				string strResult = null;
				MainForm.Invoke((MethodInvoker)(() => strResult = ConfirmModFileOverwrite(p_strFileName, p_strNewFileName)));
				return strResult;
			}
			if (!p_strFileName.Equals(p_strNewFileName))
			{
				switch (MessageBox.Show(MainForm, "File '" + p_strFileName + "' already exists. The old file can be replaced, or the new file can be named '" + p_strNewFileName + "'." + Environment.NewLine + "Do you want to overwrite the old file?", "Warning", MessageBoxButtons.YesNoCancel))
				{
					case DialogResult.Yes:
						return p_strFileName;
					case DialogResult.No:
						return p_strNewFileName;
					case DialogResult.Cancel:
						return null;
				}
			}
			return p_strFileName;
		}

		#endregion

		/// <summary>
		/// Brings the currently running client to the front.
		/// </summary>
		public void BringToFront()
		{
			if (MainForm.InvokeRequired)
				MainForm.Invoke(new MethodInvoker(DoBringToFront));
			else
				DoBringToFront();
		}

		/// <summary>
		/// Brings the currently running client to the front.
		/// </summary>
		private void DoBringToFront()
		{
			MainForm.RestoreFocus();
		}

		/// <summary>Used as a simple Power On Self Test method.</summary>
		/// <remarks>
		/// This method can be called to ensure a Messager is alive.
		/// </remarks>
		public void Post()
		{
		}
	}
}
