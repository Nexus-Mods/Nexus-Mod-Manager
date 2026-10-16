using System;
using System.IO;
using System.Reflection;
using Nexus.Client.Games;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>Guards Steam discovery without requiring Steam, registry changes, or an installed game.</summary>
    [TestFixture]
    public class SteamInstallationPathDetectorTests
    {
        /// <summary>An empty primary folder must not hide the real install in another Steam library.</summary>
        [TestCase(true)]
        [TestCase(false)]
        public void Discovery_SkipsStalePrimaryFolderAndReadsBothLibraryLayouts(bool modern)
        {
            string root = CreateRoot();
            try
            {
                string primary = Path.Combine(root, "Steam");
                string secondary = Path.Combine(root, "Library");
                Directory.CreateDirectory(Path.Combine(primary, "steamapps", "common", "Skyrim Special Edition"));
                string installed = CreateGame(secondary, "Skyrim Special Edition");
                // No apps list is required, and path need not be the object's first property.
                WriteLibraries(primary, modern
                    ? "\"1\" { \"label\" \"Library\" \"path\" \"" + Escape(secondary) + "\" }"
                    : "\"1\" \"" + Escape(secondary) + "\"");
                Assert.That(Find(primary, "Skyrim Special Edition"), Is.EqualTo(installed));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Steam's exact manifest folder is authoritative even when the legacy game hint differs.</summary>
        [Test]
        public void Discovery_UsesManifestInstallDirectory()
        {
            string root = CreateRoot();
            try
            {
                string installed = CreateGame(root, "Custom Skyrim Folder");
                WriteManifest(root, "489830", "Custom Skyrim Folder");
                Assert.That(Find(root, "SkyrimSE"), Is.EqualTo(installed));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Missing library metadata preserves detection of a valid ordinary primary install.</summary>
        [Test]
        public void Discovery_PreservesDefaultLibraryFallback()
        {
            string root = CreateRoot();
            try
            {
                string installed = CreateGame(root, "Skyrim Special Edition");
                Assert.That(Find(root, "Skyrim Special Edition"), Is.EqualTo(installed));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>A folder with no executable is not an installed game.</summary>
        [Test]
        public void Discovery_RejectsStaleFolderWithoutExecutable()
        {
            string root = CreateRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "steamapps", "common", "Skyrim Special Edition"));
                Assert.That(Find(root, "Skyrim Special Edition"), Is.Null);
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>A broken or unrelated manifest must not prevent inspecting later libraries.</summary>
        [TestCase("garbage")]
        [TestCase("\"AppState\" { \"appid\" \"999\" \"installdir\" \"Wrong\" }")]
        public void Discovery_ContinuesPastInvalidManifest(string manifest)
        {
            string root = CreateRoot();
            try
            {
                string primary = Path.Combine(root, "Steam");
                string secondary = Path.Combine(root, "Library");
                WriteLibraries(primary, "\"1\" { \"path\" \"" + Escape(secondary) + "\" }");
                File.WriteAllText(Path.Combine(primary, "steamapps", "appmanifest_489830.acf"), manifest);
                string installed = CreateGame(secondary, "Skyrim Special Edition");
                Assert.That(Find(primary, "Skyrim Special Edition"), Is.EqualTo(installed));
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Manifest directory traversal cannot identify an installation outside Steam's common directory.</summary>
        [Test]
        public void Discovery_RejectsEscapingManifestDirectory()
        {
            string root = CreateRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "steamapps", "common"));
                File.WriteAllBytes(Path.Combine(root, "SkyrimSE.exe"), new byte[] { 1 });
                WriteManifest(root, "489830", @"..\..");
                Assert.That(Find(root, "Missing folder"), Is.Null);
            }
            finally { Directory.Delete(root, true); }
        }

        /// <summary>Invokes filesystem discovery directly so regression coverage never reads the user's Steam registry.</summary>
        private static string Find(string steamPath, string folder)
        {
            MethodInfo method = typeof(SteamInstallationPathDetector).GetMethod("FindInSteamLibraries", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(null, new object[] { steamPath, "489830", folder, "SkyrimSE.exe" });
        }

        /// <summary>Creates a disposable fake Steam library and game executable.</summary>
        private static string CreateGame(string library, string folder)
        {
            string path = Path.Combine(library, "steamapps", "common", folder);
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "SkyrimSE.exe"), new byte[] { 1 });
            return path;
        }

        /// <summary>Writes one small fixture using Steam's existing VDF syntax.</summary>
        private static void WriteLibraries(string primary, string entries)
        {
            Directory.CreateDirectory(Path.Combine(primary, "steamapps"));
            File.WriteAllText(Path.Combine(primary, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { " + entries + " }");
        }

        /// <summary>Writes the app identity and actual installation folder recorded by Steam.</summary>
        private static void WriteManifest(string library, string appId, string folder)
        {
            Directory.CreateDirectory(Path.Combine(library, "steamapps"));
            File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_489830.acf"),
                "\"AppState\" { \"appid\" \"" + appId + "\" \"installdir\" \"" + Escape(folder) + "\" }");
        }

        /// <summary>Escapes Windows separators in a quoted VDF value.</summary>
        private static string Escape(string value) { return value.Replace("\\", "\\\\"); }

        /// <summary>Creates an isolated fixture directory.</summary>
        private static string CreateRoot()
        {
            string path = Path.Combine(Path.GetTempPath(), "nmm-steam-discovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
