using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Nexus.Client.Games.Gamebryo.Plugins;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.Games.Gamebryo.PluginManagement
{
	/// <summary>
	/// Provides methods for validating and correcting the
	/// order of Gamebryo based game plugins.
	/// </summary>
	public class GamebryoPluginOrderValidator : IPluginOrderValidator
	{
		#region IPluginOrderValidator Members

		/// <summary>
		/// Determines if the specified plugin order is valid.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose order is to be validated.</param>
		/// <returns><c>true</c> if the given plugins are in a valid order;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateOrder(IList<Plugin> p_lstPlugins)
		{
			bool booIsPreviousMaster = true;
			foreach (GamebryoPlugin plgPlugin in p_lstPlugins)
			{
				if (!booIsPreviousMaster && plgPlugin.IsMaster)
					return false;
				booIsPreviousMaster = plgPlugin.IsMaster;
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
			bool booHasMove = p_lstPlugins is ObservableCollection<Plugin>;
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
							((ObservableCollection<Plugin>)p_lstPlugins).Move(i, intFirstNonMasterIndex);
						else
						{
							Plugin plgPlugin = p_lstPlugins[i];
							p_lstPlugins.RemoveAt(i);
							p_lstPlugins.Insert(intFirstNonMasterIndex, plgPlugin);
						}
						intFirstNonMasterIndex--;
					}
				}
			}
		}

		#endregion
	}
}
