using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionTargetCrossProcessLeaseTests
	{
		private string _tempRoot;
		private GameStorageService _storageService;

		[SetUp]
		public void SetUp()
		{
			_tempRoot = Path.Combine(Path.GetTempPath(), "NMM_CollectionCrossProcessLease_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tempRoot);
			_storageService = new GameStorageService(Path.Combine(_tempRoot, "Registry"), new Version(9, 3));
		}

		[TearDown]
		public void TearDown()
		{
			if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
				Directory.Delete(_tempRoot, true);
		}

		[Test]
		public void Acquire_SameCanonicalTargetAcrossIndependentCoordinatorsSerializes()
		{
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			var firstManager = new CollectionTargetCrossProcessLeaseManager();
			var secondManager = new CollectionTargetCrossProcessLeaseManager();
			CollectionTargetCrossProcessLease first = firstManager.Acquire(authority);
			var waiting = new ManualResetEventSlim(false);
			var acquired = new ManualResetEventSlim(false);

			Task second = Task.Run(() =>
			{
				waiting.Set();
				using (secondManager.Acquire(authority))
					acquired.Set();
			});

			Assert.That(waiting.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(250)), Is.False);
			first.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(second.Wait(TimeSpan.FromSeconds(2)), Is.True);
		}

		[Test]
		public void Acquire_SamePhysicalGameWithDifferentStorageAuthoritiesSharesPhysicalTargetReservation()
		{
			CollectionTargetAuthority firstAuthority = CreateAuthority("SkyrimSE", "StorageA", "SharedGame");
			var independentStorageService = new GameStorageService(Path.Combine(_tempRoot, "RegistryB"), new Version(9, 3));
			CollectionTargetAuthority secondAuthority = CreateAuthority(independentStorageService, "SkyrimSE", "StorageB", "SharedGame");
			var firstManager = new CollectionTargetCrossProcessLeaseManager();
			var secondManager = new CollectionTargetCrossProcessLeaseManager();
			CollectionTargetCrossProcessLease first = firstManager.Acquire(firstAuthority);
			var acquired = new ManualResetEventSlim(false);
			string secondCoordinationName = null;

			Task second = Task.Run(() =>
			{
				using (CollectionTargetCrossProcessLease lease = secondManager.Acquire(secondAuthority))
				{
					secondCoordinationName = lease.CoordinationName;
					acquired.Set();
				}
			});

			Assert.That(firstAuthority.Target.Fingerprint, Is.Not.EqualTo(secondAuthority.Target.Fingerprint),
				"Different native storage authorities must remain distinct Collection targets.");
			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(250)), Is.False,
				"C4.15 must still serialize the shared physical game so C4.16 can reject conflicting InstallLogs under one lock.");
			string firstCoordinationName = first.CoordinationName;
			first.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(second.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(secondCoordinationName, Is.EqualTo(firstCoordinationName));
		}

		[Test]
		public void Acquire_DifferentCanonicalTargetsUseIndependentNamedReservations()
		{
			CollectionTargetAuthority firstAuthority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			CollectionTargetAuthority secondAuthority = CreateAuthority("Fallout4", "StorageB", "GameB");
			var manager = new CollectionTargetCrossProcessLeaseManager();

			using (CollectionTargetCrossProcessLease first = manager.Acquire(firstAuthority))
			using (CollectionTargetCrossProcessLease second = manager.Acquire(secondAuthority))
			{
				Assert.That(second.CoordinationName, Is.Not.EqualTo(first.CoordinationName));
				Assert.That(second.TargetFingerprint, Is.EqualTo(secondAuthority.Target.Fingerprint));
			}
		}

		[Test]
		public void Dispose_FromDifferentThread_ReleasesThroughStableOwnerThread()
		{
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			var manager = new CollectionTargetCrossProcessLeaseManager();
			CollectionTargetCrossProcessLease lease = manager.Acquire(authority);

			Task release = Task.Run(() => lease.Dispose());
			Assert.That(release.Wait(TimeSpan.FromSeconds(2)), Is.True);

			using (CollectionTargetCrossProcessLease reacquired = manager.Acquire(authority))
				Assert.That(reacquired.IsDisposed, Is.False);
		}

		[Test]
		public void AcquireAsync_CancellationWhileOtherOwnerHoldsTarget_DoesNotStealReservation()
		{
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			var firstManager = new CollectionTargetCrossProcessLeaseManager();
			var secondManager = new CollectionTargetCrossProcessLeaseManager();

			using (firstManager.Acquire(authority))
			using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
			{
				Task<CollectionTargetCrossProcessLease> waiting = secondManager.AcquireAsync(authority, cancellation.Token);
				Assert.Catch<OperationCanceledException>(() => waiting.GetAwaiter().GetResult());
			}

			using (CollectionTargetCrossProcessLease lease = secondManager.Acquire(authority))
				Assert.That(lease.TargetFingerprint, Is.EqualTo(authority.Target.Fingerprint));
		}

		[Test]
		public void Acquire_MissingCanonicalAuthorityFailsClosed()
		{
			var manager = new CollectionTargetCrossProcessLeaseManager();
			Assert.Throws<ArgumentNullException>(() => manager.Acquire((CollectionTargetAuthority)null));
		}

		[Test]
		public void MutationLease_AbandonedNamedMutexRequiresRecoveryBeforeInheritance()
		{
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			var crossProcessManager = new CollectionTargetCrossProcessLeaseManager();
			string coordinationName;
			using (CollectionTargetCrossProcessLease discovery = crossProcessManager.Acquire(authority))
				coordinationName = discovery.CoordinationName;

			using (var abandonedMutex = new Mutex(false, coordinationName))
			using (var owned = new ManualResetEventSlim(false))
			{
				var abandoningThread = new Thread(() =>
				{
					abandonedMutex.WaitOne();
					owned.Set();
					// Deliberately exit without ReleaseMutex to simulate a crashed owner.
				});
				abandoningThread.IsBackground = true;
				abandoningThread.Start();

				Assert.That(owned.Wait(TimeSpan.FromSeconds(2)), Is.True);
				Assert.That(abandoningThread.Join(TimeSpan.FromSeconds(2)), Is.True);

				var mutationManager = new CollectionTargetMutationLeaseManager();
				using (CollectionTargetMutationLease root = mutationManager.Acquire(authority))
				{
					Assert.That(root.CrossProcessLeaseWasAbandoned, Is.True);
					Assert.That(root.RequiresRecovery, Is.True);
					Assert.Throws<CollectionTargetRecoveryRequiredException>(() => mutationManager.Inherit(root, authority));
					Assert.Throws<CollectionTargetRecoveryRequiredException>(() => root.Dispose());
					Assert.That(root.IsDisposed, Is.False);

					root.MarkRecoveryCompleted();
					Assert.That(root.RequiresRecovery, Is.False);
					using (CollectionTargetMutationLease child = mutationManager.Inherit(root, authority))
						Assert.That(child.ReservationId, Is.EqualTo(root.ReservationId));
				}
			}
		}

		[Test]
		public void MutationLease_OnlyRootMayAcknowledgeRecoveryState()
		{
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			var manager = new CollectionTargetMutationLeaseManager();

			using (CollectionTargetMutationLease root = manager.Acquire(authority))
			using (CollectionTargetMutationLease child = manager.Inherit(root, authority))
			{
				Assert.Throws<InvalidOperationException>(() => child.MarkRecoveryCompleted());
			}
		}

		[Test]
		public void MutationLease_InheritedChildKeepsNamedReservationAfterRootHandleIsDisposed()
		{
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			var firstManager = new CollectionTargetMutationLeaseManager();
			var secondManager = new CollectionTargetMutationLeaseManager();
			CollectionTargetMutationLease root = firstManager.Acquire(authority);
			CollectionTargetMutationLease child = firstManager.Inherit(root, authority);
			root.Dispose();
			var waiting = new ManualResetEventSlim(false);
			var acquired = new ManualResetEventSlim(false);

			Task competitor = Task.Run(() =>
			{
				waiting.Set();
				using (secondManager.Acquire(authority))
					acquired.Set();
			});

			Assert.That(waiting.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(250)), Is.False);
			child.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(competitor.Wait(TimeSpan.FromSeconds(2)), Is.True);
		}

		[Test]
		public void CoordinationName_IsDeterministicAndDoesNotExposeCanonicalFingerprint()
		{
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			var manager = new CollectionTargetCrossProcessLeaseManager();
			string firstName;
			string secondName;

			using (CollectionTargetCrossProcessLease first = manager.Acquire(authority))
				firstName = first.CoordinationName;
			using (CollectionTargetCrossProcessLease second = manager.Acquire(authority))
				secondName = second.CoordinationName;

			Assert.That(secondName, Is.EqualTo(firstName));
			Assert.That(firstName, Does.Not.Contain(authority.Target.Fingerprint));
		}

		private CollectionTargetAuthority CreateAuthority(string gameId, string storageName, string gameDirectoryName)
		{
			return CreateAuthority(_storageService, gameId, storageName, gameDirectoryName);
		}

		private CollectionTargetAuthority CreateAuthority(GameStorageService storageService, string gameId, string storageName, string gameDirectoryName)
		{
			string root = Path.Combine(_tempRoot, storageName);
			string installInfo = Path.Combine(root, "InstallInfo");
			string mods = Path.Combine(root, "Mods");
			string virtualInstall = Path.Combine(root, "VirtualInstall");
			string game = Path.Combine(_tempRoot, gameDirectoryName);
			Directory.CreateDirectory(installInfo);
			Directory.CreateDirectory(mods);
			Directory.CreateDirectory(virtualInstall);
			Directory.CreateDirectory(game);
			File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog />");

			var paths = new GameStoragePathSet
			{
				GameId = gameId,
				GameName = gameId,
				GameInstallPath = game,
				InstallInfoPath = installInfo,
				ModsPath = mods,
				VirtualInstallPath = virtualInstall,
				LinkFolderRequired = false
			};
			storageService.InitializeMetadataForStorage(paths);
			return new CollectionTargetIdentityResolver(storageService).Resolve(paths);
		}
	}
}
