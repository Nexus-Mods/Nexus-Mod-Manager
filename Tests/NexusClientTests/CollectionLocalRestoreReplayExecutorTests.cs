using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement.Scripting;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsGateL")]
	public class CollectionLocalRestoreReplayExecutorTests
	{
		[Test]
		public void ReplaceArtifacts_ReplacesReplayAndRemovesStalePayloadTree()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				string sourcePath = Path.Combine(root, "source.xml");
				string destinationPath = Path.Combine(root, "destination.xml");
				var source = new ScriptedFileSelectionCache(sourcePath);
				source.RecordSelection("archive\\a.dds", "textures\\a.dds");
				source.RecordGeneratedFile("meshes\\generated.nif", Encoding.UTF8.GetBytes("captured-generated"));
				var destination = new ScriptedFileSelectionCache(destinationPath);
				destination.RecordGeneratedFile("textures\\stale.dds", Encoding.UTF8.GetBytes("stale-payload"));
				string stalePayload = Directory.GetFiles(ScriptedFileSelectionCache.GetPayloadDirectoryPath(destinationPath)).Single();

				CollectionLocalRestoreReplayArtifactState desired = CollectionLocalRestoreReplayFileSystem.Capture(sourcePath, CancellationToken.None);
				CollectionLocalRestoreReplayFileSystem.ReplaceArtifacts(sourcePath, destinationPath);
				CollectionLocalRestoreReplayArtifactState live = CollectionLocalRestoreReplayFileSystem.Capture(destinationPath, CancellationToken.None);

				Assert.IsTrue(live.Equals(desired));
				Assert.IsFalse(File.Exists(stalePayload));
				IReadOnlyList<ScriptedReplayOperation> operations = new ScriptedFileSelectionCache(destinationPath).LoadReplayOperations();
				Assert.AreEqual(2, operations.Count);
				Assert.AreEqual(ScriptedReplayOperationKind.GeneratedFile, operations[1].Kind);
				Assert.AreEqual("captured-generated", File.ReadAllText(operations[1].PayloadPath));
			}
			finally
			{
				TryDeleteDirectory(root);
			}
		}

		[Test]
		public void SafeTransition_AllowsOnlyRecognizableDesiredPayloadSubset()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				string desiredPath = Path.Combine(root, "desired.xml");
				string livePath = Path.Combine(root, "live.xml");
				var desiredCache = new ScriptedFileSelectionCache(desiredPath);
				desiredCache.RecordGeneratedFile("textures\\one.dds", Encoding.UTF8.GetBytes("one"));
				desiredCache.RecordGeneratedFile("textures\\two.dds", Encoding.UTF8.GetBytes("two"));
				var liveCache = new ScriptedFileSelectionCache(livePath);
				liveCache.RecordGeneratedFile("textures\\old.dds", Encoding.UTF8.GetBytes("old"));

				CollectionLocalRestoreReplayArtifactState preimage = CollectionLocalRestoreReplayFileSystem.Capture(livePath, CancellationToken.None);
				CollectionLocalRestoreReplayArtifactState desired = CollectionLocalRestoreReplayFileSystem.Capture(desiredPath, CancellationToken.None);

				File.Copy(desiredPath, livePath, true);
				string livePayloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(livePath);
				Directory.Delete(livePayloadDirectory, true);
				Directory.CreateDirectory(livePayloadDirectory);
				string desiredPayloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(desiredPath);
				string firstDesiredPayload = Directory.GetFiles(desiredPayloadDirectory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First();
				File.Copy(firstDesiredPayload, Path.Combine(livePayloadDirectory, Path.GetFileName(firstDesiredPayload)));

				CollectionLocalRestoreReplayArtifactState partial = CollectionLocalRestoreReplayFileSystem.Capture(livePath, CancellationToken.None);
				Assert.IsTrue(CollectionLocalRestoreReplayFileSystem.IsSafeTransition(partial, preimage, desired));

				File.WriteAllText(Path.Combine(livePayloadDirectory, "rogue.bin"), "rogue");
				CollectionLocalRestoreReplayArtifactState unknown = CollectionLocalRestoreReplayFileSystem.Capture(livePath, CancellationToken.None);
				Assert.IsFalse(CollectionLocalRestoreReplayFileSystem.IsSafeTransition(unknown, preimage, desired));
			}
			finally
			{
				TryDeleteDirectory(root);
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-replay-restore-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void TryDeleteDirectory(string path)
		{
			try { if (Directory.Exists(path)) Directory.Delete(path, true); }
			catch { }
		}
	}
}
