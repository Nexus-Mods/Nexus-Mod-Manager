using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Opt-in C12 workload characterization. These tests intentionally create large synthetic managed setups and
	/// report measurements; they do not impose machine-specific wall-clock pass/fail thresholds.
	/// </summary>
	[TestFixture]
	[Category("CollectionsC12Performance")]
	public class CollectionPerformanceCharacterizationTests
	{
		private const int FilesPerMod = 4;
		private const int MeasuredIterations = 3;

		[Test]
		[Explicit("C12 performance characterization is opt-in and intentionally constructs large synthetic native-state workloads.")]
		public void NativeStateCapture_LargeManagedSetups_EmitsRepeatableMeasurements()
		{
			CharacterizeNativeStateCapture(100);
			CharacterizeNativeStateCapture(500);
			CharacterizeNativeStateCapture(1500);
		}

		[Test]
		[Explicit("C12 cold/warm retained-artifact verification characterization is opt-in and hashes large payloads.")]
		public void RetainedArtifactVerification_ColdThenWarm_UsesVerificationCache()
		{
			CharacterizeRetainedArtifactVerification(1 * 1024 * 1024);
			CharacterizeRetainedArtifactVerification(8 * 1024 * 1024);
			CharacterizeRetainedArtifactVerification(32 * 1024 * 1024);
		}

		private static void CharacterizeNativeStateCapture(int modCount)
		{
			InstallLogReadSnapshot snapshot = CreateSnapshot(modCount, FilesPerMod);
			CollectionNativeStateReader reader = CreateReader(snapshot);
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("c12-native-state-characterization");

			// Warm the runtime before recording the repeated workload. Metrics are sampled after this call.
			CollectionNativeStateIndex warm = reader.Capture(target);
			Assert.AreEqual(modCount, warm.Mods.Count);
			Assert.AreEqual(modCount * FilesPerMod, warm.Files.Count);

			CollectionPerformanceSnapshot before = CollectionPerformanceMetrics.Capture();
			var watch = Stopwatch.StartNew();
			CollectionNativeStateIndex state = null;
			for (int iteration = 0; iteration < MeasuredIterations; iteration++)
				state = reader.Capture(target);
			watch.Stop();
			CollectionPerformanceSnapshot after = CollectionPerformanceMetrics.Capture();

			Assert.That(state, Is.Not.Null);
			Assert.AreEqual(modCount, state.Mods.Count);
			Assert.AreEqual(modCount * FilesPerMod, state.Files.Count);
			Assert.AreEqual(MeasuredIterations, after.NativeStateIndexBuildCount - before.NativeStateIndexBuildCount,
				"One explicit capture should produce exactly one native-state index build.");

			double recordedIndexMilliseconds = after.NativeStateIndexBuildMilliseconds - before.NativeStateIndexBuildMilliseconds;
			TestContext.WriteLine(String.Format(CultureInfo.InvariantCulture,
				"C12_NATIVE_STATE mods={0} files={1} iterations={2} total_ms={3:F3} mean_total_ms={4:F3} index_ms={5:F3} mean_index_ms={6:F3} ms_per_1000_entries={7:F3}",
				modCount, modCount * FilesPerMod, MeasuredIterations, watch.Elapsed.TotalMilliseconds,
				watch.Elapsed.TotalMilliseconds / MeasuredIterations, recordedIndexMilliseconds,
				recordedIndexMilliseconds / MeasuredIterations,
				(recordedIndexMilliseconds / MeasuredIterations) * 1000.0 / (modCount + modCount * FilesPerMod)));
		}

		private static void CharacterizeRetainedArtifactVerification(int byteCount)
		{
			string root = Path.Combine(Path.GetTempPath(), "NMM-C12-Retained-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var publisher = new CollectionsRetainedArtifactStore(featureStore);
				CollectionsRetainedArtifact artifact;
				byte[] payload = CreateDeterministicPayload(byteCount);
				using (var source = new MemoryStream(payload, false))
					artifact = publisher.Publish(source);

				// A new store instance models a restarted/cold process: its verification cache is intentionally empty.
				var verifier = new CollectionsRetainedArtifactStore(featureStore);
				CollectionPerformanceSnapshot beforeCold = CollectionPerformanceMetrics.Capture();
				var coldWatch = Stopwatch.StartNew();
				Assert.IsTrue(verifier.VerifyArtifact(artifact.ArtifactId));
				coldWatch.Stop();
				CollectionPerformanceSnapshot afterCold = CollectionPerformanceMetrics.Capture();

				var warmWatch = Stopwatch.StartNew();
				Assert.IsTrue(verifier.VerifyArtifact(artifact.ArtifactId));
				warmWatch.Stop();
				CollectionPerformanceSnapshot afterWarm = CollectionPerformanceMetrics.Capture();

				long coldHashes = afterCold.RetainedArtifactVerificationHashCount - beforeCold.RetainedArtifactVerificationHashCount;
				long coldHashedBytes = afterCold.RetainedArtifactVerificationHashedBytes - beforeCold.RetainedArtifactVerificationHashedBytes;
				long coldCacheHits = afterCold.RetainedArtifactVerificationCacheHitCount - beforeCold.RetainedArtifactVerificationCacheHitCount;
				long warmHashes = afterWarm.RetainedArtifactVerificationHashCount - afterCold.RetainedArtifactVerificationHashCount;
				long warmCacheHits = afterWarm.RetainedArtifactVerificationCacheHitCount - afterCold.RetainedArtifactVerificationCacheHitCount;
				double coldHashMilliseconds = afterCold.RetainedArtifactVerificationHashMilliseconds - beforeCold.RetainedArtifactVerificationHashMilliseconds;

				Assert.AreEqual(1, coldHashes, "A cold verification should perform exactly one complete SHA-256 pass.");
				Assert.AreEqual(artifact.ByteLength, coldHashedBytes, "Cold verification should hash the complete retained artifact exactly once.");
				Assert.AreEqual(0, coldCacheHits, "A new retained-artifact store must not inherit process-local verification proof from another instance.");
				Assert.AreEqual(0, warmHashes, "Warm verification must reuse the verified-artifact cache instead of hashing the blob again.");
				Assert.AreEqual(1, warmCacheHits, "The immediately repeated verification should be one verification-cache hit.");

				TestContext.WriteLine(String.Format(CultureInfo.InvariantCulture,
					"C12_RETAINED_VERIFY bytes={0} cold_wall_ms={1:F3} cold_hash_ms={2:F3} warm_wall_ms={3:F3} cold_hashes={4} warm_hashes={5} warm_cache_hits={6}",
					artifact.ByteLength, coldWatch.Elapsed.TotalMilliseconds, coldHashMilliseconds, warmWatch.Elapsed.TotalMilliseconds,
					coldHashes, warmHashes, warmCacheHits));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static byte[] CreateDeterministicPayload(int byteCount)
		{
			var payload = new byte[byteCount];
			for (int index = 0; index < payload.Length; index++)
				payload[index] = (byte)((index * 31 + 17) & 0xff);
			return payload;
		}

		private static InstallLogReadSnapshot CreateSnapshot(int modCount, int filesPerMod)
		{
			var mods = new List<InstallLogReadMod>(modCount);
			var files = new List<InstallLogReadFile>(modCount * filesPerMod);
			for (int modIndex = 0; modIndex < modCount; modIndex++)
			{
				string modKey = "perf-mod-" + modIndex.ToString("D5", CultureInfo.InvariantCulture);
				mods.Add(new InstallLogReadMod(modKey, "C:\\Mods\\" + modKey + ".7z", modKey + ".7z",
					"Performance Mod " + modIndex.ToString(CultureInfo.InvariantCulture),
					(10000 + modIndex).ToString(CultureInfo.InvariantCulture),
					(20000 + modIndex).ToString(CultureInfo.InvariantCulture), "1.0", "1.0.0.0", false,
					ModInstallRoot.Data, ModInstallMethod.Virtual, false));

				for (int fileIndex = 0; fileIndex < filesPerMod; fileIndex++)
				{
					string relativePath = "c12\\" + modKey + "\\file-" + fileIndex.ToString("D2", CultureInfo.InvariantCulture) + ".bin";
					ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, relativePath);
					files.Add(new InstallLogReadFile(relativePath, deploymentTarget, new[] { modKey }));
				}
			}

			return new InstallLogReadSnapshot("original-values", 1, mods, files,
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
		}

		private static CollectionNativeStateReader CreateReader(InstallLogReadSnapshot snapshot)
		{
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				method.Name == "GetCommittedStateSnapshot" ? snapshot : null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
				method.Name == "GetReadSnapshot" ? new VirtualModReadSnapshot(new VirtualModReadLink[0]) : null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_InstallationPath": return Path.GetTempPath();
					case "get_HasSecondaryInstallPath": return false;
					case "get_UsesPlugins": return false;
					default: return null;
				}
			});
			return new CollectionNativeStateReader(installLog, virtualModActivator, null, gameMode, null);
		}
	}
}
