namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Executes logical operations produced by a scripted installer.
	/// </summary>
	public interface IScriptedInstallOperationExecutor
	{
		/// <summary>
		/// Executes the specified scripted installation operation.
		/// </summary>
		/// <param name="p_sioOperation">The logical installation operation to execute.</param>
		/// <returns><c>true</c> when the operation completed successfully; otherwise, <c>false</c>.</returns>
		bool Execute(ScriptedInstallOperation p_sioOperation);
	}
}
