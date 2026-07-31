using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
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
			/// Commits the changes to the <see cref="PluginOrderLog"/> using a single batched collection update.
			/// </summary>
			public void Commit()
			{
				EnlistedPluginOrderLog.m_oclOrderedPlugins.CollectionChanged -= MasterOrderedPlugins_CollectionChanged;

				List<Plugin> desiredOrder = new List<Plugin>(m_oclOrderedPlugins);
				ReplaceOrderedPlugins(EnlistedPluginOrderLog.m_oclOrderedPlugins, desiredOrder);

				EnlistedPluginOrderLog.SavePluginLog();

				m_booEnlisted = false;
				m_oclOrderedPlugins.Clear();
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
				EnlistedPluginOrderLog.m_oclOrderedPlugins.CollectionChanged -= MasterOrderedPlugins_CollectionChanged;

				m_booEnlisted = false;
				m_oclOrderedPlugins.Clear();
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
			/// Builds the complete plugin order while preserving the relative placement of plugins omitted from the requested order.
			/// </summary>
			/// <param name="p_lstCurrentOrder">The current complete plugin order.</param>
			/// <param name="p_lstRequestedOrder">The requested partial or complete plugin order.</param>
			/// <returns>The complete resulting plugin order.</returns>
			private static List<Plugin> BuildCompletePluginOrder(IList<Plugin> p_lstCurrentOrder, IList<Plugin> p_lstRequestedOrder)
			{
				PluginComparer comparer = PluginComparer.Filename;
				List<Plugin> requestedOrder = new List<Plugin>();
				HashSet<Plugin> requestedPlugins = new HashSet<Plugin>(comparer);

				foreach (Plugin plugin in p_lstRequestedOrder)
				{
					if (plugin != null && requestedPlugins.Add(plugin))
						requestedOrder.Add(plugin);
				}

				List<Plugin> leadingPlugins = new List<Plugin>();
				Dictionary<Plugin, List<Plugin>> followersByPlugin = new Dictionary<Plugin, List<Plugin>>(comparer);
				Plugin precedingRequestedPlugin = null;

				foreach (Plugin plugin in p_lstCurrentOrder)
				{
					if (plugin == null)
						continue;

					if (requestedPlugins.Contains(plugin))
					{
						precedingRequestedPlugin = plugin;
						continue;
					}

					if (precedingRequestedPlugin == null)
					{
						leadingPlugins.Add(plugin);
						continue;
					}

					List<Plugin> followers;

					if (!followersByPlugin.TryGetValue(precedingRequestedPlugin, out followers))
					{
						followers = new List<Plugin>();
						followersByPlugin.Add(precedingRequestedPlugin, followers);
					}

					followers.Add(plugin);
				}

				List<Plugin> completeOrder = new List<Plugin>(p_lstCurrentOrder.Count + requestedOrder.Count);
				completeOrder.AddRange(leadingPlugins);

				foreach (Plugin plugin in requestedOrder)
				{
					completeOrder.Add(plugin);

					List<Plugin> followers;

					if (followersByPlugin.TryGetValue(plugin, out followers))
						completeOrder.AddRange(followers);
				}

				return completeOrder;
			}

			/// <summary>
			/// Replaces the contents of an observable plugin list using one batched reset notification.
			/// </summary>
			/// <param name="p_oclTarget">The observable list to update.</param>
			/// <param name="p_lstDesiredOrder">The desired complete plugin order.</param>
			private static void ReplaceOrderedPlugins(ThreadSafeObservableList<Plugin> p_oclTarget, IList<Plugin> p_lstDesiredOrder)
			{
				if (p_oclTarget == null)
					throw new ArgumentNullException("p_oclTarget");

				if (p_lstDesiredOrder == null)
					throw new ArgumentNullException("p_lstDesiredOrder");

				if (PluginOrdersEqual(p_oclTarget, p_lstDesiredOrder))
					return;

				using (p_oclTarget.BeginUpdate())
				{
					p_oclTarget.Clear();
					p_oclTarget.EnsureCapacity(p_lstDesiredOrder.Count);

					foreach (Plugin plugin in p_lstDesiredOrder)
						p_oclTarget.Add(plugin);
				}
			}

			/// <summary>
			/// Determines whether two plugin sequences contain the same registered plugin instances in the same filename-based order.
			/// </summary>
			/// <param name="p_lstFirst">The first plugin sequence.</param>
			/// <param name="p_lstSecond">The second plugin sequence.</param>
			/// <returns><c>true</c> if both sequences contain the same plugin instances and order; otherwise, <c>false</c>.</returns>
			private static bool PluginOrdersEqual(IList<Plugin> p_lstFirst, IList<Plugin> p_lstSecond)
			{
				if (ReferenceEquals(p_lstFirst, p_lstSecond))
					return true;

				if (p_lstFirst == null || p_lstSecond == null || p_lstFirst.Count != p_lstSecond.Count)
					return false;

				PluginComparer comparer = PluginComparer.Filename;

				for (int index = 0; index < p_lstFirst.Count; index++)
				{
					if (!comparer.Equals(p_lstFirst[index], p_lstSecond[index]) ||
						!ReferenceEquals(p_lstFirst[index], p_lstSecond[index]))
					{
						return false;
					}
				}

				return true;
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
						using (m_oclOrderedPlugins.BeginUpdate())
						{
							m_oclOrderedPlugins.Clear();
							m_oclOrderedPlugins.EnsureCapacity(EnlistedPluginOrderLog.m_oclOrderedPlugins.Count);

							foreach (Plugin plugin in EnlistedPluginOrderLog.m_oclOrderedPlugins)
								m_oclOrderedPlugins.Add(plugin);
						}
						break;
				}
			}

			#endregion

			#region Plugin Order Management

			/// <summary>
			/// Sets the order of the plugins to the given order using a linear reconstruction of the final list.
			/// </summary>
			/// <remarks>
			/// Plugins omitted from the requested order retain their relationship with the closest preceding requested plugin from the original order.
			/// </remarks>
			/// <param name="p_lstOrderedPlugins">The list indicating the desired order of the plugins.</param>
			public void SetPluginOrder(IList<Plugin> p_lstOrderedPlugins)
			{
				if (p_lstOrderedPlugins == null)
					throw new ArgumentNullException("p_lstOrderedPlugins");

				List<Plugin> currentOrder = new List<Plugin>(m_oclOrderedPlugins);
				List<Plugin> completeOrder = BuildCompletePluginOrder(currentOrder, p_lstOrderedPlugins);

				if (PluginOrdersEqual(currentOrder, completeOrder))
					return;

				ReplaceOrderedPlugins(m_oclOrderedPlugins, completeOrder);

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
				RemovePlugins(p_plgPlugin == null ? new List<Plugin>() : new List<Plugin> { p_plgPlugin });
			}

			/// <summary>
			/// Removes multiple plugins from the transaction-local order in one linear reconstruction.
			/// </summary>
			/// <param name="p_lstPlugins">The plugins to remove.</param>
			public void RemovePlugins(IList<Plugin> p_lstPlugins)
			{
				HashSet<Plugin> hstPluginsToRemove = new HashSet<Plugin>(p_lstPlugins ?? new List<Plugin>(), PluginComparer.Filename);

				if (hstPluginsToRemove.Count == 0)
					return;

				List<Plugin> lstRemainingPlugins = m_oclOrderedPlugins
					.Where(x => x != null && !hstPluginsToRemove.Contains(x))
					.ToList();

				if (lstRemainingPlugins.Count == m_oclOrderedPlugins.Count)
					return;

				ReplaceOrderedPlugins(m_oclOrderedPlugins, lstRemainingPlugins);

				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}

			#endregion
		}
	}
}
