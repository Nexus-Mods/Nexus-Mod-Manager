using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client;
using Nexus.Client.Games;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class GameDiscoveryRecursiveScannerTests
	{
		private string _root;

		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-discovery-scan-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(_root)) Directory.Delete(_root, true);
		}

		[Test]
		public void NestedExecutable_ResolvesActualGameRoot_WithoutAcceptingASiblingPath()
		{
			var matcher = new GameDiscoveryScanCandidateMatcher(new[] { new Descriptor("Nested", @"bin\x64\game.exe") });
			string expectedDirectory = Path.Combine(_root, "bin", "x64");
			string siblingDirectory = Path.Combine(_root, "unrelated");
			Directory.CreateDirectory(expectedDirectory);
			Directory.CreateDirectory(siblingDirectory);
			string executable = Path.Combine(expectedDirectory, "game.exe");
			File.WriteAllBytes(executable, new byte[] { 42 });
			string sibling = Path.Combine(siblingDirectory, "game.exe");
			File.WriteAllBytes(sibling, new byte[] { 42 });

			var results = new List<GameDiscoveryInstallationCandidate>();
			matcher.TryAddFile(sibling, results);
			matcher.TryAddFile(executable, results);
			matcher.TryAddFile(executable, results);
			Assert.That(results, Has.Count.EqualTo(1));
			Assert.That(results[0].ModeId, Is.EqualTo("Nested"));
			Assert.That(results[0].GameRoot, Is.EqualTo(_root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
			Assert.That(results[0].Source, Is.EqualTo("Drive scan"));
		}

		[Test]
		public void SharedExecutable_NameStillRequiresPerModeEditionApproval()
		{
			string executable = Path.Combine(_root, "game.exe");
			File.WriteAllBytes(executable, new byte[] { 1 });
			var accepted = new Descriptor("Accepted", "game.exe", true);
			var rejected = new Descriptor("RejectedEdition", "game.exe", false);
			var matcher = new GameDiscoveryScanCandidateMatcher(new[] { accepted, rejected });
			var results = new List<GameDiscoveryInstallationCandidate>();
			matcher.TryAddFile(executable, results);
			Assert.That(matcher.SearchFileNames, Is.EquivalentTo(new[] { "game.exe" }));
			Assert.That(results.Select(candidate => candidate.ModeId), Is.EquivalentTo(new[] { "Accepted" }));
		}

		[Test]
		public void SameGame_CanHaveTwoValidInstallations_WithoutDuplicateResults()
		{
			var matcher = new GameDiscoveryScanCandidateMatcher(new[] { new Descriptor("Test", "game.exe") });
			var results = new List<GameDiscoveryInstallationCandidate>();
			foreach (string folder in new[] { "One", "Two" })
			{
				string directory = Path.Combine(_root, folder);
				Directory.CreateDirectory(directory);
				string executable = Path.Combine(directory, "game.exe");
				File.WriteAllBytes(executable, new byte[] { 1 });
				matcher.TryAddFile(executable, results);
				matcher.TryAddFile(executable, results);
			}
			Assert.That(results.Select(candidate => candidate.GameRoot), Is.EquivalentTo(
				new[] { Path.Combine(_root, "One"), Path.Combine(_root, "Two") }));
		}

		[Test]
		public void QuickRefresh_MergesScanEvidence_WithoutReplacingPathsOrMutatingInput()
		{
			var first = new[] { new GameDiscoveryInstallationCandidate("TEST", @"C:\Games\Example", "Drive scan") };
			var second = new[]
			{
				new GameDiscoveryInstallationCandidate("test", @"c:\games\example\", "Steam"),
				new GameDiscoveryInstallationCandidate("Test", @"D:\Another", "GOG")
			};
			var merged = GameDiscoveryCandidateMerger.Merge(first, second);
			Assert.That(merged, Has.Count.EqualTo(2));
			Assert.That(merged[0], Is.SameAs(first[0]));
			Assert.That(merged[1], Is.SameAs(second[1]));
			Assert.That(first, Has.Length.EqualTo(1));
		}

		[Test]
		public void ScanWithoutSupportedExecutables_DoesNotVisitDrives()
		{
			var scanner = new GameDiscoveryRecursiveScanner();
			var empty = scanner.ScanAsync(new[] { new Descriptor("Empty", null) }, null, CancellationToken.None)
				.GetAwaiter().GetResult();
			Assert.That(empty.Cancelled, Is.False);
			Assert.That(empty.Candidates, Is.Empty);
		}

		[Test]
		public void CancelledBeforeStart_DoesNotVisitDrives()
		{
			var scanner = new GameDiscoveryRecursiveScanner();
            Assert.CatchAsync<OperationCanceledException>(async () =>
			{
				await scanner.ScanAsync(new[] { new Descriptor("Test", "game.exe") }, null, new CancellationToken(true));
			});
		}

		private sealed class Descriptor : GameModeDescriptorBase, IGameInstallationCandidateValidator
		{
			private readonly string _id;
			private readonly string _exe;
			private readonly bool _accepted;
			internal Descriptor(string id, string exe, bool accepted = true) : base(null)
			{ _id = id; _exe = exe; _accepted = accepted; }
			public override string Name => _id;
			public override string ModeId => _id;
			public override string[] GameExecutables => _exe == null ? Array.Empty<string>() : new[] { _exe };
			public override string PluginDirectory => null;
			public override Theme ModeTheme => null;
			public bool IsValidInstallationCandidate(string absoluteGameRoot) => _accepted;
		}
	}
}
