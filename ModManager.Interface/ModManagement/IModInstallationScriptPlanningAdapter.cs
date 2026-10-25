using System.Collections.Generic;
using System.Threading;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>Collects native scripted installer operations without deploying them during Collection preparation.</summary>
	public interface IModInstallationScriptPlanningAdapter
	{
		/// <summary>Runs the native installer UI and returns its confirmed operations for retained, reviewed replay.</summary>
		IReadOnlyList<ScriptedInstallOperation> CaptureOperations(IMod mod, IGameMode gameMode, IEnvironmentInfo environmentInfo,
			IVirtualModActivator activator, InstallerGroup installers, IModInstallationConditionEnvironment conditionEnvironment,
			SynchronizationContext uiContext, CancellationToken cancellationToken);
	}
}
