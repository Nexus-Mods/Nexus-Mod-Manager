namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    using Nexus.Client.ModManagement;

    using NUnit.Framework;

    [TestFixture]
    public class VirtualLinkIndexTests
    {
        private const string GameRoot = @"C:\Games\Skyrim";
        private static readonly string DataRoot = Path.Combine(GameRoot, "Data");

        [Test]
        public void DeploymentPathIndex_MatchesDataAndGameRootPhysicalEquivalentFiles()
        {
            IVirtualModInfo modInfo = CreateModInfo();
            IVirtualModLink dataLink = CreateLink(modInfo, @"Scripts\Test.pex", ModInstallRoot.Data);
            IVirtualModLink gameRootLink = CreateLink(modInfo, @"Data\Scripts\Test.pex", ModInstallRoot.GameRoot);
            object index = CreateIndex();

            Rebuild(index, new[] { dataLink, gameRootLink }, GetDeploymentKeys);

            List<IVirtualModLink> matches = FindByDeploymentPath(index, DeploymentKey(@"Scripts\Test.pex", ModInstallRoot.Data));

            AssertContainsReference(matches, dataLink);
            AssertContainsReference(matches, gameRootLink);
        }

        [Test]
        public void DeploymentPathIndex_DoesNotMatchDifferentPhysicalDestinations()
        {
            IVirtualModInfo modInfo = CreateModInfo();
            IVirtualModLink dataLink = CreateLink(modInfo, @"Scripts\Test.pex", ModInstallRoot.Data);
            IVirtualModLink gameRootLink = CreateLink(modInfo, @"Scripts\Test.pex", ModInstallRoot.GameRoot);
            object index = CreateIndex();

            Rebuild(index, new[] { dataLink, gameRootLink }, GetDeploymentKeys);

            List<IVirtualModLink> dataMatches = FindByDeploymentPath(index, DeploymentKey(@"Scripts\Test.pex", ModInstallRoot.Data));

            AssertContainsReference(dataMatches, dataLink);
            AssertDoesNotContainReference(dataMatches, gameRootLink);
        }

        [Test]
        public void DeploymentPathIndex_AddAndRemoveUpdateLookup()
        {
            IVirtualModInfo modInfo = CreateModInfo();
            IVirtualModLink link = CreateLink(modInfo, @"Scripts\Test.pex", ModInstallRoot.Data);
            string key = DeploymentKey(@"Scripts\Test.pex", ModInstallRoot.Data);
            object index = CreateIndex();

            Add(index, link, new[] { key });

            AssertContainsReference(FindByDeploymentPath(index, key), link);

            Remove(index, link, new[] { key });

            Assert.That(FindByDeploymentPath(index, key), Is.Empty);
        }

        [Test]
        public void DeploymentPathIndex_RebuildPreservesLookup()
        {
            IVirtualModInfo modInfo = CreateModInfo();
            IVirtualModLink link = CreateLink(modInfo, @"Textures\Armor\A.dds", ModInstallRoot.Data);
            object index = CreateIndex();

            Rebuild(index, new[] { link }, GetDeploymentKeys);

            AssertContainsReference(FindByDeploymentPath(index, DeploymentKey(@"Textures\Armor\A.dds", ModInstallRoot.Data)), link);
        }

        [Test]
        [Timeout(5000)]
        public void DeploymentPathIndex_LookupsDoNotRebuildOrRescanAllLinks()
        {
            const int linkCount = 5000;
            const int lookupCount = 1000;
            IVirtualModInfo modInfo = CreateModInfo();
            List<IVirtualModLink> links = new List<IVirtualModLink>();
            for (int i = 0; i < linkCount; i++)
                links.Add(CreateLink(modInfo, @"Textures\Generated\File" + i + ".dds", ModInstallRoot.Data));

            int keyFactoryCalls = 0;
            object index = CreateIndex();
            Rebuild(index, links, link =>
            {
                keyFactoryCalls++;
                return GetDeploymentKeys(link);
            });

            Assert.AreEqual(linkCount, keyFactoryCalls);

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < lookupCount; i++)
                FindByDeploymentPath(index, DeploymentKey(@"Textures\Generated\Missing" + i + ".dds", ModInstallRoot.Data));
            stopwatch.Stop();

            Assert.AreEqual(linkCount, keyFactoryCalls, "Lookups must not call the deployed-path key factory again.");
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(500), "Indexed lookups should scale with lookup count, not lookup count times all links.");
        }


        private static void AssertContainsReference(IEnumerable<IVirtualModLink> links, IVirtualModLink expected)
        {
            Assert.IsTrue(links.Any(x => ReferenceEquals(x, expected)), "Expected indexed lookup to return the original link reference.");
        }

        private static void AssertDoesNotContainReference(IEnumerable<IVirtualModLink> links, IVirtualModLink unexpected)
        {
            Assert.IsFalse(links.Any(x => ReferenceEquals(x, unexpected)), "Indexed lookup returned a link with a different physical destination.");
        }
        private static IVirtualModInfo CreateModInfo()
        {
            return new VirtualModInfo("1", "1", "Test Mod", "Test Mod.7z", "1.0");
        }

        private static IVirtualModLink CreateLink(IVirtualModInfo modInfo, string virtualPath, ModInstallRoot installRoot)
        {
            return new VirtualModLink(@"Test Mod.7z\" + virtualPath, virtualPath, 0, true, modInfo, installRoot);
        }

        private static IEnumerable<string> GetDeploymentKeys(IVirtualModLink link)
        {
            yield return DeploymentKey(link.VirtualModPath, link.InstallRoot);
        }

        private static string DeploymentKey(string relativePath, ModInstallRoot installRoot)
        {
            string root = installRoot == ModInstallRoot.GameRoot ? GameRoot : DataRoot;
            return Path.GetFullPath(Path.Combine(root, relativePath)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static object CreateIndex()
        {
            Type indexType = GetIndexType();
            return Activator.CreateInstance(indexType, true);
        }

        private static void Rebuild(object index, IEnumerable<IVirtualModLink> links, Func<IVirtualModLink, IEnumerable<string>> keyFactory)
        {
            MethodInfo method = GetIndexType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(x => x.Name == "Rebuild" && x.GetParameters().Length == 2);
            method.Invoke(index, new object[] { links, keyFactory });
        }

        private static void Add(object index, IVirtualModLink link, IEnumerable<string> keys)
        {
            MethodInfo method = GetIndexType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(x => x.Name == "Add" && x.GetParameters().Length == 2);
            method.Invoke(index, new object[] { link, keys });
        }

        private static void Remove(object index, IVirtualModLink link, IEnumerable<string> keys)
        {
            MethodInfo method = GetIndexType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(x => x.Name == "Remove" && x.GetParameters().Length == 2);
            method.Invoke(index, new object[] { link, keys });
        }

        private static List<IVirtualModLink> FindByDeploymentPath(object index, string key)
        {
            MethodInfo method = GetIndexType().GetMethod("FindByDeploymentPath", BindingFlags.Instance | BindingFlags.Public);
            return (List<IVirtualModLink>)method.Invoke(index, new object[] { key });
        }

        private static Type GetIndexType()
        {
            return typeof(VirtualModLink).Assembly.GetType("Nexus.Client.ModManagement.VirtualLinkIndex", true);
        }
    }
}