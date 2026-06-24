namespace NexusClientTests
{
    using System;
    using System.Drawing;
    using System.IO;

    using Nexus.Client.Games;

    using NUnit.Framework;

    /// <summary>
    /// Exposes the protected SafeExtractIcon helper for testing.
    /// </summary>
    internal sealed class TestLauncher : GameLauncherBase
    {
        public TestLauncher() : base(null, null) { }

        public static Image CallSafeExtractIcon(string path) => SafeExtractIcon(path);

        protected override void SetupCommands() { }
    }

    [TestFixture]
    public class SafeExtractIconTests
    {
        [Test]
        public void NullPath_ReturnsFallbackIcon()
        {
            using (Image result = TestLauncher.CallSafeExtractIcon(null))
            {
                Assert.IsNotNull(result);
            }
        }

        [Test]
        public void EmptyPath_ReturnsFallbackIcon()
        {
            using (Image result = TestLauncher.CallSafeExtractIcon(string.Empty))
            {
                Assert.IsNotNull(result);
            }
        }

        [Test]
        public void NonExistentPath_ReturnsFallbackIcon()
        {
            using (Image result = TestLauncher.CallSafeExtractIcon(@"C:\DoesNotExist\fake.exe"))
            {
                Assert.IsNotNull(result);
            }
        }

        [Test]
        public void PlainTextFileRenamedExe_DoesNotThrow_ReturnsImage()
        {
            // Simulates an exe-like file with no embedded icon resource.
            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
            File.WriteAllText(tempPath, "not a real exe");
            try
            {
                Image result = null;
                Assert.DoesNotThrow(() => result = TestLauncher.CallSafeExtractIcon(tempPath));
                Assert.IsNotNull(result);
                result?.Dispose();
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        [Test]
        public void RealExeWithIcon_ReturnsNonNull()
        {
            // Use the current test runner executable, which always has an embedded icon.
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            Assume.That(File.Exists(exePath), "Test runner exe not found; skipping.");

            Image result = TestLauncher.CallSafeExtractIcon(exePath);
            Assert.IsNotNull(result, "Expected a non-null Image from an exe with an embedded icon.");
            result.Dispose();
        }

        [Test]
        public void CalledRepeatedly_NeverThrows()
        {
            // Regression: calling SafeExtractIcon many times in SetupCommands must not crash.
            for (int i = 0; i < 20; i++)
            {
                Assert.DoesNotThrow(
                    () => TestLauncher.CallSafeExtractIcon(@"C:\NonExistent\tool" + i + ".exe"));
            }
        }
    }
}
