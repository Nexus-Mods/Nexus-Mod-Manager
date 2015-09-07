using System;
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
					EnlistedPluginRegistry.m_ostRegisteredPlugins.Add(plgNew);
				foreach (Plugin plgRemoved in m_setRemovedPlugins)
					EnlistedPluginRegistry.m_ostRegisteredPlugins.Remove(plgRemoved);
				
				m_booEnlisted = false;
				m_setManagedPlugins.Clear();
				m_setRemovedPlugins.Clear();
			}

			/// <summary>
			/// Used to notify an enlisted resource manager that the transaction is being committed.
			/// </summary>
			/// <param name="p_eltEnlistment">The enlistment class used to communicate with the resource manager.</param>
			public void Commit(Enlistment p_eltEnlistment)
			{
				Commit();
				m_dicEnlistments.Remove(CurrentTransaction.TransactionInformation.LocalIdentifier);
				p_eltEnlistment.Done();
			}

			/// <summary>
			/// Used to notify an enlisted resource manager that the transaction is in doubt.
			/// </summary>
			/// <remarks>
			/// A transaction is in doubt if it has not received votes from all enlisted resource managers
			/// as to the state of the transaciton.
			/// </remarks>
			/// <param name="p_eltEnlistment">The enlistment class used to communicate with the resource manager.</param>
			public void InDoubt(Enlistment p_eltEnlistment)
			{
				Rollback(p_eltEnlistment);
			}

			/// <summary>
			/// Used to notify an enlisted resource manager that the transaction is being prepared for commitment.
			/// </summary>
			/// <param name="p_entPreparingEnlistment">The enlistment class used to communicate with the resource manager.</param>
			public void Prepare(PreparingEnlistment p_entPreparingEnlistment)
			{
				p_entPreparingEnlistment.Prepared();
			}

			/// <summary>
			/// Used to notify an enlisted resource manager that the transaction is being rolled back.
			/// </summary>
			/// <param name="p_eltEnlistment">The enlistment class used to communicate with the resource manager.</param>
			public void Rollback(Enlistment p_eltEnlistment)
			{
				m_booEnlisted = false;
				m_setManagedPlugins.Clear();
				m_setRemovedPlugins.Clear();
				m_dicEnlistments.Remove(CurrentTransaction.TransactionInformation.LocalIdentifier);
				p_eltEnlistment.Done();
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
				Plugin plgPlugin = null;
				if (m_setManagedPlugins.Contains(x => x.Filename.Equals(p_strPluginPath, StringComparison.OrdinalIgnoreCase)))
					return true;
				plgPlugin = EnlistedPluginRegistry.PluginFactory.CreatePlugin(p_strPluginPath);
				if (plgPlugin == null)
					return false;
				m_setManagedPlugins.Add(plgPlugin);
				m_setRemovedPlugins.Remove(plgPlugin);
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
				m_setManagedPlugins.Remove(p_plgPlugin);
				m_setRemovedPlugins.Add(p_plgPlugin);
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
				if (m_setRemovedPlugins.Contains(x => x.Filename.Equals(p_strPluginPath, StringComparison.OrdinalIgnoreCase)))
					return null;
				Plugin plgPlugin = m_setManagedPlugins.FirstOrDefault(x => x.Filename.Equals(p_strPluginPath, StringComparison.OrdinalIgnoreCase));
				if (plgPlugin == null)
					plgPlugin = EnlistedPluginRegistry.m_ostRegisteredPlugins.FirstOrDefault(x => x.Filename.Equals(p_strPluginPath, StringComparison.OrdinalIgnoreCase));
				return plgPlugin;
			}
		}
	}
}
