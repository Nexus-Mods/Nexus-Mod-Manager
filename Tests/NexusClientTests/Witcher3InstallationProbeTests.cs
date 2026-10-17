using System;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Characterizes the filesystem adapter independently from any real Witcher installation.</summary>
	[TestFixture]
	public class Witcher3InstallationProbeTests
	{
		[Test]
		public void Inspect_MissingGameDirectory_NeverCreatesIt()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-w3-missing-" + Guid.NewGuid().ToString("N"));
			Witcher3InstallationObservation result = Witcher3InstallationProbe.Inspect(root);
			Assert.That(result.GameRootExists, Is.False);
			Assert.That(result.Edition.Edition, Is.EqualTo(Witcher3Edition.Ambiguous));
			Assert.That(Directory.Exists(root), Is.False);
		}

		[Test]
		public void Inspect_Dx11Fixture_IsReadOnlyAndNeverRemastered()
		{
			string root = CreateDirectoryWithExecutable("x64");
			try
			{
				Witcher3InstallationObservation result = Witcher3InstallationProbe.Inspect(root + Path.DirectorySeparatorChar);
				Assert.That(result.GameRootExists, Is.True);
				Assert.That(result.NormalizedGameRoot, Is.EqualTo(Path.GetFullPath(root)));
				Assert.That(result.Evidence.HasDx11Executable, Is.True);
				Assert.That(result.Evidence.HasDx12Executable, Is.False);
				Assert.That(result.Edition.Edition, Is.Not.EqualTo(Witcher3Edition.Remastered));
				Assert.That(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length, Is.EqualTo(1));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Inspect_UnversionedDx12Only_DoesNotPretendToBeVersionFive()
		{
			string root = CreateDirectoryWithExecutable("x64_dx12");
			try
			{
				Witcher3InstallationObservation result = Witcher3InstallationProbe.Inspect(root);
				Assert.That(result.Evidence.HasDx11Executable, Is.False);
				Assert.That(result.Evidence.HasDx12Executable, Is.True);
				Assert.That(result.Edition.Edition, Is.EqualTo(Witcher3Edition.Ambiguous));
				Assert.That(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Single(),
					Does.EndWith("witcher3.exe"));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Inspect_RelativeOrEmptyRoot_IsRejected()
		{
			Assert.Throws<ArgumentException>(() => Witcher3InstallationProbe.Inspect(""));
			Assert.Throws<ArgumentException>(() => Witcher3InstallationProbe.Inspect("relative-game-folder"));
		}

		private static string CreateDirectoryWithExecutable(string executableDirectory)
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-w3-probe-" + Guid.NewGuid().ToString("N"));
			string bin = Path.Combine(root, "bin", executableDirectory);
			Directory.CreateDirectory(bin);
			File.WriteAllBytes(Path.Combine(bin, "witcher3.exe"), new byte[] { 1, 2, 3 });
			return root;
		}
	}
}
