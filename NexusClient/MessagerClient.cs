using System;
using System.Diagnostics;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using Nexus.Client.Games;
using Nexus.Client.Util;

namespace Nexus.Client
{
	/// <summary>
	/// This class enables communication between different instance of the mod manager.
	/// </summary>
	/// <remarks>
	/// Only one instance of the client can be running per game mode. This class allows one instance of the mod manager
	/// to send messages to the running instance to process.
	/// </remarks>
	public class MessagerClient : IMessager
	{
		private static IpcClientChannel m_cchMessagerChannel = null;

		#region IPC Channel Setup

		/// <summary>
		/// Gets an instance of a <see cref="Messager"/> to use to talk to the running instance of the client.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor of the game mode for which mods are being managed.</param>
		/// <returns>An instance of a <see cref="Messager"/> to use to talk to the running instance of the client,
		/// or <c>null</c> if no valid <see cref="Messager"/> could be created.</returns>
		public static IMessager GetMessager(EnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo)
		{
			if (m_cchMessagerChannel == null)
			{
				System.Collections.IDictionary properties = new System.Collections.Hashtable();
				properties["exclusiveAddressUse"] = false;
				m_cchMessagerChannel = new IpcClientChannel();
				ChannelServices.RegisterChannel(m_cchMessagerChannel, true);
			}
			else
				throw new InvalidOperationException("The IPC Channel has already been created as a CLIENT.");

			string strMessagerUri = String.Format("ipc://{0}-{1}IpcServer/{1}Listener", p_eifEnvironmentInfo.Settings.ModManagerName, p_gmdGameModeInfo.ModeId);
			IMessager msgMessager = null;
			try
			{
				Trace.TraceInformation(String.Format("Getting listener on: {0}", strMessagerUri));
				msgMessager = (IMessager)Activator.GetObject(typeof(IMessager), strMessagerUri);

				//Just because a messager has been returned, dosn't mean it exists.
				//All you've really done at this point is create an object wrapper of type "Messager" which has the same methods, properties etc...
				//You wont know if you've got a real object, until you invoke something, hence the post (Power on self test) method.
				msgMessager.Post();
			}
			catch (RemotingException e)
			{
				Trace.TraceError("Could not get Messager: {0}", strMessagerUri);
				TraceUtil.TraceException(e);
				return null;
			}
			return new MessagerClient(msgMessager);
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the proxy to use to send messages to the running instance of the mod manager.
		/// </summary>
		/// <value>The proxy to use to send messages to the running instance of the mod manager.</value>
		protected IMessager Messager { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_msgMessager">The proxy to use to send messages to the running instance of the mod manager.</param>
		private MessagerClient(IMessager p_msgMessager)
		{
			Messager = p_msgMessager;
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
			if (m_cchMessagerChannel == null)
				return;
			ChannelServices.UnregisterChannel(m_cchMessagerChannel);
			m_cchMessagerChannel = null;
		}

		#endregion

		#region Mod Addition

		/// <summary>
		/// Adds the specified mod to the mod manager.
		/// </summary>
		/// <param name="p_strFilePath">The path or URL of the mod to add to the mod manager.</param>
		public void AddMod(string p_strFilePath)
		{
			Messager.AddMod(p_strFilePath);
		}

		#endregion

		/// <summary>
		/// Brings the currently running client to the front.
		/// </summary>
		public void BringToFront()
		{
			Messager.BringToFront();
		}

		/// <summary>Used as a simple Power On Self Test method.</summary>
		/// <remarks>
		/// This method can be called to ensure a Messager is alive.
		/// </remarks>
		public void Post()
		{
			Messager.Post();
		}
	}
}
