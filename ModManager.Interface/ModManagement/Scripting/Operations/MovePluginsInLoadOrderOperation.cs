using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes the legacy scripted request to move selected plugin indices to a load-order position.
	/// </summary>
	public sealed class MovePluginsInLoadOrderOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the plugin indices selected by the scripted installer.
		/// </summary>
		public IReadOnlyList<int> PluginIndices { get; private set; }

		/// <summary>
		/// Gets the requested insertion position in the load order.
		/// </summary>
		public int Position { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new load-order move operation.
		/// </summary>
		/// <param name="p_intPluginIndices">The plugin indices to move.</param>
		/// <param name="p_intPosition">The requested insertion position.</param>
		public MovePluginsInLoadOrderOperation(int[] p_intPluginIndices, int p_intPosition)
		{
			PluginIndices = p_intPluginIndices == null
				? null
				: new ReadOnlyCollection<int>(new List<int>(p_intPluginIndices));
			Position = p_intPosition;
		}

		#endregion
	}
}
