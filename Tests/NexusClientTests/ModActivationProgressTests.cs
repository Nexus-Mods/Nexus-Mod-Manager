using System;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModActivationMonitoring.UI;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>Prevents file-by-file queue notifications from becoming synchronous UI refreshes with identical text.</summary>
    [TestFixture]
    public class ModActivationProgressTests
    {
        /// <summary>Thousands of file and message notifications require only the displayed percentage transitions.</summary>
        [Test]
        public void FileProgress_OnlyVisiblePercentageChangesNeedDispatch()
        {
            int completed = 0;
            IBackgroundTask task = InterfaceStub<IBackgroundTask>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_OverallProgress": return (long)completed;
                    case "get_OverallProgressMaximum": return 5000L;
                    default: return null;
                }
            });
            string displayed = String.Empty;
            int updates = 0;
            for (completed = 0; completed <= 5000; completed++)
            {
                Assert.That(ModActivationMonitorRow.GetTaskProgressUpdate(task, nameof(IBackgroundTask.ItemMessage), displayed), Is.Null);
                string progress = ModActivationMonitorRow.GetTaskProgressUpdate(task, nameof(IBackgroundTask.OverallProgress), displayed);
                if (progress != null)
                {
                    displayed = progress;
                    updates++;
                }
            }
            Assert.That(updates, Is.EqualTo(51));
            Assert.That(displayed, Does.Contain("100%"));
        }
    }
}
