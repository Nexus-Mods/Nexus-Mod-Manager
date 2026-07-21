using System.Collections.Generic;
using Nexus.Client.Plugins;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// Describes the properties and methods of a plugin order validator.
	/// </summary>
	/// <remarks>
	/// A plugin order validator provides methods for validating and correcting the
	/// order of plugins.
	/// </remarks>
	public interface IPluginOrderValidator
	{
		/// <summary>
		/// Determines if the specified plugin order is valid.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose order is to be validated.</param>
		/// <returns><c>true</c> if the given plugins are in a valid order;
		/// <c>false</c> otherwise.</returns>
		bool ValidateOrder(IList<Plugin> p_lstPlugins);

		/// <summary>
		/// Reoreders the given plugins so that their order is valid.
		/// </summary>
		/// <remarks>
		/// This method moves the minimum number of plugins the minimum distance
		/// in order to make the order valid.
		/// </remarks>
		/// <param name="p_lstPlugins">The plugins whose order is to be corrected.</param>
		void CorrectOrder(IList<Plugin> p_lstPlugins);
	}
}
