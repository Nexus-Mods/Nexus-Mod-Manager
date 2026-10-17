using Nexus.Client.BackgroundTasks;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Reports the native ownership reload performed before an ordinary mod operation opens its archive.
	/// </summary>
	internal sealed class NativeModStatePreparationTask : BackgroundTask
	{
		/// <summary>Creates an indeterminate progress phase for target reservation and native-state validation.</summary>
		internal NativeModStatePreparationTask()
		{
			OverallMessage = LanguageManager.Get("Tasks.Mod.CheckingInstalledState", "Checking installed mod state...");
			ShowOverallProgressAsMarquee = true;
			ShowItemProgress = false;
		}

		/// <summary>Ends the preparation phase without changing the parent operation's durability or completion.</summary>
		internal void Complete(bool succeeded)
		{
			Status = succeeded ? TaskStatus.Complete : TaskStatus.Error;
			OnTaskEnded(succeeded);
		}
	}
}
