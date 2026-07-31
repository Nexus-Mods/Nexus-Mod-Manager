using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
using Nexus.Transactions;

namespace Nexus.Client.PluginManagement
{
	public partial class PluginRegistry
	{
		/// <summary>
		/// Tracks the changes made to an <see cref="PluginRegistry"/> in the scope of a single
		/// <see cref="Transaction"/>. This also provides to mean to commit and rollback the
		/// tracked changes.
		/// </summary>
		private class TransactionEnlistment : IEnlistmentNotification
		{
			private Set<Plugin> m_setManagedPlugins = new Set<Plugin>(PluginComparer.Filename);
			private Set<Plugin> m_setRemovedPlugins = new Set<Plugin>(PluginComparer.Filename);
			private Dictionary<string, Plugin> m_dicManagedPlugins = new Dictionary<string, Plugin>(StringComparer.OrdinalIgnoreCase);
			private HashSet<string> m_hstRemovedPluginPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			private bool m_booEnlisted = false;

			#region Properties

			/// <summary>
			/// Gets the transaction into which we are enlisting.
			/// </summary>
			/// <value>The transaction into which we are enlisting.</value>
			protected Transaction CurrentTransaction { get; private set; }

			/// <summary>
			/// Gets the <see cref="PluginRegistry"/> whose actions are being transacted.
			/// </summary>
			/// <value>The <see cref="PluginRegistry"/> whose actions are being transacted.</value>
			protected PluginRegistry EnlistedPluginRegistry { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_txTransaction">The transaction into which we are enlisting.</param>
			/// <param name="p_prgPluginRegistry">The <see cref="PluginRegistry"/> whose actions are being transacted.</param>
			public TransactionEnlistment(Transaction p_txTransaction, PluginRegistry p_prgPluginRegistry)
			{
				CurrentTransaction = p_txTransaction;
				EnlistedPluginRegistry = p_prgPluginRegistry;
			}

			#endregion

			#region IEnlistmentNotification Members
			
			/// <summary>
			/// Commits the changes to the <see cref="ActivePluginLog"/>.
			/// </summary>
			public void Commit()
			{
				foreach (Plugin plgNew in m_setManagedPlugins)
				{
					EnlistedPluginRegistry.m_ostRegisteredPlugins.Add(plgNew);
					EnlistedPluginRegistry.m_dicRegisteredPlugins[plgNew.Filename] = plgNew;
				}

				EnlistedPluginRegistry.m_ostRegisteredPlugins.RemoveRange(m_setRemovedPlugins);

				foreach (Plugin plgRemoved in m_setRemovedPlugins)
				{
					Plugin removedPlugin;
					EnlistedPluginRegistry.m_dicRegisteredPlugins.TryRemove(plgRemoved.Filename, out removedPlugin);
				}
				
				m_booEnlisted = false;
				m_setManagedPlugins.Clear();
				m_setRemovedPlugins.Clear();
				m_dicManagedPlugins.Clear();
				m_hstRemovedPluginPaths.Clear();
			}

			/// <summary>
			/// Used to notify an enlisted resource manager that the transaction is being committed.
			/// </summary>
			/// <param name="enlistment">The enlistment class used to communicate with the resource manager.</param>
			public void Commit(Enlistment enlistment)
			{
				Commit();
				m_dicEnlistments.Remove(CurrentTransaction.TransactionInformation.LocalIdentifier);
				enlistment.Done();
			}

			/// <summary>
			/// Used to notify an enlisted resource manager that the transaction is in doubt.
			/// </summary>
			/// <remarks>
			/// A transaction is in doubt if it has not received votes from all enlisted resource managers
			/// as to the state of the transaciton.
			/// </remarks>
			/// <param name="enlistment">The enlistment class used to communicate with the resource manager.</param>
			public void InDoubt(Enlistment enlistment)
			{
				Rollback(enlistment);
			}

			/// <summary>
			/// Used to notify an enlisted resource manager that the transaction is being prepared for commitment.
			/// </summary>
			/// <param name="preparingEnlistment">The enlistment class used to communicate with the resource manager.</param>
			public void Prepare(PreparingEnlistment preparingEnlistment)
			{
				preparingEnlistment.Prepared();
			}

			/// <summary>
			/// Used to notify an enlisted resource manager that the transaction is being rolled back.
			/// </summary>
			/// <param name="enlistment">The enlistment class used to communicate with the resource manager.</param>
			public void Rollback(Enlistment enlistment)
			{
				m_booEnlisted = false;
				m_setManagedPlugins.Clear();
				m_setRemovedPlugins.Clear();
				m_dicManagedPlugins.Clear();
				m_hstRemovedPluginPaths.Clear();
				m_dicEnlistments.Remove(CurrentTransaction.TransactionInformation.LocalIdentifier);
				enlistment.Done();
			}

			#endregion

			/// <summary>
			/// Enlists the install log into the current transaction.
			/// </summary>
			private void Enlist()
			{
				if (!m_booEnlisted)
				{
					CurrentTransaction.EnlistVolatile(this, EnlistmentOptions.None);
					m_booEnlisted = true;
				}
			}

			/// <summary>
			/// Registers the specified plugin.
			/// </summary>
			/// <param name="p_strPluginPath">The path to the plugin to register.</param>
			/// <returns><c>true</c> if the specified plugin was registered;
			/// <c>false</c> otherwise.</returns>
			public bool RegisterPlugin(string p_strPluginPath)
			{
				Plugin plgPlugin;

				if (String.IsNullOrWhiteSpace(p_strPluginPath))
					return false;

				if (m_dicManagedPlugins.ContainsKey(p_strPluginPath))
					return true;

				if (!m_hstRemovedPluginPaths.Contains(p_strPluginPath) && EnlistedPluginRegistry.m_dicRegisteredPlugins.ContainsKey(p_strPluginPath))
					return true;

				plgPlugin = EnlistedPluginRegistry.PluginFactory.CreatePlugin(p_strPluginPath);
				if (plgPlugin == null)
					return false;

				m_setManagedPlugins.Add(plgPlugin);
				m_dicManagedPlugins[plgPlugin.Filename] = plgPlugin;
				m_setRemovedPlugins.Remove(plgPlugin);
				m_hstRemovedPluginPaths.Remove(plgPlugin.Filename);

				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();

				return true;
			}

			/// <summary>
			/// Removes the specified plugin from the registry.
			/// </summary>
			/// <param name="p_plgPlugin">The plugin to unregister.</param>
			public void UnregisterPlugin(Plugin p_plgPlugin)
			{
				UnregisterPlugins(p_plgPlugin == null ? new List<Plugin>() : new List<Plugin> { p_plgPlugin });
			}

			/// <summary>
			/// Tracks multiple plugin removals and enlists once.
			/// </summary>
			/// <param name="p_lstPlugins">The plugins to unregister.</param>
			public void UnregisterPlugins(IList<Plugin> p_lstPlugins)
			{
				bool booChanged = false;

				foreach (Plugin plgPlugin in (p_lstPlugins ?? new List<Plugin>()).Where(x => x != null).Distinct(PluginComparer.Filename))
				{
					m_setManagedPlugins.Remove(plgPlugin);
					m_dicManagedPlugins.Remove(plgPlugin.Filename);
					m_setRemovedPlugins.Add(plgPlugin);
					m_hstRemovedPluginPaths.Add(plgPlugin.Filename);
					booChanged = true;
				}

				if (!booChanged)
					return;

				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}

			/// <summary>
			/// Returns the plugin registered with the given path.
			/// </summary>
			/// <param name="p_strPluginPath">The path of the plugin to return</param>
			/// <returns>The plugin registered with the given path, or
			/// <c>null</c> if there is no registered plugin with the given path.</returns>
			public Plugin GetPlugin(string p_strPluginPath)
			{
				Plugin plgPlugin;

				if (String.IsNullOrWhiteSpace(p_strPluginPath) || m_hstRemovedPluginPaths.Contains(p_strPluginPath))
					return null;

				if (m_dicManagedPlugins.TryGetValue(p_strPluginPath, out plgPlugin))
					return plgPlugin;

				return EnlistedPluginRegistry.m_dicRegisteredPlugins.TryGetValue(p_strPluginPath, out plgPlugin) ? plgPlugin : null;
			}

		}
	}
}
