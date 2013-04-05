using System.Collections.Generic;
using System.Diagnostics;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
using Nexus.Transactions;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// A registry of all plugins being managed by the plugin manager.
	/// </summary>
	public partial class PluginRegistry
	{
		private static readonly object m_objEnlistmentLock = new object();
		private static Dictionary<string, TransactionEnlistment> m_dicEnlistments = null;

		/// <summary>
		/// Searches for plugins in the specified path, and loads
		/// any plugins that are found into a registry.
		/// </summary>
		/// <param name="p_pftFactory">The factory to use to create <see cref="Plugin"/>s.</param>
		/// <param name="p_pdvDiscoverer">The discoverer to use to search for plugins.</param>
		/// <returns>A registry containing all of the discovered plugin formats.</returns>
		public static PluginRegistry DiscoverManagedPlugins(IPluginFactory p_pftFactory, IPluginDiscoverer p_pdvDiscoverer)
		{
			Trace.TraceInformation("Discovering Managed Plugins...");
			Trace.Indent();

			PluginRegistry pgrRegistry = new PluginRegistry(p_pftFactory);
            if (p_pdvDiscoverer != null)
			    foreach (string strPlugin in p_pdvDiscoverer.FindPlugins())
			    {
				    Trace.TraceInformation("Found: {0}", strPlugin);
				    if (pgrRegistry.RegisterPlugin(strPlugin))
				    {
					    Trace.Indent();
					    Trace.TraceInformation("Registered.");
					    Trace.Unindent();
				    }
			    }
			Trace.Unindent();
			return pgrRegistry;
		}

		private ObservableSet<Plugin> m_ostRegisteredPlugins = new ObservableSet<Plugin>(PluginComparer.Filename);

		#region Properties

		/// <summary>
		/// Gets the list of registered plugins.
		/// </summary>
		/// <value>The list of installed plugins.</value>
		public ReadOnlyObservableList<Plugin> RegisteredPlugins { get; private set; }

		/// <summary>
		/// Gets the factory to use to create plugins.
		/// </summary>
		/// <value>The factory to use to create plugins.</value>
		protected IPluginFactory PluginFactory { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_pgfPluginFactory">The factory to use to create plugins.</param>
		public PluginRegistry(IPluginFactory p_pgfPluginFactory)
		{
			PluginFactory = p_pgfPluginFactory;
			RegisteredPlugins = new ReadOnlyObservableList<Plugin>(m_ostRegisteredPlugins);
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

		/// <summary>
		/// Registers the specified plugin.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to register.</param>
		/// <returns><c>true</c> if the specified plugin was registered;
		/// <c>false</c> otherwise.</returns>
		public bool RegisterPlugin(string p_strPluginPath)
		{
			return GetEnlistment().RegisterPlugin(p_strPluginPath);
		}

		/// <summary>
		/// Removes the given plugin from the registry.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to unregister.</param>
		public void UnregisterPlugin(Plugin p_plgPlugin)
		{
			GetEnlistment().UnregisterPlugin(p_plgPlugin);
		}

		/// <summary>
		/// Removes the specified plugin from the registry.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to unregister.</param>
		public void UnregisterPlugin(string p_strPluginPath)
		{
			GetEnlistment().UnregisterPlugin(GetPlugin(p_strPluginPath));
		}

		/// <summary>
		/// Returns the plugin registered with the given path.
		/// </summary>
		/// <param name="p_strPluginPath">The path of the plugin to return</param>
		/// <returns>The plugin registered with the given path, or
		/// <c>null</c> if there is no registered plugin with the given path.</returns>
		public Plugin GetPlugin(string p_strPluginPath)
		{
			return GetEnlistment().GetPlugin(p_strPluginPath);
		}

		/// <summary>
		/// Determines if the specified file is a plugin that can be activated for the game mode.
		/// </summary>
		/// <param name="p_strPath">The path to the file for which it is to be determined if it is a plugin file.</param>
		/// <returns><c>true</c> if the specified file is a plugin file that can be activated in the game mode;
		/// <c>false</c> otherwise.</returns>
		public bool IsActivatiblePluginFile(string p_strPath)
		{
            if (PluginFactory == null)
                return true;
			return PluginFactory.IsActivatiblePluginFile(p_strPath);
		}
	}
}
