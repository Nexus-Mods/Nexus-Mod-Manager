using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes the legacy scripted request to set the load order from plugin indices.
	/// </summary>
	public sealed class SetLoadOrderOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the plugin indices supplied by the scripted installer.
		/// </summary>
		public IReadOnlyList<int> PluginIndices { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new legacy load-order operation.
		/// </summary>
		/// <param name="p_intPluginIndices">The plugin indices supplied by the scripted installer.</param>
		public SetLoadOrderOperation(int[] p_intPluginIndices)
		{
			PluginIndices = p_intPluginIndices == null
				? null
				: new ReadOnlyCollection<int>(new List<int>(p_intPluginIndices));
		}

		#endregion
	}
}
