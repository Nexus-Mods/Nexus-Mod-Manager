using System;
using System.Collections.Generic;
using Nexus.Client.Plugins;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// Describes the policy-corrected result of evaluating a requested plugin order and activation state without applying it.
	/// </summary>
	public sealed class PluginStateResolution
	{
		/// <summary>
		/// Gets whether the resolved state can be applied without introducing new blocking validation errors.
		/// </summary>
		public bool IsAllowed { get; private set; }

		/// <summary>
		/// Gets the plugin order after applying the current game policy.
		/// </summary>
		public IList<Plugin> OrderedPlugins { get; private set; }

		/// <summary>
		/// Gets the active plugin set after applying the current game policy.
		/// </summary>
		public IList<Plugin> ActivePlugins { get; private set; }

		/// <summary>
		/// Initializes a resolved plugin-state result.
		/// </summary>
		/// <param name="p_booIsAllowed">Whether the resolved state is allowed by the current policy.</param>
		/// <param name="p_lstOrderedPlugins">The policy-corrected plugin order.</param>
		/// <param name="p_lstActivePlugins">The policy-corrected active plugin set.</param>
		public PluginStateResolution(bool p_booIsAllowed, IList<Plugin> p_lstOrderedPlugins, IList<Plugin> p_lstActivePlugins)
		{
			IsAllowed = p_booIsAllowed;
			OrderedPlugins = new List<Plugin>(p_lstOrderedPlugins ?? new List<Plugin>()).AsReadOnly();
			ActivePlugins = new List<Plugin>(p_lstActivePlugins ?? new List<Plugin>()).AsReadOnly();
		}
	}
}
