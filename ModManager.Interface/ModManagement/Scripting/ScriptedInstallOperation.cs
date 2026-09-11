namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Represents a logical installation operation requested by a scripted installer.
	/// </summary>
	/// <remarks>
	/// Operations describe installation intent only. Physical staging, virtual deployment,
	/// hard-link selection, and other deployment details are resolved by the installation engine.
	/// </remarks>
	public abstract class ScriptedInstallOperation
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ScriptedInstallOperation"/> class.
		/// </summary>
		protected ScriptedInstallOperation()
		{
		}
	}
}
