using System;
using System.Collections.Generic;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
using Nexus.Transactions;

namespace Nexus.Client.PluginManagement.InstallationLog
{
	/// <summary>
	/// The log that tracks plugins that have been enabled.
	/// </summary>
	/// <remarks>
	/// The plugin log can only be accessed by one install task at a time, so this
	/// object is a singleton to help enforce that policy.
	/// Note, however, that the singleton nature of the log is not meant to provide global access to the object.
	/// As such, there is no static accessor to retrieve the singleton instance. Instead, the
	/// <see cref="Initialize(PluginRegistry, IActivePluginLogSerializer)"/> method returns the only instance that should be used.
	/// </remarks>
	public partial class ActivePluginLog
	{
		private static readonly object m_objEnlistmentLock = new object();
		private static Dictionary<string, TransactionEnlistment> m_dicEnlistments = null;

		#region Singleton

		private static ActivePluginLog m_aplCurrent = null;

		/// <summary>
		/// Initializes the plugin log.
		/// </summary>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_plsSerializer">The object that serializes and deserializes
		/// data from an active plugin log permanent store.</param>
		/// <exception cref="InvalidOperationException">Thrown if the plugins log has already
		/// been initialized.</exception>
		public static ActivePluginLog Initialize(PluginRegistry p_mprManagedPluginRegistry, IActivePluginLogSerializer p_plsSerializer)
		{
			if (m_aplCurrent != null)
				throw new InvalidOperationException("The Active Plugin Log has already been initialized.");
			m_aplCurrent = new ActivePluginLog(p_mprManagedPluginRegistry, p_plsSerializer);
			return m_aplCurrent;
		}

		/// <summary>
		/// This disposes of the singleton object, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
			m_aplCurrent = null;
		}

		#endregion

		private ObservableSet<Plugin> m_ostActivePlugins = new ObservableSet<Plugin>(PluginComparer.Filename);
		private ReadOnlyObservableList<Plugin> m_rolActivePlugins = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.
		/// </summary>
		/// <value>The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</value>
		protected PluginRegistry ManagedPluginRegistry { get; private set; }

		/// <summary>
		/// Gets the object that serializes and deserializes the list of active plugins
		/// to and from a permanent store.
		/// </summary>
		/// <value>The object that serializes and deserializes the list of active plugins
		/// to and from a permanent store.</value>
		protected IActivePluginLogSerializer LogSerializer { get; private set; }

		/// <summary>
		/// Gets the list of active plugins.
		/// </summary>
		/// <value>The list of active plugins.</value>
		public ReadOnlyObservableList<Plugin> ActivePlugins
		{
			get
			{
				if (Transaction.Current == null)
					return m_rolActivePlugins;
				return GetEnlistment().ActivePlugins;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_plsSerializer">The object that serializes and deserializes
		/// data from an active plugin log permanent store.</param>
		private ActivePluginLog(PluginRegistry p_mprManagedPluginRegistry, IActivePluginLogSerializer p_plsSerializer)
		{
			ManagedPluginRegistry = p_mprManagedPluginRegistry;
			LogSerializer = p_plsSerializer;
			LoadPluginLog();
			m_rolActivePlugins = new ReadOnlyObservableList<Plugin>(m_ostActivePlugins);
		}

		#endregion

		#region Serialization/Deserialization

		/// <summary>
		/// Loads the data from the Install Log file.
		/// </summary>
		private void LoadPluginLog()
		{
			if (m_ostActivePlugins != null)
				m_ostActivePlugins.Clear();
			else
				m_ostActivePlugins = new ObservableSet<Plugin>(PluginComparer.Filename);

			if (LogSerializer != null)
				foreach (string strPlugin in LogSerializer.LoadPluginLog())
					if (!String.IsNullOrEmpty(strPlugin))
						m_ostActivePlugins.Add(ManagedPluginRegistry.GetPlugin(strPlugin));
		}

		/// <summary>
		/// Save the data to the Install Log file.
		/// </summary>
		protected void SavePluginLog()
		{
			LogSerializer.SavePluginLog(m_ostActivePlugins);
		}

		#endregion

		#region Transaction Handling

		/// <summary>
		/// Gets an enlistment into the ambient transaction, if one exists.
		/// </summary>
		/// <returns>An enlistment into the ambient transaction, or <c>null</c> if there is no ambient
		/// transaction.</returns>
		private TransactionEnlistment GetEnlistment()
		{
			Transaction txTransaction = Transaction.Current;
			TransactionEnlistment enlEnlistment = null; ;

			if (txTransaction != null)
			{
				lock (m_objEnlistmentLock)
				{
					if (m_dicEnlistments == null)
						m_dicEnlistments = new Dictionary<string, TransactionEnlistment>();

					if (m_dicEnlistments.ContainsKey(txTransaction.TransactionInformation.LocalIdentifier))
						enlEnlistment = m_dicEnlistments[txTransaction.TransactionInformation.LocalIdentifier];
					else
					{
						enlEnlistment = new TransactionEnlistment(txTransaction, this);
						m_dicEnlistments.Add(txTransaction.TransactionInformation.LocalIdentifier, enlEnlistment);
					}
				}
			}
			else
				enlEnlistment = new TransactionEnlistment(null, this);

			return enlEnlistment;
		}

		#endregion

		#region Plugin Activation/Deactivation

		/// <summary>
		/// Activates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to activate.</param>
		public void ActivatePlugin(Plugin p_plgPlugin)
		{
			if (!IsPluginActive(p_plgPlugin))
				GetEnlistment().ActivatePlugin(p_plgPlugin);
		}

		/// <summary>
		/// Activates the given plugin.
		/// </summary>
		/// <param name="p_lstPlugins">The list of plugin to activate.</param>
		public void ActivatePlugins(IList<Plugin> p_lstPlugins)
		{
			GetEnlistment().ActivatePlugins(p_lstPlugins);
		}

		/// <summary>
		/// Activates the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin to activate.</param>
		public void ActivatePlugin(string p_strPath)
		{
			ActivatePlugin(ManagedPluginRegistry.GetPlugin(p_strPath));
		}

		/// <summary>
		/// Deactivates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to deactivate.</param>
		public void DeactivatePlugin(Plugin p_plgPlugin)
		{
			GetEnlistment().DeactivatePlugin(p_plgPlugin);
		}

		/// <summary>
		/// Deactivates the given plugin.
		/// </summary>
		/// <param name="p_lstPlugins">The list of plugin to deactivate.</param>
		public void DeactivatePlugins(IList<Plugin> p_lstPlugins)
		{
			GetEnlistment().DeactivatePlugins(p_lstPlugins);
		}

		/// <summary>
		/// Deactivates the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin to deactivate.</param>
		public void DeactivatePlugin(string p_strPath)
		{
			DeactivatePlugin(ManagedPluginRegistry.GetPlugin(p_strPath));
		}

		/// <summary>
		/// Determines if the given plugin is active.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose active state is to be determined.</param>
		/// <returns><c>true</c> if the given plugin is active;
		/// <c>false</c> otherwise.</returns>
		public bool IsPluginActive(Plugin p_plgPlugin)
		{
			return GetEnlistment().IsPluginActive(p_plgPlugin);
		}

		#endregion
	}
}
