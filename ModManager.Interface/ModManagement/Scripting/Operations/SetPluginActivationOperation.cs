namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes a requested change to a plugin's active state.
	/// </summary>
	public sealed class SetPluginActivationOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the logical path of the plugin whose state should be changed.
		/// </summary>
		public string PluginPath { get; private set; }

		/// <summary>
		/// Gets whether the plugin should be active after the operation is applied.
		/// </summary>
		public bool Activate { get; private set; }

		/// <summary>
		/// Gets whether native plugin activatability must be checked after the corresponding file operation before applying this request.
		/// </summary>
		public bool RequireActivatablePlugin { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new plugin-activation operation.
		/// </summary>
		/// <param name="p_strPluginPath">The logical path of the plugin to update.</param>
		/// <param name="p_booActivate">Whether the plugin should be activated.</param>
		public SetPluginActivationOperation(string p_strPluginPath, bool p_booActivate)
			: this(p_strPluginPath, p_booActivate, false)
		{
		}

		/// <summary>
		/// Initializes a new plugin-activation operation with an optional execution-time activatability guard.
		/// </summary>
		/// <param name="p_strPluginPath">The logical path of the plugin to update.</param>
		/// <param name="p_booActivate">Whether the plugin should be activated.</param>
		/// <param name="p_booRequireActivatablePlugin">Whether the native executor must verify activatability immediately before applying the request.</param>
		public SetPluginActivationOperation(string p_strPluginPath, bool p_booActivate, bool p_booRequireActivatablePlugin)
		{
			PluginPath = p_strPluginPath;
			Activate = p_booActivate;
			RequireActivatablePlugin = p_booRequireActivatablePlugin;
		}

		#endregion
	}
}
