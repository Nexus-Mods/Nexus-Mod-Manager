namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes a requested absolute load-order index for a plugin.
	/// </summary>
	public sealed class SetPluginOrderIndexOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the logical path of the plugin to reorder.
		/// </summary>
		public string PluginPath { get; private set; }

		/// <summary>
		/// Gets the requested load-order index.
		/// </summary>
		public int NewIndex { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new plugin-order operation.
		/// </summary>
		/// <param name="p_strPluginPath">The logical path of the plugin to reorder.</param>
		/// <param name="p_intNewIndex">The requested load-order index.</param>
		public SetPluginOrderIndexOperation(string p_strPluginPath, int p_intNewIndex)
		{
			PluginPath = p_strPluginPath;
			NewIndex = p_intNewIndex;
		}

		#endregion
	}
}
