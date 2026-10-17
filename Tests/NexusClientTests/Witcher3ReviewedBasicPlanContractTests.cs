using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public class Witcher3ReviewedBasicPlanContractTests
    {
        private interface IReviewedTestGameMode : IGameMode, IReviewedBasicInstallMappingProvider { }

        private static IMod Mod(bool scripted, params string[] paths)
        {
            return InterfaceStub<IMod>.Create((method, arguments) =>
            {
                if (method.Name == "GetFileList") return paths.ToList();
                if (method.Name == "get_HasInstallScript") return scripted;
                if (method.Name == "get_Filename") return "WitcherTest.7z";
                if (method.Name == "get_ModName") return "Witcher Test";
                return null;
            });
        }

        private static IGameMode Mode(Action mapCalled = null)
        {
            return InterfaceStub<IReviewedTestGameMode>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_Name": return "The Witcher 3 Remastered";
                    case "get_ModeId": return "Witcher3Remastered";
                    case "get_PluginDirectory": return @"C:\Games\Witcher3";
                    case "MapBasicInstallFiles":
                        mapCalled?.Invoke();
                        return Witcher3BasicInstallMapping.Map(
                            (IEnumerable<KeyValuePair<string, string>>)args[2], "native:unique:file",
                            "Witcher Test", Witcher3Edition.Remastered, (ModInstallContext)args[1]);
                    case "GetModFormatAdjustedPath":
                        return args[1]; // Already-reviewed paths are used verbatim.
                    default:
                        return null;
                }
            });
        }

        [TestCase(ModInstallMethod.Direct)]
        [TestCase(ModInstallMethod.Virtual)]
        public void CollectionsBasicPlanner_UsesExactlyTheSameReviewedDestinations(ModInstallMethod method)
        {
            var mod = Mod(false, @"pack\Mods\modA\content\a.ws",
                @"pack\DLC\dlcB\content\b.bundle", @"pack\README.md");
            int calls = 0;
            var context = new ModInstallContext(method, ModInstallRoot.Data);
            BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(mod,
                Mode(() => calls++), context, false);

            Assert.That(result.IsSupported, Is.True, result.Message);
            Assert.That(calls, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { @"Mods\modA\content\a.ws", @"DLC\dlcB\content\b.bundle" },
                result.Plan.Files.Select(file => file.DestinationPath).ToArray());
            Assert.That(result.Plan.Files.Select(file => file.DeploymentTarget.Root).Distinct().Single(),
                Is.EqualTo(ModDeploymentRoot.Data));
            if (method == ModInstallMethod.Virtual)
                Assert.That(result.Plan.Files.All(file => file.VirtualStoragePath == file.DestinationPath), Is.True);
            else
                Assert.That(result.Plan.Files.All(file => file.VirtualStoragePath == null), Is.True);
        }

        [Test]
        public void UnsupportedWitcherPayload_IsReportedAsUnsupportedNotPartiallyPlanned()
        {
            var mod = Mod(false, @"Mods\modA\content\a.ws", @"bin\config\r4game\user_config_matrix\pc\hud.xml");
            BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(mod, Mode(),
                new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), false);
            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.ReviewedGameLayoutRejected));
            Assert.That(result.Plan, Is.Null);
        }

        [Test]
        public void ScriptedWitcherArchive_IsRejectedBeforeMapping()
        {
            int calls = 0;
            var mod = Mod(true, @"Mods\modA\content\a.ws");
            BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(mod,
                Mode(() => calls++), new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), false);
            Assert.That(result.IsSupported, Is.False);
            Assert.That(result.UnsupportedReason, Is.EqualTo(BasicInstallPlanUnsupportedReasonKind.ReviewedGameLayoutRejected));
            Assert.That(calls, Is.EqualTo(0));
        }
    }
}
