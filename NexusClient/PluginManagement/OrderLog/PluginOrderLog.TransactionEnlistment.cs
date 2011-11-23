using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Nexus.Client.Plugins;
using Nexus.Client.Util;
using Nexus.Transactions;

namespace Nexus.Client.PluginManagement.OrderLog
{
	public partial class PluginOrderLog
	{
		/// <summary>
		/// Tracks the changes made to an <see cref="PluginOrderLog"/> in the scope of a single
		/// <see cref="Transaction"/>. This also provides to mean to commit and rollback the
		/// tracked changes.
		/// </summary>
		private class TransactionEnlistment : IEnlistmentNotification
		{
			private ThreadSafeObservableList<Plugin> m_oclOrderedPlugins = null;
			private ReadOnlyObservableList<Plugin> m_rolOrderedPlugins = null;
			private bool m_booEnlisted = false;

			#region Properties

			/// <summary>
			/// Gets the transaction into which we are enlisting.
			/// </summary>
			/// <value>The transaction into which we are enlisting.</value>
			protected Transaction CurrentTransaction { get; private set; }

			/// <summary>
			/// Gets the <see cref="PluginOrderLog"/> whose actions are being transacted.
			/// </summary>
			/// <value>The <see cref="PluginOrderLog"/> whose actions are being transacted.</value>
			protected PluginOrderLog EnlistedPluginOrderLog { get; private set; }

			/// <summary>
			/// Gets the list of ordered plugins.
			/// </summary>
			/// <value>The list of ordered plugins.</value>
			public ReadOnlyObservableList<Plugin> OrderedPlugins
			{
				get
				{
					if (CurrentTransaction == null)
						return EnlistedPluginOrderLog.m_rolOrderedPlugins;
					return m_rolOrderedPlugins;
				}
			}

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_txTransaction">The transaction into which we are enlisting.</param>
			/// <param name="p_polPluginOrderLog">The <see cref="PluginOrderLog"/> whose actions are being transacted.</param>
			public TransactionEnlistment(Transaction p_txTransaction, PluginOrderLog p_polPluginOrderLog)
			{
				CurrentTransaction = p_txTransaction;
				EnlistedPluginOrderLog = p_polPluginOrderLog;
				m_oclOrderedPlugins = new ThreadSafeObservableList<Plugin>(EnlistedPluginOrderLog.m_oclOrderedPlugins);
				m_rolOrderedPlugins = new ReadOnlyObservableList<Plugin>(m_oclOrderedPlugins);

				EnlistedPluginOrderLog.m_oclOrderedPlugins.CollectionChanged += new NotifyCollectionChangedEventHandler(MasterOrderedPlugins_CollectionChanged);
			}

			#endregion

			#region IEnlistmentNotification Members

