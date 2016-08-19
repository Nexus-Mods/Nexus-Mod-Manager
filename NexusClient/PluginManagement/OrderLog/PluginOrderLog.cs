using System;
using System.Collections.Generic;
using System.Diagnostics;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
using Nexus.Transactions;

namespace Nexus.Client.PluginManagement.OrderLog
{
	/// <summary>
	/// The log that tracks the order of plugins.
	/// </summary>
	/// <remarks>
	/// The plugin order log can only be accessed by one install task at a time, so this
	/// object is a singleton to help enforce that policy.
	/// Note, however, that the singleton nature of the log is not meant to provide global access to the object.
	/// As such, there is no static accessor to retrieve the singleton instance. Instead, the
	/// <see cref="Initialize"/> method returns the only instance that should be used.
	/// </remarks>
	public partial class PluginOrderLog : IPluginOrderLog
	{
		private static readonly object m_objEnlistmentLock = new object();
		private static Dictionary<string, TransactionEnlistment> m_dicEnlistments = null;

		#region Singleton

		private static PluginOrderLog m_polCurrent = null;

		/// <summary>
		/// Initializes the plugin log.
		/// </summary>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_posOrderSerializer">The object that serializes and deserializes
		/// data from an active plugin log permanent store.</param>
		/// <param name="p_povOrderValidator">The object that validates plugin order.</param>
		/// <exception cref="InvalidOperationException">Thrown if the plugin order log has already
		/// been initialized.</exception>
		public static PluginOrderLog Initialize(PluginRegistry p_mprManagedPluginRegistry, IPluginOrderLogSerializer p_posOrderSerializer, IPluginOrderValidator p_povOrderValidator)
		{
			if (m_polCurrent != null)
				throw new InvalidOperationException("The Plugin Order Log has already been initialized.");
			m_polCurrent = new PluginOrderLog(p_mprManagedPluginRegistry, p_posOrderSerializer, p_povOrderValidator);
			return m_polCurrent;
		}

		/// <summary>
		/// This disposes of the singleton object, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
			m_polCurrent = null;
		}

		#endregion

		private ThreadSafeObservableList<Plugin> m_oclOrderedPlugins = null;
		private ReadOnlyObservableList<Plugin> m_rolOrderedPlugins = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.
		/// </summary>
		/// <value>The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</value>
		protected PluginRegistry ManagedPluginRegistry { get; private set; }

		/// <summary>
		/// Gets the object that serializes and deserializes the plugin order
		/// to and from a permanent store.
		/// </summary>
		/// <value>The object that serializes and deserializes the plugin order
		/// to and from a permanent store.</value>
		protected IPluginOrderLogSerializer LogSerializer { get; private set; }

		/// <summary>
		/// Gets the object that validates plugin order.
		/// </summary>
		/// <value>The object that validates plugin order.</value>
		protected IPluginOrderValidator OrderValidator { get; private set; }

		/// <summary>
		/// Gets the list of ordered plugins.
		/// </summary>
		/// <value>The list of ordered plugins.</value>
		public ReadOnlyObservableList<Plugin> OrderedPlugins
		{
			get
			{
				if (Transaction.Current == null)
					return m_rolOrderedPlugins;
				return GetEnlistment().OrderedPlugins;
			}
		}

		#endregion
		
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_posOrderSerializer">The object that serializes and deserializes
		/// data from a plugin order log permanent store.</param>
		/// <param name="p_povOrderValidator">The object that validates plugin order.</param>
		private PluginOrderLog(PluginRegistry p_mprManagedPluginRegistry, IPluginOrderLogSerializer p_posOrderSerializer, IPluginOrderValidator p_povOrderValidator)
		{			
			ManagedPluginRegistry = p_mprManagedPluginRegistry;
			LogSerializer = p_posOrderSerializer;
			OrderValidator = p_povOrderValidator;
			LoadPluginOrder();
			m_rolOrderedPlugins = new ReadOnlyObservableList<Plugin>(m_oclOrderedPlugins);
		}

		#endregion

		#region Serialization/Deserialization

		/// <summary>
		/// Loads the plugin order data from the permanent store.
		/// </summary>
		private void LoadPluginOrder()
		{
			Trace.TraceInformation("Loading Plugin Order...");
			Trace.Indent();
			m_oclOrderedPlugins = new ThreadSafeObservableList<Plugin>();
			if (LogSerializer != null)
				foreach (string strPlugin in LogSerializer.LoadPluginOrder())
				{
					Plugin plgPlugin = ManagedPluginRegistry.GetPlugin(strPlugin);
					Trace.TraceInformation("Loading {0} (IsNull={1})", strPlugin, (plgPlugin == null));
					if ((plgPlugin != null) && !m_oclOrderedPlugins.Contains(plgPlugin))
						m_oclOrderedPlugins.Add(plgPlugin);
				}
			Trace.Unindent();
		}

		/// <summary>
		/// Save the data to the Install Log file.
		/// </summary>
		protected void SavePluginLog()
		{
			LogSerializer.SavePluginOrder(m_oclOrderedPlugins);
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

		#region Plugin Order Management

		/// <summary>
		/// Sets the order of the plugins to the given order.
		/// </summary>
		/// <remarks>
		/// If the given list does not include all registered plugins, then the plugins are ordered in a manner
		/// so as to not displace the positions of the plugins whose order was not specified.
		/// </remarks>
		/// <param name="p_lstOrderedPlugins">The list indicating the desired order of the plugins.</param>
		public void SetPluginOrder(IList<Plugin> p_lstOrderedPlugins)
		{
			GetEnlistment().SetPluginOrder(p_lstOrderedPlugins);
		}

		/// <summary>
		/// Sets the load order of the specified plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The full path to the plugin file whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		public void SetPluginOrderIndex(Plugin p_plgPlugin, int p_intNewIndex)
		{
			List<Plugin> lstPlugins = new List<Plugin>(OrderedPlugins);
			lstPlugins.Remove(p_plgPlugin, PluginComparer.Filename);
			if (p_intNewIndex > lstPlugins.Count)
				p_intNewIndex = lstPlugins.Count;
			lstPlugins.Insert(p_intNewIndex, p_plgPlugin);
			SetPluginOrder(lstPlugins);
		}

		/// <summary>
		/// Removes the given plugin from the order list.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to remove from the order list.</param>
		public void RemovePlugin(Plugin p_plgPlugin)
		{
			GetEnlistment().RemovePlugin(p_plgPlugin);
		}

		#endregion
	}
}
