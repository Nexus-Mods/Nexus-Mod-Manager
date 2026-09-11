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

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new plugin-activation operation.
		/// </summary>
		/// <param name="p_strPluginPath">The logical path of the plugin to update.</param>
		/// <param name="p_booActivate">Whether the plugin should be activated.</param>
		public SetPluginActivationOperation(string p_strPluginPath, bool p_booActivate)
		{
			PluginPath = p_strPluginPath;
			Activate = p_booActivate;
		}

		#endregion
	}
}
