using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModActivationMonitoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class Witcher3NativeLifecycleRefreshTests
	{
		[Test]
		public void ObserverOnlyInvalidates_NeverMutatesTheNativeOperationQueue()
		{
			var monitor = new ModActivationMonitor();
			int notifications = 0;
			var observer = new Witcher3NativeLifecycleObserver(monitor, null);
			observer.Changed += (sender, args) => notifications++;
			Assert.That(observer.Revision, Is.Zero);
			observer.NotifyExternalChange();
			observer.NotifyExternalChange();
			Assert.That(observer.Revision, Is.EqualTo(2));
			Assert.That(notifications, Is.EqualTo(2));
			Assert.That(monitor.Tasks.Count, Is.Zero);
			observer.Dispose();
			observer.NotifyExternalChange();
			Assert.That(observer.Revision, Is.EqualTo(2));
			Assert.That(notifications, Is.EqualTo(2));
		}

		[Test]
		public void ConcurrentSignalsAdvanceAnAtomicGeneration_WithoutScanningAnything()
		{
			var observer = new Witcher3NativeLifecycleObserver(new ModActivationMonitor(), null);
			try
			{
				int received = 0;
				observer.Changed += (sender, args) => Interlocked.Increment(ref received);
				Parallel.For(0, 800, _ => observer.NotifyExternalChange());
				Assert.That(observer.Revision, Is.EqualTo(800));
				Assert.That(received, Is.EqualTo(800));
			}
			finally { observer.Dispose(); }
		}

		[Test]
		public void ThrowingSubscriberDoesNotPreventInvalidationOrEscapeIntoANativeCallback()
		{
			var observer = new Witcher3NativeLifecycleObserver(new ModActivationMonitor(), null);
			try
			{
				observer.Changed += (sender, args) => { throw new InvalidOperationException("UI closing"); };
				Assert.DoesNotThrow(() => observer.NotifyExternalChange());
				Assert.That(observer.Revision, Is.EqualTo(1));
			}
			finally { observer.Dispose(); }
		}

		[TestCase(3, 3, false, true)]
		[TestCase(3, 4, false, false)]
		[TestCase(3, 3, true, false)]
		public void ApplyApprovalRequiresSameNativeGenerationAndIdleQueue(
			int readRevision, int currentRevision, bool nativeTaskRunning, bool expected)
		{
			Assert.That(Witcher3NativeRefreshPolicy.CanApprove(readRevision, currentRevision,
				nativeTaskRunning), Is.EqualTo(expected));
		}

		[TestCase(true, false, false, false, true, true)]
		[TestCase(false, false, false, false, true, false)]
		[TestCase(true, true, false, false, true, false)]
		[TestCase(true, false, true, false, true, false)]
		[TestCase(true, false, false, true, true, false)]
		[TestCase(true, false, false, false, false, false)]
		public void NativeChangeIsRefreshedOnlyWhenTabActiveAndNoDraftOrRunningTask(
			bool active, bool busy, bool draftDirty, bool nativeTaskRunning, bool needsRefresh, bool expected)
		{
			Assert.That(Witcher3NativeRefreshPolicy.CanReadAutomatically(active, busy, draftDirty,
				nativeTaskRunning, needsRefresh), Is.EqualTo(expected));
		}

		[Test]
		public void NativeMutationStartAndEndInvalidateAndBlockReads_EvenWhenRolledBack()
		{
			var monitor = new ModActivationMonitor();
			var observer = new Witcher3NativeLifecycleObserver(monitor, null);
			var signal = new NativeDeploymentMutationSignal();
			try
			{
				observer.BindDeploymentMutations(signal);
				int revision = observer.Revision;
				Assert.That(observer.HasRunningNativeTask, Is.False);
				Assert.Throws<IOException>(() =>
				{
					using (signal.Begin())
					{
						Assert.That(observer.HasRunningNativeTask, Is.True);
						Assert.That(Witcher3NativeRefreshPolicy.CanApprove(revision, observer.Revision,
							observer.HasRunningNativeTask), Is.False);
						throw new IOException("simulated native rollback");
					}
				});
				Assert.That(observer.HasRunningNativeTask, Is.False);
				Assert.That(observer.Revision, Is.EqualTo(revision + 2));
				Assert.That(Witcher3NativeRefreshPolicy.CanApprove(revision, observer.Revision, false), Is.False);
			}
			finally { observer.Dispose(); }
		}

		[Test]
		public void OverlappingOwnerMutationsRemainBusyUntilLastScopeEnds()
		{
			var observer = new Witcher3NativeLifecycleObserver(new ModActivationMonitor(), null);
			var signal = new NativeDeploymentMutationSignal();
			try
			{
				observer.BindDeploymentMutations(signal);
				using (signal.Begin())
				{
					using (signal.Begin()) Assert.That(observer.HasRunningNativeTask, Is.True);
					Assert.That(observer.HasRunningNativeTask, Is.True);
				}
				Assert.That(signal.IsActive, Is.False);
				Assert.That(observer.HasRunningNativeTask, Is.False);
			}
			finally { observer.Dispose(); }
		}

		[TestCase(true)]
		[TestCase(false)]
		public void AmbientNativeTransactionKeepsOwnerIndexBusyUntilCommitOrRollback(bool commit)
		{
			var signal = new NativeDeploymentMutationSignal();
			var observer = new Witcher3NativeLifecycleObserver(new ModActivationMonitor(), null);
			try
			{
				observer.BindDeploymentMutations(signal);
				int revision = observer.Revision;
				using (var transaction = new Nexus.Transactions.TransactionScope())
				{
					using (signal.Begin())
						Assert.That(signal.IsActive, Is.True);
					Assert.That(observer.HasRunningNativeTask, Is.True,
						"The game-order UI must not read an owner stack before ambient native commit.");
					Assert.That(observer.Revision, Is.EqualTo(revision + 1));
					if (commit) transaction.Complete();
				}
				Assert.That(SpinWait.SpinUntil(() => !observer.HasRunningNativeTask, 2000), Is.True,
					"The old native transaction has released its authority.");
				Assert.That(observer.Revision, Is.EqualTo(revision + 2));
			}
			finally { observer.Dispose(); }
		}

		[Test]
		public void AuthorityRebindingUnsubscribesOldDeploymentManager_AndInvalidatesSnapshot()
		{
			var observer = new Witcher3NativeLifecycleObserver(new ModActivationMonitor(), null);
			var oldManagerSignal = new NativeDeploymentMutationSignal();
			var newManagerSignal = new NativeDeploymentMutationSignal();
			try
			{
				observer.BindDeploymentMutations(oldManagerSignal);
				int original = observer.Revision;
				observer.BindDeploymentMutations(newManagerSignal);
				Assert.That(observer.Revision, Is.EqualTo(original + 1));
				using (oldManagerSignal.Begin())
					Assert.That(observer.HasRunningNativeTask, Is.False);
				Assert.That(observer.Revision, Is.EqualTo(original + 1));
				using (newManagerSignal.Begin())
					Assert.That(observer.HasRunningNativeTask, Is.True);
				Assert.That(observer.Revision, Is.EqualTo(original + 3));
				observer.Dispose();
				using (newManagerSignal.Begin()) { }
				Assert.That(observer.Revision, Is.EqualTo(original + 3));
			}
			finally { observer.Dispose(); }
		}

		[Test]
		public void ThrowingNativeMutationSubscriberDoesNotBlockOthersOrTheOwnerMutation()
		{
			var signal = new NativeDeploymentMutationSignal();
			int received = 0;
			signal.Changed += (sender, args) => { throw new InvalidOperationException("disposed page"); };
			signal.Changed += (sender, args) => received++;
			Assert.DoesNotThrow(() => { using (signal.Begin()) { } });
			Assert.That(received, Is.EqualTo(2));
			Assert.That(signal.IsActive, Is.False);
		}

		[Test]
		public void PromotedOwnerStackMutationSignalsBeforeAndAfter_AndCleansUpOnFailure()
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(
				ModDeploymentRoot.Data, @"Mods\modExample\content\scripts\init.ws");
			bool failAtOwnerWrite = false;
			int wrote = 0;
			IInstallLog log = InterfaceStub<IInstallLog>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "IsDeploymentTargetPromoted": return true;
					case "GetDeploymentOwnerKeys": return new[] { "ownerA", "ownerB" };
					case "get_OriginalValuesKey": return "original";
					case "SetDeploymentOwners":
						wrote++;
						if (failAtOwnerWrite) throw new IOException("persist rejected");
						return null;
				}
				return null;
			});
			IVirtualModActivator virtualActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) => null);
			var manager = new ModDeploymentManager(log, virtualActivator);
			var observer = new Witcher3NativeLifecycleObserver(new ModActivationMonitor(), null);
			try
			{
				observer.BindDeploymentMutations(manager.NativeOwnerMutations);
				int revision = observer.Revision;
				manager.SwitchPromotedOwner(target, "ownerB"); // already effective; no write
				Assert.That(observer.Revision, Is.EqualTo(revision));
				manager.RestorePromotedOwnerStack(target, new[] { "ownerA", "ownerB" });
				Assert.That(observer.Revision, Is.EqualTo(revision + 2));
				Assert.That(wrote, Is.EqualTo(1));
				failAtOwnerWrite = true;
				Assert.Throws<IOException>(() => manager.RestorePromotedOwnerStack(target,
					new[] { "ownerA", "ownerB" }));
				Assert.That(observer.Revision, Is.EqualTo(revision + 4));
				Assert.That(manager.NativeOwnerMutations.IsActive, Is.False);
			}
			finally { observer.Dispose(); }
		}

		[Test]
		public void RescanJoinsChangedFoldersToUnmodifiedExternalPriorities_AndKeepsGameEnabledSeparate()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-w3r-native-refresh-" + Guid.NewGuid().ToString("N"));
			string game = Path.Combine(root, "game");
			string path = Witcher3ModSettingsFileReader.GetFilePath(Path.Combine(root, "docs"));
			try
			{
				Directory.CreateDirectory(game);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, "[modA]\r\nEnabled=0\r\nPriority=3\r\nVK=external\r\n",
					new UTF8Encoding(false, true));
				byte[] original = File.ReadAllBytes(path);
				var native = new NativeStateCaptureSnapshot(
					new InstallLogReadSnapshot("original-values", 10, new InstallLogReadMod[0],
						new InstallLogReadFile[0], new InstallLogReadIniEdit[0],
						new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]),
					new VirtualModReadSnapshot(new VirtualModReadLink[0]),
					new[] { new NativeStateCaptureRoot(ModDeploymentRoot.Data, game),
						new NativeStateCaptureRoot(ModDeploymentRoot.GameRoot, game) },
					new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
					new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0],
					NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureIssue[0]);
				Witcher3ModSettingsFileReadResult settings = Witcher3ModSettingsFileReader.Read(path);
				var first = Witcher3LoadOrderProjector.Project(Witcher3NativeComponentIndexer.Build(native, game,
					new Witcher3ComponentDiskObservation(game, new string[0], new[] { @"Mods\modA" }, true)),
					settings, Witcher3Edition.Legacy);
				var second = Witcher3LoadOrderProjector.Project(Witcher3NativeComponentIndexer.Build(native, game,
					new Witcher3ComponentDiskObservation(game, new string[0],
						new[] { @"Mods\modA", @"Mods\modNew" }, true)),
					Witcher3ModSettingsFileReader.Read(path), Witcher3Edition.Legacy);
				Assert.That(first.Rows.Any(x => x.FolderName == "modNew"), Is.False);
				Assert.That(second.Rows.Any(x => x.FolderName == "modNew"), Is.True);
				var preserved = second.Rows.Single(x => x.FolderName == "modA");
				Assert.That(preserved.ConfiguredPriority, Is.EqualTo(3));
				Assert.That(preserved.ConfiguredEnabled, Is.False);
				Assert.That(preserved.Presence, Is.EqualTo(Witcher3LoadOrderPresence.Observed));
				Assert.That(preserved.Origin, Is.EqualTo(Witcher3LoadOrderOrigin.ExternalObserved));
				Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
			}
			finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
		}
	}
}
