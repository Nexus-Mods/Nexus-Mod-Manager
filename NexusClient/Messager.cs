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
	/// This class enables communication between different instance of the client.
	/// </summary>
	/// <remarks>
	/// Only one instance of the client can be running per game mode. This class allows other instances of the client
	/// for the same game mode to send messages to the running instance to process.
	/// </remarks>
	public class Messager : MarshalByRefObject, IDisposable
	{
		private static IChannel m_cnlMessagerChannel = null;

		#region IPC Channel Setup

		/// <summary>
		/// Starts up the IPC listner channel to wait for message from other instances.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor of the game mode for which mods are being managed.</param>
		/// <param name="p_mmgModManager">The mod manager to use to manage mods.</param>
		/// <param name="p_frmMainForm">The main application form.</param>
		public static Messager InitializeListener(EnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo, ModManager p_mmgModManager, MainForm p_frmMainForm)
		{
			if (m_cnlMessagerChannel != null)
				throw new InvalidOperationException(String.Format("The IPC Channel has already been created as a {0}.", (m_cnlMessagerChannel is IpcServerChannel) ? "SERVER" : "CLIENT"));

			string strUri = String.Format("{0}-{1}IpcServer", p_eifEnvironmentInfo.Settings.ModManagerName, p_gmdGameModeInfo.ModeId);
			m_cnlMessagerChannel = new IpcServerChannel(strUri);
			ChannelServices.RegisterChannel(m_cnlMessagerChannel, true);
			Messager msgMessager = new Messager(p_mmgModManager, p_frmMainForm);
			string strEndpoint = String.Format("{0}Listener", p_gmdGameModeInfo.ModeId);
			RemotingServices.Marshal(msgMessager, strEndpoint, typeof(Messager));

			strUri += "/" + strEndpoint;
			string strTraceInfo = String.Format("Setting up listener on {0} at {1}", strUri, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			Trace.TraceInformation(strTraceInfo);
			
			return msgMessager;
		}

		/// <summary>
		/// Gets an instance of a <see cref="Messager"/> to use to talk to the running instance of the client.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor of the game mode for which mods are being managed.</param>
		/// <returns>An instance of a <see cref="Messager"/> to use to talk to the running instance of the client.</returns>
		public static Messager GetMessager(EnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo, out RemotingException exception)
		{
            exception = null;
			if (m_cnlMessagerChannel == null)
			{
				m_cnlMessagerChannel = new IpcClientChannel();
				ChannelServices.RegisterChannel(m_cnlMessagerChannel, true);
			}
			else if (m_cnlMessagerChannel is IpcServerChannel)
				throw new InvalidOperationException("The IPC Channel has already been created as a SERVER.");

			string strMessagerUri = String.Format("ipc://{0}-{1}IpcServer/{1}Listener", p_eifEnvironmentInfo.Settings.ModManagerName, p_gmdGameModeInfo.ModeId);
			Trace.TraceInformation(String.Format("Getting listener on: {0}", strMessagerUri));
			Messager msgMessager = (Messager)Activator.GetObject(typeof(Messager), strMessagerUri);

            //Just because a messager has been returned, dosn't mean it exists.
            //All you've really done at this point is create an object wrapper of type "Messager" which has the same methods, properties etc...
            //You wont know if you've got a real object, until you invoke something, hence the post (Power on self test) method.
            try
            {
                //Try to call the power on self test method.
                msgMessager.Post();
                return msgMessager;
            }
            catch(RemotingException ex)
            {
                //Ignore any remoting exception, obviously it's not there.
                exception = ex;
                return null;
            }
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
		private Messager(ModManager p_mmgModManager, MainForm p_frmMainForm)
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
			if (m_cnlMessagerChannel == null)
				return;
			if (m_cnlMessagerChannel is IpcServerChannel)
				((IpcServerChannel)m_cnlMessagerChannel).StopListening(null);
			ChannelServices.UnregisterChannel(m_cnlMessagerChannel);
			m_cnlMessagerChannel = null;
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
        /// <remarks>This is called when the Messager is created across the remoting boundary.</remarks>
        public void Post()
        {            
        }
	}
}
