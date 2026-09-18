using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionTargetMutationLeaseTests
	{
		private string _tempRoot;
		private GameStorageService _storageService;

		[SetUp]
		public void SetUp()
		{
			_tempRoot = Path.Combine(Path.GetTempPath(), "NMM_CollectionLease_" + Guid.NewGuid().ToString("N"));
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
		public void Acquire_SameTargetSerializesUntilFirstLeaseIsReleased()
		{
			var manager = new CollectionTargetMutationLeaseManager();
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			CollectionTargetMutationLease first = manager.Acquire(authority);
			var waiting = new ManualResetEventSlim(false);
			var acquired = new ManualResetEventSlim(false);

			Task second = Task.Run(() =>
			{
				waiting.Set();
				using (manager.Acquire(authority))
					acquired.Set();
			});

			Assert.That(waiting.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(200)), Is.False);
			first.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(second.Wait(TimeSpan.FromSeconds(2)), Is.True);
		}

		[Test]
		public void Acquire_DifferentTargetsRemainSerializedByInitialProcessPolicy()
		{
			var manager = new CollectionTargetMutationLeaseManager();
			CollectionTargetAuthority firstAuthority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			CollectionTargetAuthority secondAuthority = CreateAuthority("Fallout4", "StorageB", "GameB");
			CollectionTargetMutationLease first = manager.Acquire(firstAuthority);
			var waiting = new ManualResetEventSlim(false);
			var acquired = new ManualResetEventSlim(false);

			Task second = Task.Run(() =>
			{
				waiting.Set();
				using (manager.Acquire(secondAuthority))
					acquired.Set();
			});

			Assert.That(waiting.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(200)), Is.False,
				"C4.14 intentionally serializes native mutation across the whole process, not only per target.");
			first.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(second.Wait(TimeSpan.FromSeconds(2)), Is.True);
		}

		[Test]
		public void Inherit_SameTargetSharesReservationAndPreventsParentSelfDeadlock()
		{
			var manager = new CollectionTargetMutationLeaseManager();
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			CollectionTargetMutationLease root = manager.Acquire(authority);
			CollectionTargetMutationLease child = manager.Inherit(root, authority);

			Assert.That(child.IsRoot, Is.False);
			Assert.That(child.ReservationId, Is.EqualTo(root.ReservationId));
			Assert.That(child.TargetFingerprint, Is.EqualTo(root.TargetFingerprint));

			root.Dispose();
			var acquired = new ManualResetEventSlim(false);
			Task waiter = Task.Run(() =>
			{
				using (manager.Acquire(authority))
					acquired.Set();
			});

			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(200)), Is.False,
				"Disposing the root handle must not release the reservation while an inherited child still owns it.");
			child.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(waiter.Wait(TimeSpan.FromSeconds(2)), Is.True);
		}

		[Test]
		public void Inherit_DifferentTargetFailsInsteadOfRebindingReservation()
		{
			var manager = new CollectionTargetMutationLeaseManager();
			CollectionTargetAuthority firstAuthority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			CollectionTargetAuthority secondAuthority = CreateAuthority("Fallout4", "StorageB", "GameB");

			using (CollectionTargetMutationLease root = manager.Acquire(firstAuthority))
			{
				Assert.Throws<InvalidOperationException>(() => manager.Inherit(root, secondAuthority));
			}
		}

		[Test]
		public void Inherit_DifferentManagerFailsClosed()
		{
			var firstManager = new CollectionTargetMutationLeaseManager();
			var secondManager = new CollectionTargetMutationLeaseManager();
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");

			using (CollectionTargetMutationLease root = firstManager.Acquire(authority))
			{
				Assert.Throws<InvalidOperationException>(() => secondManager.Inherit(root, authority));
			}
		}

		[Test]
		public void Inherit_DisposedParentFailsImmediately()
		{
			var manager = new CollectionTargetMutationLeaseManager();
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			CollectionTargetMutationLease root = manager.Acquire(authority);
			root.Dispose();

			Assert.Throws<ObjectDisposedException>(() => manager.Inherit(root, authority));
		}

		[Test]
		public void AcquireAsync_CancellationStopsWaitingWithoutTakingReservation()
		{
			var manager = new CollectionTargetMutationLeaseManager();
			CollectionTargetAuthority firstAuthority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			CollectionTargetAuthority secondAuthority = CreateAuthority("Fallout4", "StorageB", "GameB");

			using (manager.Acquire(firstAuthority))
			using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
			{
				Task<CollectionTargetMutationLease> waiting = manager.AcquireAsync(secondAuthority, cancellation.Token);
				Assert.Throws<OperationCanceledException>(() => waiting.GetAwaiter().GetResult());
			}

			using (CollectionTargetMutationLease lease = manager.Acquire(secondAuthority))
				Assert.That(lease.TargetFingerprint, Is.EqualTo(secondAuthority.Target.Fingerprint));
		}

		[Test]
		public void Dispose_IsIdempotentAndDoesNotOverReleaseProcessGate()
		{
			var manager = new CollectionTargetMutationLeaseManager();
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			CollectionTargetMutationLease first = manager.Acquire(authority);

			first.Dispose();
			first.Dispose();

			using (CollectionTargetMutationLease second = manager.Acquire(authority))
				Assert.That(second.ReservationId, Is.Not.EqualTo(first.ReservationId));
		}

		[Test]
		public void NativeOperation_AcquiresSharedProcessLeaseFromC3Fingerprint()
		{
			var first = new LeaseProbe();
			var second = new LeaseProbe();
			first.Assign(CreateNativeIdentity("manual-target"));
			second.Assign(CreateNativeIdentity("other-manual-target"));
			CollectionTargetMutationLease firstLease = first.AcquireForTest();
			var waiting = new ManualResetEventSlim(false);
			var acquired = new ManualResetEventSlim(false);

			Task waiter = Task.Run(() =>
			{
				waiting.Set();
				using (second.AcquireForTest())
					acquired.Set();
			});

			Assert.That(firstLease.IsRoot, Is.True);
			Assert.That(waiting.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(200)), Is.False);
			firstLease.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(waiter.Wait(TimeSpan.FromSeconds(2)), Is.True);
		}

		[Test]
		public void NativeOperation_WithoutC3IdentityStillJoinsSharedProcessGate()
		{
			var first = new LeaseProbe();
			var second = new LeaseProbe();
			CollectionTargetMutationLease firstLease = first.AcquireForTest();
			var waiting = new ManualResetEventSlim(false);
			var acquired = new ManualResetEventSlim(false);

			Task waiter = Task.Run(() =>
			{
				waiting.Set();
				using (second.AcquireForTest())
					acquired.Set();
			});

			Assert.That(waiting.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(acquired.Wait(TimeSpan.FromMilliseconds(200)), Is.False);
			firstLease.Dispose();
			Assert.That(acquired.Wait(TimeSpan.FromSeconds(2)), Is.True);
			Assert.That(waiter.Wait(TimeSpan.FromSeconds(2)), Is.True);
		}

		[Test]
		public void NativeOperation_InheritsAssignedCollectionParentReservation()
		{
			CollectionTargetAuthority authority = CreateAuthority("SkyrimSE", "StorageA", "GameA");
			var probe = new LeaseProbe();
			probe.Assign(CreateNativeIdentity(authority.Target.Fingerprint));

			using (CollectionTargetMutationLease parent = CollectionTargetMutationLeaseManager.Shared.Acquire(authority))
			{
				probe.AssignParent(parent);
				using (CollectionTargetMutationLease child = probe.AcquireForTest())
				{
					Assert.That(child.IsRoot, Is.False);
					Assert.That(child.ReservationId, Is.EqualTo(parent.ReservationId));
				}
			}
		}

		private static ModOperationIdentity CreateNativeIdentity(string targetFingerprint)
		{
			return ModOperationIdentity.CreateNew(ModOperationOrigin.Manual,
				new ModOperationFingerprint(targetFingerprint,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Default), null));
		}

		private sealed class LeaseProbe : ModInstallerBase
		{
			public void Assign(ModOperationIdentity identity)
			{
				AssignOperationIdentity(identity);
			}

			public void AssignParent(CollectionTargetMutationLease parentLease)
			{
				AssignParentMutationLease(parentLease);
			}

			public CollectionTargetMutationLease AcquireForTest()
			{
				return AcquireMutationLease();
			}
		}

		private CollectionTargetAuthority CreateAuthority(string gameId, string storageName, string gameDirectoryName)
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
			_storageService.InitializeMetadataForStorage(paths);
			return new CollectionTargetIdentityResolver(_storageService).Resolve(paths);
		}
	}
}
