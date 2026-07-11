using System;
using System.Threading;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.ModManagement
{
	internal static class TaskSetWaiter
	{
		public static void Wait(IBackgroundTaskSet taskSet)
		{
			if (taskSet.IsCompleted)
				return;

			EventWaitHandle completed = new EventWaitHandle(false, EventResetMode.ManualReset);
			EventHandler<TaskSetCompletedEventArgs> handler = (sender, e) => completed.Set();
			taskSet.TaskSetCompleted += handler;
			try
			{
				if (!taskSet.IsCompleted)
					completed.WaitOne();
			}
			finally
			{
				taskSet.TaskSetCompleted -= handler;
			}
		}
	}
}