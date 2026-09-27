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
		}
	}
}
