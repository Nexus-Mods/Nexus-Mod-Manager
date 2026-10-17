using System;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Bounded read-only traversal fixture, separate from native owner projection.</summary>
	[TestFixture]
	public class Witcher3ComponentDiskScanTests
	{
		[Test]
		public void MissingGameRootIsNotCreatedOrDeclaredComplete()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-w3r-disk-missing-" + Guid.NewGuid().ToString("N"));
			var scan = Witcher3ComponentDiskScan.Scan(root);
			Assert.That(scan.IsComplete, Is.False);
			Assert.That(scan.Issues.Single().Kind, Is.EqualTo(Witcher3ComponentDiskIssueKind.GameRootMissing));
			Assert.That(Directory.Exists(root), Is.False);
		}

		[Test]
		public void FindsOnlyModsAndDlcAndDoesNotInspectUnrelatedGameFolders()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-w3r-disk-" + Guid.NewGuid().ToString("N"));
			try
			{
				string mod = Path.Combine(root, "Mods", "modA", "content");
				string dlc = Path.Combine(root, "DLC", "dlcA", "content");
				string untouched = Path.Combine(root, "bin", "mods");
				Directory.CreateDirectory(mod);
				Directory.CreateDirectory(dlc);
				Directory.CreateDirectory(untouched);
				File.WriteAllText(Path.Combine(mod, "script.ws"), "test");
				File.WriteAllText(Path.Combine(dlc, "bundle.bin"), "test");
				File.WriteAllText(Path.Combine(untouched, "unrelated.ws"), "test");
				var scan = Witcher3ComponentDiskScan.Scan(root);
				Assert.That(scan.IsComplete, Is.True);
				Assert.That(scan.ComponentFolders, Is.EquivalentTo(new[] { @"Mods\modA", @"DLC\dlcA" }));
				Assert.That(scan.GameRelativeFiles, Is.EquivalentTo(new[]
				{
					@"Mods\modA\content\script.ws", @"DLC\dlcA\content\bundle.bin"
				}));
			}
			finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
		}

		[Test]
		public void ExplicitFileLimitReturnsPartialObservationWithoutGuessingAbsence()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-w3r-disk-limit-" + Guid.NewGuid().ToString("N"));
			try
			{
				string dir = Path.Combine(root, "Mods", "modA", "content");
				Directory.CreateDirectory(dir);
				File.WriteAllText(Path.Combine(dir, "a.ws"), "a");
				File.WriteAllText(Path.Combine(dir, "b.ws"), "b");
				var scan = Witcher3ComponentDiskScan.Scan(root, maxFiles: 1);
				Assert.That(scan.IsComplete, Is.False);
				Assert.That(scan.GameRelativeFiles.Count, Is.EqualTo(1));
				Assert.That(scan.Issues.Select(x => x.Kind), Does.Contain(Witcher3ComponentDiskIssueKind.ScanLimitReached));
			}
			finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
		}

		[Test]
		public void EmptyModFolderStillAppearsAsUnmanagedFolderEvidence()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-w3r-empty-dir-" + Guid.NewGuid().ToString("N"));
			try
			{
				Directory.CreateDirectory(Path.Combine(root, "Mods", "modEmpty"));
				var scan = Witcher3ComponentDiskScan.Scan(root);
				Assert.That(scan.IsComplete, Is.True);
				Assert.That(scan.ComponentFolders, Is.EqualTo(new[] { @"Mods\modEmpty" }));
				Assert.That(scan.GameRelativeFiles, Is.Empty);
			}
			finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
		}
	}
}
