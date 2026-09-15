namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Exposes successful-completion handling for scripted installation executors that retain installation-scoped state.
	/// </summary>
	public interface IScriptedInstallOperationCompletionExecutor
	{
		/// <summary>
		/// Completes retained scripted installation work after every operation has succeeded.
		/// </summary>
		void CompleteExecution();
	}
}
