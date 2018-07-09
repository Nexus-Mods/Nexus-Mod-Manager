using System;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.Sorter
{
	/// <summary>
	/// The interface for SORTER functionality.
	/// </summary>
	/// <remarks>
	/// This use BAPI to expose SORTER's pluing sorting and activation abilities.
	/// </remarks>
	public interface IPluginSorter
	{
		#region Properties

		/// <summary>
		/// Gets the path to the masterlist.
		/// </summary>
		/// <value>The path to the masterlist.</value>
		string MasterlistPath { get; }

		/// <summary>
		/// Gets the path to the userlist.
		/// </summary>
		/// <value>The path to the userlist.</value>
		string UserlistPath { get; }

		/// <summary>
		/// Gets whether the sorting library successfully initialized.
		/// </summary>
		/// <value>Whether the sorting library successfully initialized.</value>
		bool Initialized { get; }

		#endregion

		#region Masterlist Updating

		

		#endregion

		#region Plugin Sorting Functions
		
		#endregion

		#region Utility Methods

		#endregion
	}
}
