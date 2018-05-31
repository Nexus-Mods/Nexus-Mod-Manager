﻿using System;
using System.Collections.Generic;
using Nexus.Client.Games.Gamebryo.Plugins;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
using System.IO;

namespace Nexus.Client.Games.Gamebryo.PluginManagement
{
	/// <summary>
	/// Provides methods for validating and correcting the
	/// order of Gamebryo based game plugins.
	/// </summary>
	public class GamebryoPluginOrderValidator : IPluginOrderValidator
	{
		#region Properties

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		protected string[] OrderedCriticalPluginNames { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strOrderedCriticalPluginNames">The list of critical plugin names, ordered by load order.</param>
		public GamebryoPluginOrderValidator(string[] p_strOrderedCriticalPluginNames)
		{
			OrderedCriticalPluginNames = p_strOrderedCriticalPluginNames;
		}

		#endregion

		#region IPluginOrderValidator Members

		/// <summary>
		/// Determines if the specified plugin order is valid.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose order is to be validated.</param>
		/// <returns><c>true</c> if the given plugins are in a valid order;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateOrder(IList<Plugin> p_lstPlugins)
		{
			if (p_lstPlugins.Count < OrderedCriticalPluginNames.Length)
				return false;
			for (Int32 i = 0; i < OrderedCriticalPluginNames.Length; i++)
				if (!p_lstPlugins[i].Filename.Equals(OrderedCriticalPluginNames[i], StringComparison.OrdinalIgnoreCase))
					return false;
			bool booIsPreviousMaster = true;
			bool booIsPreviousLightMaster = false;
			foreach (GamebryoPlugin plgPlugin in p_lstPlugins)
			{
				if (!booIsPreviousMaster && plgPlugin.IsMaster)
					return false;
				// simple test esl come after esm or esl
				if (!(booIsPreviousMaster || booIsPreviousLightMaster) && plgPlugin.IsLightMaster)
					return false;
				// no true esm after an esl
				if (booIsPreviousLightMaster && plgPlugin.IsMaster && !plgPlugin.IsLightMaster)
					return false;
				booIsPreviousMaster = plgPlugin.IsMaster;
				booIsPreviousLightMaster = plgPlugin.IsLightMaster;
			}
			for (Int32 i = p_lstPlugins.Count - 1; i >= 0; i--)
				if (p_lstPlugins[i].HasMasters)
					foreach (string pluginName in p_lstPlugins[i].Masters)
					{
						Int32 masterIndex = p_lstPlugins.IndexOf(p => Path.GetFileName(p.Filename).Equals(pluginName, StringComparison.OrdinalIgnoreCase));
						if (i < masterIndex)
							return false;
					}

			return true;
		}

		/// <summary>
		/// Reoreders the given plugins so that their order is valid.
		/// </summary>
		/// <remarks>
		/// This method moves the minimum number of plugins the minimum distance
		/// in order to make the order valid.
		/// </remarks>
		/// <param name="p_lstPlugins">The plugins whose order is to be corrected.</param>
		public void CorrectOrder(IList<Plugin> p_lstPlugins)
		{
			bool booHasMove = p_lstPlugins is ThreadSafeObservableList<Plugin>;

			//make sure critical plugins are in order, at the top
			for (Int32 i = 0; i < OrderedCriticalPluginNames.Length && i < p_lstPlugins.Count; i++)
				if (!p_lstPlugins[i].Filename.Equals(OrderedCriticalPluginNames[i], StringComparison.OrdinalIgnoreCase))
				{
					//a critical plugin is not in the correct position
					Int32 intCriticalIndex = p_lstPlugins.IndexOf(p => p.Filename.Equals(OrderedCriticalPluginNames[i], StringComparison.OrdinalIgnoreCase));
					if (intCriticalIndex > -1)
					{
						if (booHasMove)
							((ThreadSafeObservableList<Plugin>)p_lstPlugins).Move(intCriticalIndex, i);
						else
						{
							Plugin plgPlugin = p_lstPlugins[intCriticalIndex];
							p_lstPlugins.RemoveAt(intCriticalIndex);
							p_lstPlugins.Insert(i, plgPlugin);
						}
					}
				}

			bool booFoundMasters = false;
			Int32 intFirstNonMasterIndex = p_lstPlugins.Count - 1;
			for (Int32 i = p_lstPlugins.Count - 1; i >= 0; i--)
			{
				if (!booFoundMasters)
					booFoundMasters = ((GamebryoPlugin)p_lstPlugins[i]).IsMaster;
				if (!booFoundMasters)
					intFirstNonMasterIndex = i - 1;
				else
				{
					if (!((GamebryoPlugin)p_lstPlugins[i]).IsMaster)
					{
						if (booHasMove)
							((ThreadSafeObservableList<Plugin>)p_lstPlugins).Move(i, intFirstNonMasterIndex);
						else
						{
							Plugin plgPlugin = p_lstPlugins[i];
							p_lstPlugins.RemoveAt(i);
							if (intFirstNonMasterIndex >= p_lstPlugins.Count)
								p_lstPlugins.Add(plgPlugin);
							else
								p_lstPlugins.Insert(intFirstNonMasterIndex, plgPlugin);
						}
						intFirstNonMasterIndex--;
					}
				}
			}

			bool booFoundLightMasters = false;
			intFirstNonMasterIndex = p_lstPlugins.Count - 1;
			for (Int32 i = p_lstPlugins.Count - 1; i >= 0; i--)
			{
				if (!booFoundLightMasters)
					booFoundLightMasters = ((GamebryoPlugin)p_lstPlugins[i]).IsLightMaster;
				if (!booFoundLightMasters)
					intFirstNonMasterIndex = i - 1;
				else
				{
					if ((!((GamebryoPlugin)p_lstPlugins[i]).IsLightMaster) && (!((GamebryoPlugin)p_lstPlugins[i]).IsMaster))
					{
						if (booHasMove)
							((ThreadSafeObservableList<Plugin>)p_lstPlugins).Move(i, intFirstNonMasterIndex);
						else
						{
							Plugin plgPlugin = p_lstPlugins[i];
							p_lstPlugins.RemoveAt(i);
							if (intFirstNonMasterIndex >= p_lstPlugins.Count)
								p_lstPlugins.Add(plgPlugin);
							else
								p_lstPlugins.Insert(intFirstNonMasterIndex, plgPlugin);
						}
						intFirstNonMasterIndex--;
					}
				}
			}

			// Makes sure no plugin is loaded before his master.
			for (Int32 i = p_lstPlugins.Count - 1; i >= 0; i--)
			{
				if (p_lstPlugins[i].HasMasters)
				{
					Int32 highestMasterIndex = 0;
					foreach (string pluginName in p_lstPlugins[i].Masters)
					{
						Int32 masterIndex = p_lstPlugins.IndexOf(p => Path.GetFileName(p.Filename).Equals(pluginName, StringComparison.OrdinalIgnoreCase));
						highestMasterIndex = highestMasterIndex > masterIndex ? highestMasterIndex : masterIndex;
					}

					if (i < highestMasterIndex)
					{
						if (booHasMove)
							((ThreadSafeObservableList<Plugin>)p_lstPlugins).Move(i, highestMasterIndex);
						else
						{
							Plugin plgPlugin = p_lstPlugins[i];
							p_lstPlugins.RemoveAt(i);
							if (highestMasterIndex >= p_lstPlugins.Count)
								p_lstPlugins.Add(plgPlugin);
							else
								p_lstPlugins.Insert(highestMasterIndex, plgPlugin);
						}
					}
				}
			}
		}

		#endregion
	}
}
