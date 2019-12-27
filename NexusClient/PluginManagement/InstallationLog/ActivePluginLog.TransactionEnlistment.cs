using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
using Nexus.Transactions;

namespace Nexus.Client.PluginManagement.InstallationLog
{
	public partial class ActivePluginLog
	{
		/// <summary>
		/// Tracks the changes made to an <see cref="ActivePluginLog"/> in the scope of a single
		/// <see cref="Transaction"/>. This also provides to mean to commit and rollback the
		/// tracked changes.
		/// </summary>
		private class TransactionEnlistment : IEnlistmentNotification
		{
			private ObservableSet<Plugin> m_ostActivePlugins = null;
			private ReadOnlyObservableList<Plugin> m_rolActivePlugins = null;
			private bool m_booEnlisted = false;

			#region Properties

			/// <summary>
			/// Gets the transaction into which we are enlisting.
			/// </summary>
			/// <value>The transaction into which we are enlisting.</value>
			protected Transaction CurrentTransaction { get; private set; }

			/// <summary>
			/// Gets the <see cref="ActivePluginLog"/> whose actions are being transacted.
			/// </summary>
			/// <value>The <see cref="ActivePluginLog"/> whose actions are being transacted.</value>
			protected ActivePluginLog EnlistedPluginLog { get; private set; }

			/// <summary>
			/// Gets the list of active plugins.
			/// </summary>
			/// <value>The list of active plugins.</value>
			public ReadOnlyObservableList<Plugin> ActivePlugins
			{
				get
				{
					if (CurrentTransaction == null)
						return EnlistedPluginLog.m_rolActivePlugins;
					return m_rolActivePlugins;
				}
			}

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_txTransaction">The transaction into which we are enlisting.</param>
			/// <param name="p_aplPluginLog">The <see cref="ActivePluginLog"/> whose actions are being transacted.</param>
			public TransactionEnlistment(Transaction p_txTransaction, ActivePluginLog p_aplPluginLog)
			{
				CurrentTransaction = p_txTransaction;
				EnlistedPluginLog = p_aplPluginLog;
				m_ostActivePlugins = new ObservableSet<Plugin>(EnlistedPluginLog.m_ostActivePlugins, PluginComparer.Filename);
				m_rolActivePlugins = new ReadOnlyObservableList<Plugin>(m_ostActivePlugins);

				EnlistedPluginLog.m_ostActivePlugins.CollectionChanged += new NotifyCollectionChangedEventHandler(MasterPlugins_CollectionChanged);
			}

			#endregion

			#region IEnlistmentNotification Members

			/// <summary>
			/// Commits the changes to the <see cref="ActivePluginLog"/>.
			/// </summary>
			public void Commit()
			{
				lock (EnlistedPluginLog.m_ostActivePlugins)
				{
					EnlistedPluginLog.m_ostActivePlugins.CollectionChanged -= MasterPlugins_CollectionChanged;
					foreach (Plugin plgNew in m_ostActivePlugins)
						EnlistedPluginLog.m_ostActivePlugins.Add(plgNew);
					for (Int32 i = EnlistedPluginLog.m_ostActivePlugins.Count - 1; i >= 0; i--)
						if (!m_ostActivePlugins.Contains(EnlistedPluginLog.m_ostActivePlugins[i]))
							EnlistedPluginLog.m_ostActivePlugins.RemoveAt(i);
				}
				EnlistedPluginLog.SavePluginLog();

				m_booEnlisted = false;
				m_ostActivePlugins.Clear();;
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
				m_ostActivePlugins.Clear();
				m_dicEnlistments.Remove(CurrentTransaction.TransactionInformation.LocalIdentifier);
				enlistment.Done();
			}

			#endregion

			#region Helper Methods

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
			/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the
			/// master list of plugins.
			/// </summary>
			/// <remarks>
			/// This applies any changes that are made to the maser list to the transacted list
			/// with which we are currently working.
			/// </remarks>
			/// <param name="sender">The object that raised the event.</param>
			/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
			private void MasterPlugins_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
			{
				switch (e.Action)
				{
					case NotifyCollectionChangedAction.Add:
						foreach (Plugin plgAdded in e.NewItems)
							m_ostActivePlugins.Add(plgAdded);
						break;
					case NotifyCollectionChangedAction.Remove:
						foreach (Plugin plgRemoved in e.OldItems)
							m_ostActivePlugins.Remove(plgRemoved);
						break;
					case NotifyCollectionChangedAction.Replace:
						foreach (Plugin plgRemoved in e.OldItems)
							m_ostActivePlugins.Remove(plgRemoved);
						foreach (Plugin plgAdded in e.NewItems)
							m_ostActivePlugins.Add(plgAdded);
						break;
					case NotifyCollectionChangedAction.Reset:
						m_ostActivePlugins.Clear();
						break;
				}
			}

			#endregion

			#region Plugin Activation/Deactivation

			/// <summary>
			/// Activates the given plugin.
			/// </summary>
			/// <param name="p_plgPlugin">The plugin to activate.</param>
			public void ActivatePlugin(Plugin p_plgPlugin)
			{
				m_ostActivePlugins.Add(p_plgPlugin);
				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}

			/// <summary>
			/// Activates the given plugin.
			/// </summary>
			/// <param name="p_lstPlugins">The list of plugin to activate.</param>
			public void ActivatePlugins(IList<Plugin> p_lstPlugins)
			{
				m_ostActivePlugins.AddRange(p_lstPlugins);
				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}

			/// <summary>
			/// Deactivates the given plugin.
			/// </summary>
			/// <param name="p_plgPlugin">The plugin to deactivate.</param>
			public void DeactivatePlugin(Plugin p_plgPlugin)
			{
				m_ostActivePlugins.Remove(p_plgPlugin);
				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}

			/// <summary>
			/// Deactivates the given plugin.
			/// </summary>
			/// <param name="p_lstPlugins">The list of plugin to deactivate.</param>
			public void DeactivatePlugins(IList<Plugin> p_lstPlugins)
			{
				m_ostActivePlugins.RemoveRange(p_lstPlugins);
				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}

			/// <summary>
			/// Determines if the given plugin is active.
			/// </summary>
			/// <param name="p_plgPlugin">The plugin whose active state is to be determined.</param>
			/// <returns><c>true</c> if the given plugin is active;
			/// <c>false</c> otherwise.</returns>
			public bool IsPluginActive(Plugin p_plgPlugin)
			{
				return m_ostActivePlugins.Contains(p_plgPlugin);
			}

			#endregion
		}
	}
}
