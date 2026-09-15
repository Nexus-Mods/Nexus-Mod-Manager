using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Tracks the plugin activation state requested during a single scripted installation.
	/// </summary>
	/// <remarks>
	/// Successfully deployed plugins implicitly request activation unless an explicit activation instruction overrides that default.
	/// Explicit instructions are retained independently of deployment order, with the latest instruction winning.
	/// </remarks>
	public sealed class ScriptedPluginActivationState
	{
		private readonly HashSet<string> m_hstDeployedPluginPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, bool> m_dicExplicitActivationRequests = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		#region Properties

		/// <summary>
		/// Gets a snapshot of the plugins successfully deployed by the current scripted installation.
		/// </summary>
		public IList<string> DeployedPluginPaths
		{
			get { return new List<string>(m_hstDeployedPluginPaths).AsReadOnly(); }
		}

		#endregion

		#region State Tracking

		/// <summary>
		/// Records a plugin successfully deployed by the current scripted installation.
		/// </summary>
		/// <param name="p_strPluginPath">The canonical path identifying the deployed plugin.</param>
		public void RecordDeployedPlugin(string p_strPluginPath)
		{
			ValidatePluginPath(p_strPluginPath);
			m_hstDeployedPluginPaths.Add(p_strPluginPath);
		}

		/// <summary>
		/// Records an explicit plugin activation instruction, replacing any earlier instruction for the same plugin.
		/// </summary>
		/// <param name="p_strPluginPath">The canonical path identifying the plugin.</param>
		/// <param name="p_booActivate">Whether the plugin should be active after successful scripted installation completion.</param>
		public void RecordActivationRequest(string p_strPluginPath, bool p_booActivate)
		{
			ValidatePluginPath(p_strPluginPath);
			m_dicExplicitActivationRequests[p_strPluginPath] = p_booActivate;
		}

		/// <summary>
		/// Gets the final requested activation state for the specified plugin when one is known.
		/// </summary>
		/// <param name="p_strPluginPath">The canonical path identifying the plugin.</param>
		/// <param name="p_booActivate">The requested activation state when the method returns <c>true</c>.</param>
		/// <returns><c>true</c> when the plugin was deployed or received an explicit activation instruction; otherwise, <c>false</c>.</returns>
		public bool TryGetRequestedActivation(string p_strPluginPath, out bool p_booActivate)
		{
			ValidatePluginPath(p_strPluginPath);

			if (m_dicExplicitActivationRequests.TryGetValue(p_strPluginPath, out p_booActivate))
				return true;

			if (m_hstDeployedPluginPaths.Contains(p_strPluginPath))
			{
				p_booActivate = true;
				return true;
			}

			p_booActivate = false;
			return false;
		}

		/// <summary>
		/// Gets a snapshot of all plugins whose final requested state is active.
		/// </summary>
		/// <returns>The case-insensitive effective activation set for the current scripted installation.</returns>
		public IList<string> GetRequestedActivePluginPaths()
		{
			HashSet<string> hstRequestedActive = new HashSet<string>(m_hstDeployedPluginPaths, StringComparer.OrdinalIgnoreCase);

			foreach (KeyValuePair<string, bool> kvpRequest in m_dicExplicitActivationRequests)
			{
				if (kvpRequest.Value)
					hstRequestedActive.Add(kvpRequest.Key);
				else
					hstRequestedActive.Remove(kvpRequest.Key);
			}

			return new List<string>(hstRequestedActive).AsReadOnly();
		}

		/// <summary>
		/// Reconciles the complete scripted activation intent after all deployment work has succeeded.
		/// </summary>
		/// <param name="p_pmgPluginManager">The plugin manager used to register and validate the final state.</param>
		public void Reconcile(IPluginManager p_pmgPluginManager)
		{
			if (p_pmgPluginManager == null)
				return;

			IList<string> lstDeployedPluginPaths = DeployedPluginPaths;
			if (lstDeployedPluginPaths.Count > 0)
				p_pmgPluginManager.IntegrateDeployedPlugins(lstDeployedPluginPaths);

			HashSet<string> hstTrackedPluginPaths = new HashSet<string>(m_hstDeployedPluginPaths, StringComparer.OrdinalIgnoreCase);
			hstTrackedPluginPaths.UnionWith(m_dicExplicitActivationRequests.Keys);

			List<Plugin> lstTrackedPlugins = hstTrackedPluginPaths
				.Select(p_pmgPluginManager.GetRegisteredPlugin)
				.Where(x => x != null)
				.GroupBy(x => x.Filename, StringComparer.OrdinalIgnoreCase)
				.Select(x => x.First())
				.ToList();

			if (lstTrackedPlugins.Count > 0)
			{
				IList<PluginValidationDiagnostic> lstBlockingDiagnostics;
				p_pmgPluginManager.TrySetPluginActivation(lstTrackedPlugins, false, out lstBlockingDiagnostics);
			}

			List<string> lstRequestedActivePluginPaths = GetRequestedActivePluginPaths()
				.Where(x => m_hstDeployedPluginPaths.Contains(x) || p_pmgPluginManager.GetRegisteredPlugin(x) != null)
				.ToList();
			if (lstRequestedActivePluginPaths.Count > 0)
				p_pmgPluginManager.IntegrateDeployedPlugins(lstRequestedActivePluginPaths);

			m_hstDeployedPluginPaths.Clear();
			m_dicExplicitActivationRequests.Clear();
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Validates a plugin path before it is used as a state key.
		/// </summary>
		/// <param name="p_strPluginPath">The plugin path to validate.</param>
		private static void ValidatePluginPath(string p_strPluginPath)
		{
			if (String.IsNullOrWhiteSpace(p_strPluginPath))
				throw new ArgumentException("Plugin path cannot be null or empty.", nameof(p_strPluginPath));
		}

		#endregion
	}
}