			/// <summary>
			/// Commits the changes to the <see cref="PluginOrderLog"/>.
			/// </summary>
			public void Commit()
			{
				PluginComparer pcpComparer = PluginComparer.Filename;
				Dictionary<Plugin, Plugin> dicPredecessors = new Dictionary<Plugin, Plugin>();
				ThreadSafeObservableList<Plugin> oclUnorderedList = EnlistedPluginOrderLog.m_oclOrderedPlugins;

				lock (EnlistedPluginOrderLog.m_oclOrderedPlugins)
				{
					EnlistedPluginOrderLog.m_oclOrderedPlugins.CollectionChanged -= MasterOrderedPlugins_CollectionChanged;
					
					IList<Plugin> lstOrderedList = m_oclOrderedPlugins;
					
					//sort the items whose order has been specified
					for (Int32 i = 0; i < lstOrderedList.Count; i++)
					{
						Int32 intOldIndex = 0;
						for (intOldIndex = 0; intOldIndex < oclUnorderedList.Count; intOldIndex++)
							if (pcpComparer.Equals(oclUnorderedList[intOldIndex], lstOrderedList[i]))
								break;
						if (intOldIndex == oclUnorderedList.Count)
						{
							oclUnorderedList.Insert(i, lstOrderedList[i]);
							continue;
						}
						if (intOldIndex != i)
							oclUnorderedList.Move(intOldIndex, i);
					}
					//as the transacted order list has been kept in sync with the master list
					// the transacted list is canonical (it contains all of the plugins,
					// and no plugins that hsouldn't be present), so
					// if a plugin is not in the transacted list it means the plugin was removed,
					// and should be removed form the master list
					for (Int32 i = oclUnorderedList.Count - 1; i >= lstOrderedList.Count; i--)
						oclUnorderedList.RemoveAt(i);
					EnlistedPluginOrderLog.OrderValidator.CorrectOrder(oclUnorderedList);
				}
				EnlistedPluginOrderLog.SavePluginLog();
				m_booEnlisted = false;
				m_oclOrderedPlugins.Clear();
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
				m_dicEnlistments.Remove(CurrentTransaction.TransactionInformation.LocalIdentifier);
				p_eltEnlistment.Done();
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
			/// master list of ordered plugins.
			/// </summary>
			/// <remarks>
			/// This applies any changes that are made to the maser list to the transacted list
			/// with which we are currently working.
			/// </remarks>
			/// <param name="sender">The object that raised the event.</param>
			/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
			private void MasterOrderedPlugins_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
			{
				switch (e.Action)
				{
					case NotifyCollectionChangedAction.Add:
						{
							Plugin plgPrevious = null;
							if (e.NewStartingIndex > 0)
								plgPrevious = EnlistedPluginOrderLog.m_oclOrderedPlugins[e.NewStartingIndex - 1];
							Int32 intStartIndex = m_oclOrderedPlugins.IndexOf(plgPrevious, PluginComparer.Filename) + 1;
							foreach (Plugin plgAdded in e.NewItems)
								m_oclOrderedPlugins.Insert(intStartIndex++, plgAdded);
						}
						break;
					case NotifyCollectionChangedAction.Remove:
						foreach (Plugin plgRemoved in e.OldItems)
							m_oclOrderedPlugins.Remove(plgRemoved, PluginComparer.Filename);
						break;
					case NotifyCollectionChangedAction.Replace:
						for (Int32 i = 0; i < e.OldItems.Count; i++)
						{
							m_oclOrderedPlugins.Remove((Plugin)e.OldItems[i], PluginComparer.Filename);

							Int32 intNewIndex = e.NewStartingIndex + i;
							Plugin plgPrevious = null;
							if (intNewIndex > 0)
								plgPrevious = EnlistedPluginOrderLog.m_oclOrderedPlugins[intNewIndex - 1];
							Int32 intStartIndex = m_oclOrderedPlugins.IndexOf(plgPrevious, PluginComparer.Filename) + 1;
							m_oclOrderedPlugins.Insert(intStartIndex, (Plugin)e.NewItems[i]);
						}
						break;
					case NotifyCollectionChangedAction.Reset:
						m_oclOrderedPlugins.Clear();
						break;
				}
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
				PluginComparer pcpComparer = PluginComparer.Filename;
				Dictionary<Plugin, Plugin> dicPredecessors = new Dictionary<Plugin, Plugin>();
				ThreadSafeObservableList<Plugin> oclUnorderedList = m_oclOrderedPlugins;
				IList<Plugin> lstOrderedList = p_lstOrderedPlugins;

				for (Int32 i = 0; i < oclUnorderedList.Count; i++)
					if (!lstOrderedList.Contains(oclUnorderedList[i], pcpComparer))
						dicPredecessors[oclUnorderedList[i]] = (i > 0) ? oclUnorderedList[i - 1] : null;

				//sort the items whose order has been specified
				for (Int32 i = 0; i < lstOrderedList.Count; i++)
				{
					Int32 intOldIndex = 0;
					for (intOldIndex = 0; intOldIndex < oclUnorderedList.Count; intOldIndex++)
						if (pcpComparer.Equals(oclUnorderedList[intOldIndex], lstOrderedList[i]))
							break;
					if (intOldIndex == oclUnorderedList.Count)
					{
						oclUnorderedList.Insert(i, lstOrderedList[i]);
						continue;
					}
					if (intOldIndex != i)
						oclUnorderedList.Move(intOldIndex, i);
				}
				//sort the items whose order has not been specified
				// if an item's order hasn't been specified, it is placed after the
				// item it followed in the original, unordered, list
				for (Int32 i = lstOrderedList.Count; i < oclUnorderedList.Count; i++)
				{
					Plugin plgPredecessor = dicPredecessors[oclUnorderedList[i]];
					Int32 intNewIndex = -1;
					//if the predecessor is null, then the item was at the beginning of the list
					if (plgPredecessor != null)
					{
						for (intNewIndex = 0; intNewIndex < oclUnorderedList.Count; intNewIndex++)
							if (pcpComparer.Equals(oclUnorderedList[intNewIndex], plgPredecessor))
								break;
					}
					if (intNewIndex + 1 != i)
						oclUnorderedList.Move(i, intNewIndex + 1);
				}
				EnlistedPluginOrderLog.OrderValidator.CorrectOrder(oclUnorderedList);

				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}

			/// <summary>
			/// Removes the given plugin from the order list.
			/// </summary>
			/// <param name="p_plgPlugin">The plugin to remove from the order list.</param>
			public void RemovePlugin(Plugin p_plgPlugin)
			{
				m_oclOrderedPlugins.Remove(p_plgPlugin, PluginComparer.Filename);
				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}

			#endregion
		}
	}
}
