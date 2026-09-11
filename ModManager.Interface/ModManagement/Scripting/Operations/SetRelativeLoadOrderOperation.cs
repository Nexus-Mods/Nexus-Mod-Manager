using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes a request to preserve the relative order of a specified plugin sequence.
	/// </summary>
	public sealed class SetRelativeLoadOrderOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the logical plugin paths in the requested relative order.
		/// </summary>
		public IReadOnlyList<string> PluginPaths { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new relative load-order operation.
		/// </summary>
		/// <param name="p_strPluginPaths">The logical plugin paths in the requested relative order.</param>
		public SetRelativeLoadOrderOperation(string[] p_strPluginPaths)
		{
			PluginPaths = p_strPluginPaths == null
				? null
				: new ReadOnlyCollection<string>(new List<string>(p_strPluginPaths));
		}

		#endregion
	}
}
