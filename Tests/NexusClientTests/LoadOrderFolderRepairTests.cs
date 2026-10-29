using System;
using System.IO;
using System.Threading;
using Nexus.Client.ModManagement.LoadOrder;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	/// <summary>Regression source for destructive-operation guards; fixtures are isolated and remain maintainer-run only.</summary>
	[TestFixture]
	public sealed class LoadOrderFolderRepairTests
	{
		/// <summary>Content changes invalidate a cleanup preview even when size and timestamps are unchanged.</summary>
		[Test]
		public void RetainedIdentityUsesContentsRatherThanTimestampHints()
		{
			using (var fixture = new FolderFixture())
			{
				string file = Path.Combine(fixture.Root, "readme.txt"); File.WriteAllText(file, "first");
				DateTime time = File.GetLastWriteTimeUtc(file);
				string before = NativeLoadOrderService.FolderItemIdentity(file, false, CancellationToken.None);
				File.WriteAllText(file, "other"); File.SetLastWriteTimeUtc(file, time);
				Assert.That(NativeLoadOrderService.FolderItemIdentity(file, false, CancellationToken.None), Is.Not.EqualTo(before));
			}
		}

		/// <summary>A misplaced package prevents automatic movement of its parent instead of being flattened or discarded.</summary>
		[Test]
		public void NestedPackageRequiresDeliberateInstallation()
		{
			using (var fixture = new FolderFixture())
			{
				string nested = Path.Combine(fixture.Root, "Nested"); Directory.CreateDirectory(nested);
				string package = Path.Combine(nested, "Example.PAK"); File.WriteAllText(package, "unchanged");
				Assert.Throws<InvalidDataException>(() => NativeLoadOrderService.FolderItemIdentity(fixture.Root, true, CancellationToken.None));
				Assert.That(File.ReadAllText(package), Is.EqualTo("unchanged"));
			}
		}

		/// <summary>Unexpected large content requires manual review instead of an unbounded automatic hash/move.</summary>
		[Test]
		public void OversizedFileCannotEnterAutomaticCleanup()
		{
			using (var fixture = new FolderFixture())
			{
				string file = Path.Combine(fixture.Root, "Large.bin");
				using (var stream = new FileStream(file, FileMode.CreateNew)) stream.SetLength(32 * 1024 * 1024 + 1);
				Assert.Throws<InvalidDataException>(() => NativeLoadOrderService.FolderItemIdentity(file, false, CancellationToken.None));
			}
		}

		/// <summary>Type changes are conflicts, including the empty-file case that is otherwise valid cleanup evidence.</summary>
		[Test]
		public void ChangedEntryTypeCannotMatchReviewedDirectory()
		{
			using (var fixture = new FolderFixture())
			{
				string file = Path.Combine(fixture.Root, "empty.txt"); File.WriteAllBytes(file, new byte[0]);
				Assert.That(NativeLoadOrderService.FolderItemIdentity(file, false, CancellationToken.None).Length, Is.EqualTo(64));
				Assert.Throws<IOException>(() => NativeLoadOrderService.FolderItemIdentity(file, true, CancellationToken.None));
			}
		}

		/// <summary>Cancellation propagates before reading even the first filesystem entry.</summary>
		[Test]
		public void CancellationDoesNotBecomeAReadableEmptyFolder()
		{
			using (var cancellation = new CancellationTokenSource())
			{
				cancellation.Cancel();
				Assert.Throws<OperationCanceledException>(() => NativeLoadOrderService.FolderItemIdentity(Path.Combine(Path.GetTempPath(), "missing-folder"), true, cancellation.Token));
			}
		}

		/// <summary>Owns only the unique directory created for this fixture.</summary>
		private sealed class FolderFixture : IDisposable
		{
			internal string Root { get; } = Path.Combine(Path.GetTempPath(), "nmm-folder-repair-" + Guid.NewGuid().ToString("N"));
			/// <summary>Creates isolated synthetic content, never a game package folder.</summary>
			internal FolderFixture() { Directory.CreateDirectory(Root); }
			/// <summary>Removes this fixture's exact unique temporary tree only.</summary>
			public void Dispose() { Directory.Delete(Root, true); }
		}
	}
}
