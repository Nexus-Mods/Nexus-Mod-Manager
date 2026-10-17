using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsGateA")]
	[Category("CollectionsC12Concurrency")]
	public class CollectionManualNativeMutationBoundaryTests
	{
		private string _tempRoot;
		private GameStorageService _storageService;

		[SetUp]
		public void SetUp()
		{
			_tempRoot = Path.Combine(Path.GetTempPath(), "NMM_ManualNativeBoundary_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tempRoot);
			_storageService = new GameStorageService(Path.Combine(_tempRoot, "Registry"), new Version(9, 3));
		}

		[TearDown]
		public void TearDown()
		{
			if (!String.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
				Directory.Delete(_tempRoot, true);
		}

		[Test]
		public void Acquire_OrdinaryNativeOperationWaitsForCanonicalCrossProcessReservationThenReloadsBeforeRebind()
		{
			GameStoragePathSet paths;
			CollectionTargetAuthority authority = CreateAuthority(out paths);
			var existingOwner = new CollectionTargetMutationLeaseManager();
			CollectionTargetMutationLease held = existingOwner.Acquire(authority);
			var manualManager = new CollectionTargetMutationLeaseManager();
			var waiting = new ManualResetEventSlim(false);
			var acquired = new ManualResetEventSlim(false);
			bool validated = false;
			bool rebound = false;
			var boundary = new CollectionManualNativeMutationBoundary(new object(),
				() => Tuple.Create(authority, paths), manualManager,
				(lease, liveAuthority, livePaths) =>
				{
					Assert.That(lease.HasCrossProcessReservation, Is.True);
					Assert.That(liveAuthority, Is.SameAs(authority));
					Assert.That(livePaths, Is.SameAs(paths));
					validated = true;
				},
				operation =>
				{
					Assert.That(validated, Is.True, "Native services must only rebind after authoritative reload/validation.");
					rebound = true;
				});

			CollectionTargetMutationLease manualLease = null;
			Task waiter = Task.Run(() =>
			{
				waiting.Set();
				manualLease = boundary.Acquire(new LeaseProbe());
				acquired.Set();
			});

			Assert.That(waiting.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(250)), Is.False,
				"An ordinary native operation must wait for the same physical-target reservation held by Collections.");
			held.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(waiter.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(validated, Is.True);
			Assert.That(rebound, Is.True);
			Assert.That(manualLease, Is.Not.Null);
			Assert.That(manualLease.HasCrossProcessReservation, Is.True);
			Assert.That(manualLease.TargetFingerprint, Is.EqualTo(authority.Target.Fingerprint));
			manualLease.Dispose();
		}

		[Test]
		public void Acquire_ValidationFailureReleasesNonAbandonedManualReservation()
		{
			GameStoragePathSet paths;
			CollectionTargetAuthority authority = CreateAuthority(out paths);
			var manualManager = new CollectionTargetMutationLeaseManager();
			var boundary = new CollectionManualNativeMutationBoundary(new object(),
				() => Tuple.Create(authority, paths), manualManager,
				(lease, liveAuthority, livePaths) => { throw new InvalidOperationException("reload failed"); },
				operation => Assert.Fail("A failed validation must not rebind native services."));

			Assert.Throws<InvalidOperationException>(() => boundary.Acquire(new LeaseProbe()));

			using (CollectionTargetMutationLease recovered = new CollectionTargetMutationLeaseManager().Acquire(authority))
				Assert.That(recovered.HasCrossProcessReservation, Is.True);
		}

		/// <summary>Reports startup work while authoritative reload is blocked, without completing the native operation.</summary>
		[TestCase(false)]
		[TestCase(true)]
		[NonParallelizable]
		public void Acquire_ReportsNativeStatePreparationBeforeReloadCompletes(bool failReload)
		{
			GameStoragePathSet paths;
			CollectionTargetAuthority authority = CreateAuthority(out paths);
			var currentField = typeof(CollectionManualNativeMutationBoundary).GetField("s_current",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			object previousBoundary = currentField.GetValue(null);
			using (var reloadEntered = new ManualResetEventSlim(false))
			using (var allowReload = new ManualResetEventSlim(false))
			using (var progressObserved = new ManualResetEventSlim(false))
			{
				var probe = new LeaseProbe();
				NativeModStatePreparationTask preparation = null;
				Exception failure = null;
				Task worker = null;
				probe.TaskStarted += (sender, args) =>
				{
					NativeModStatePreparationTask observed = args.Argument as NativeModStatePreparationTask;
					if (observed == null) return;
					preparation = observed;
					progressObserved.Set();
				};
				var boundary = new CollectionManualNativeMutationBoundary(new object(),
					() => Tuple.Create(authority, paths), new CollectionTargetMutationLeaseManager(),
					(lease, liveAuthority, livePaths) =>
					{
						reloadEntered.Set();
						if (!allowReload.Wait(TimeSpan.FromSeconds(10)))
							throw new TimeoutException("The test did not release native reload.");
						if (failReload) throw new InvalidOperationException("Controlled reload failure.");
					}, operation => { });
				try
				{
					currentField.SetValue(null, boundary);
					worker = Task.Run(() =>
					{
						try { using (probe.AcquireWithProgress()) { } }
						catch (Exception exception) { failure = exception; }
					});
					Assert.That(reloadEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
					Assert.That(progressObserved.Wait(TimeSpan.FromSeconds(5)), Is.True,
						"Native-state reload remained invisible until archive preparation.");
					Assert.That(preparation.Status, Is.EqualTo(Nexus.Client.BackgroundTasks.TaskStatus.Running));
					Assert.That(preparation.OverallMessage, Is.Not.Empty);
					Assert.That(preparation.ShowOverallProgressAsMarquee, Is.True);
					Assert.That(probe.IsCompleted, Is.False);
					allowReload.Set();
					Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
					Assert.That(preparation.Status, Is.EqualTo(failReload
						? Nexus.Client.BackgroundTasks.TaskStatus.Error : Nexus.Client.BackgroundTasks.TaskStatus.Complete));
					Assert.That(failure == null, Is.EqualTo(!failReload));
					Assert.That(probe.IsCompleted, Is.False,
						"Finishing startup preparation must not publish native operation completion.");
				}
				finally
				{
					allowReload.Set();
					if (worker != null) worker.Wait(TimeSpan.FromSeconds(10));
					currentField.SetValue(null, previousBoundary);
				}
			}
		}

		private CollectionTargetAuthority CreateAuthority(out GameStoragePathSet paths)
		{
			string root = Path.Combine(_tempRoot, "Storage");
			string installInfo = Path.Combine(root, "InstallInfo");
			string mods = Path.Combine(root, "Mods");
			string virtualInstall = Path.Combine(root, "VirtualInstall");
			string game = Path.Combine(_tempRoot, "Game");
			Directory.CreateDirectory(installInfo);
			Directory.CreateDirectory(mods);
			Directory.CreateDirectory(virtualInstall);
			Directory.CreateDirectory(game);
			File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog />");

			paths = new GameStoragePathSet
			{
				GameId = "SkyrimSE",
				GameName = "SkyrimSE",
				GameInstallPath = game,
				InstallInfoPath = installInfo,
				ModsPath = mods,
				VirtualInstallPath = virtualInstall,
				LinkFolderRequired = false
			};
			_storageService.InitializeMetadataForStorage(paths);
			return new CollectionTargetIdentityResolver(_storageService).Resolve(paths);
		}

		private sealed class LeaseProbe : ModInstallerBase
		{
			/// <summary>Runs the ordinary native preflight without starting an installer or changing files.</summary>
			public CollectionTargetMutationLease AcquireWithProgress()
			{
				return AcquireMutationLease();
			}
		}
	}
}
