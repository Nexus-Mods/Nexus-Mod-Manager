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
	/// Deployments are tracked independently from activation intent so scripted paths that historically suppress plugin handling remain unchanged.
	/// Normal plugin deployments implicitly request activation, while explicit instructions override that intent with the latest instruction winning.
	/// </remarks>
	public sealed class ScriptedPluginActivationState
	{
		private readonly HashSet<string> m_hstDeployedPluginPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> m_hstImplicitActivationRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
		/// Records a plugin successfully deployed by the current scripted installation using normal plugin activation semantics.
		/// </summary>
		/// <param name="p_strPluginPath">The canonical path identifying the deployed plugin.</param>
		public void RecordDeployedPlugin(string p_strPluginPath)
		{
			RecordDeployedPlugin(p_strPluginPath, true);
		}

		/// <summary>
		/// Records a plugin successfully deployed by the current scripted installation and whether deployment implicitly requests activation.
		/// </summary>
		/// <param name="p_strPluginPath">The canonical path identifying the deployed plugin.</param>
		/// <param name="p_booRequestActivation">Whether the deployment itself requests plugin activation.</param>
		public void RecordDeployedPlugin(string p_strPluginPath, bool p_booRequestActivation)
		{
			ValidatePluginPath(p_strPluginPath);
			m_hstDeployedPluginPaths.Add(p_strPluginPath);
			if (p_booRequestActivation)
				m_hstImplicitActivationRequests.Add(p_strPluginPath);
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
		/// <returns><c>true</c> when deployment or an explicit instruction requested an activation state; otherwise, <c>false</c>.</returns>
		public bool TryGetRequestedActivation(string p_strPluginPath, out bool p_booActivate)
		{
			ValidatePluginPath(p_strPluginPath);

			if (m_dicExplicitActivationRequests.TryGetValue(p_strPluginPath, out p_booActivate))
				return true;

			if (m_hstImplicitActivationRequests.Contains(p_strPluginPath))
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
			HashSet<string> hstRequestedActive = new HashSet<string>(m_hstImplicitActivationRequests, StringComparer.OrdinalIgnoreCase);

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
		/// Gets a snapshot of the final requested activation state for every plugin controlled by the current scripted installation.
		/// </summary>
		/// <returns>The case-insensitive final activation state keyed by plugin path.</returns>
		public IDictionary<string, bool> GetRequestedActivationStates()
		{
			Dictionary<string, bool> dicRequestedStates = m_hstImplicitActivationRequests
				.ToDictionary(x => x, x => true, StringComparer.OrdinalIgnoreCase);

			foreach (KeyValuePair<string, bool> kvpRequest in m_dicExplicitActivationRequests)
				dicRequestedStates[kvpRequest.Key] = kvpRequest.Value;

			return dicRequestedStates;
		}

		/// <summary>
		/// Reconciles the complete scripted activation intent after all deployment work has succeeded.
		/// </summary>
		/// <param name="p_pmgPluginManager">The plugin manager used to register and validate the final state.</param>
		public void Reconcile(IPluginManager p_pmgPluginManager)
		{
			if (p_pmgPluginManager == null)
				return;

			IList<PluginValidationDiagnostic> lstBlockingDiagnostics;
			bool booReconciled = p_pmgPluginManager.TryReconcileDeployedPlugins(
				DeployedPluginPaths, GetRequestedActivationStates(), out lstBlockingDiagnostics);

			if (!booReconciled)
			{
				System.Diagnostics.Trace.TraceWarning(
					"One or more scripted plugin activation requests could not be applied during final reconciliation.");
			}

			m_hstDeployedPluginPaths.Clear();
			m_hstImplicitActivationRequests.Clear();
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
