namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Requests registration of the current mod in native installed state without applying file or configuration effects.
	/// </summary>
	/// <remarks>
	/// This operation exists only for Local Collection restore/recovery. The owning native installer still performs ordinary
	/// InstallLog registration inside its transaction; later restore phases reconstruct exact captured owners and effects through
	/// their native services instead of rerunning environment-dependent installer behavior.
	/// </remarks>
	public sealed class RestoreNativeRegistrationOperation : ScriptedInstallOperation
	{
	}
}
