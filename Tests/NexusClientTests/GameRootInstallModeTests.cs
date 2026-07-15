namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Remoting.Messaging;
    using System.Runtime.Remoting.Proxies;

    using Nexus.Client.Games;
    using Nexus.Client.ModManagement;
    using Nexus.Client.Mods;

    using NUnit.Framework;

    [TestFixture]
    public class GameRootInstallModeTests
    {
        [Test]
        public void GameRootWrapper_StripsSkseWrapper()
        {
            List<KeyValuePair<string, string>> files = CreateFiles(
                @"skse64_2_02_06_gog\skse64_loader.exe",
                @"skse64_2_02_06_gog\skse64_1_6_1179.dll",
                @"skse64_2_02_06_gog\Data\Scripts\Test.pex");

            List<KeyValuePair<string, string>> normalized = StripGameRootWrapper(files);

            CollectionAssert.AreEqual(
                new[]
                {
                    "skse64_loader.exe",
                    "skse64_1_6_1179.dll",
                    @"Data\Scripts\Test.pex"
                },
                ExtractDestinations(normalized));
        }

        [Test]
        public void GameRootWrapper_DoesNotStripAlreadyRootedArchive()
        {
            List<KeyValuePair<string, string>> files = CreateFiles(
                "skse64_loader.exe",
                @"Data\Scripts\Test.pex");

            List<KeyValuePair<string, string>> normalized = StripGameRootWrapper(files);

            CollectionAssert.AreEqual(
                new[] { "skse64_loader.exe", @"Data\Scripts\Test.pex" },
                ExtractDestinations(normalized));
        }

        [Test]
        public void GameRootWrapper_DoesNotStripArbitraryCommonFolder()
        {
            List<KeyValuePair<string, string>> files = CreateFiles(@"Wrapper\arbitrary.txt");

            List<KeyValuePair<string, string>> normalized = StripGameRootWrapper(files);

            CollectionAssert.AreEqual(new[] { @"Wrapper\arbitrary.txt" }, ExtractDestinations(normalized));
        }

        [Test]
        public void GameRootWrapper_DoesNotStripMultipleTopLevelEntries()
        {
            List<KeyValuePair<string, string>> files = CreateFiles(
                @"FolderA\file.dll",
                @"FolderB\file.txt");

            List<KeyValuePair<string, string>> normalized = StripGameRootWrapper(files);

            CollectionAssert.AreEqual(
                new[] { @"FolderA\file.dll", @"FolderB\file.txt" },
                ExtractDestinations(normalized));
        }


        [Test]
        public void GameRootProductionPath_StripsWrapperAndIgnoresDataAdjustedDestinationValues()
        {
            List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(@"skse64_2_02_06_gog\skse64_loader.exe", null),
                new KeyValuePair<string, string>(@"skse64_2_02_06_gog\skse64_1_6_1179.dll", null),
                new KeyValuePair<string, string>(@"skse64_2_02_06_gog\Data\Scripts\Test.pex", @"Scripts\Test.pex")
            };

            List<KeyValuePair<string, string>> normalized = StripGameRootWrapper(files);

            CollectionAssert.AreEqual(
                new[]
                {
                    "skse64_loader.exe",
                    "skse64_1_6_1179.dll",
                    @"Data\Scripts\Test.pex"
                },
                ExtractDestinations(normalized));

            Assert.AreEqual(@"skse64_2_02_06_gog\skse64_loader.exe", normalized[0].Key);
            Assert.AreEqual(@"skse64_2_02_06_gog\skse64_1_6_1179.dll", normalized[1].Key);
            Assert.AreEqual(@"skse64_2_02_06_gog\Data\Scripts\Test.pex", normalized[2].Key);
            CollectionAssert.AreEqual(
                new[]
                {
                    "skse64_loader.exe",
                    "skse64_1_6_1179.dll",
                    @"Data\Scripts\Test.pex"
                },
                BuildGameRootInstallDestinations(files));
        }

        [Test]
        public void GameRootProductionPath_PreservesDataFolderWhenExistingDestinationWasDataAdjusted()
        {
            List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(@"Data\Scripts\Test.pex", @"Scripts\Test.pex")
            };

            List<KeyValuePair<string, string>> normalized = StripGameRootWrapper(files);

            CollectionAssert.AreEqual(new[] { @"Data\Scripts\Test.pex" }, ExtractDestinations(normalized));
            CollectionAssert.AreEqual(new[] { @"Data\Scripts\Test.pex" }, BuildGameRootInstallDestinations(files));
        }


        [Test]
        public void GameRootBasicInstall_UsesStrippedWrapperPathForRootFiles()
        {
            List<string> archiveFiles = new List<string>
            {
                @"skse64_2_02_06_gog\skse64_loader.exe",
                @"skse64_2_02_06_gog\Data\Scripts\Test.pex"
            };
            RecordingFileInstaller fileInstaller = new RecordingFileInstaller();
            RecordingModLinkInstaller linkInstaller = new RecordingModLinkInstaller();
            IMod mod = CreateMod(archiveFiles);
            IGameMode gameMode = CreateGameMode();
            IVirtualModActivator virtualModActivator = CreateVirtualModActivator(linkInstaller);
            BasicInstallTask task = new BasicInstallTask(mod, gameMode, fileInstaller, null, virtualModActivator, false, null, null, ModInstallRoot.GameRoot);

            InvokeDoWork(task);

            Assert.AreEqual(2, fileInstaller.InstallCalls.Count);
            Assert.AreEqual(@"skse64_2_02_06_gog\skse64_loader.exe", fileInstaller.InstallCalls[0].SourcePath);
            Assert.AreEqual(@"C:\VirtualInstall\SKSE64\skse64_loader.exe", fileInstaller.InstallCalls[0].InstallPath);
            Assert.AreEqual(@"skse64_2_02_06_gog\Data\Scripts\Test.pex", fileInstaller.InstallCalls[1].SourcePath);
            Assert.AreEqual(@"C:\VirtualInstall\SKSE64\Data\Scripts\Test.pex", fileInstaller.InstallCalls[1].InstallPath);

            Assert.AreEqual(2, linkInstaller.LinkCalls.Count);
            Assert.AreEqual("skse64_loader.exe", linkInstaller.LinkCalls[0].BaseFilePath);
            Assert.AreEqual(@"C:\VirtualInstall\SKSE64\skse64_loader.exe", linkInstaller.LinkCalls[0].SourceFile);
            Assert.AreEqual(ModInstallRoot.GameRoot, linkInstaller.LinkCalls[0].InstallRoot);
            Assert.AreEqual(@"Data\Scripts\Test.pex", linkInstaller.LinkCalls[1].BaseFilePath);
            Assert.AreEqual(@"C:\VirtualInstall\SKSE64\Data\Scripts\Test.pex", linkInstaller.LinkCalls[1].SourceFile);
            Assert.AreEqual(ModInstallRoot.GameRoot, linkInstaller.LinkCalls[1].InstallRoot);
        }

        [Test]
        public void GameRootPath_PreservesDataFolderAsRelativePath()
        {
            Assert.AreEqual(@"Data\Scripts\Test.pex", GetAdjustedGameRootPath(@"Data\Scripts\Test.pex"));
        }

        [TestCase(@"..\outside.dll")]
        [TestCase(@"Data\..\..\outside.dll")]
        [TestCase(@"C:\outside.dll")]
        [TestCase(@"\\server\share\outside.dll")]
        public void GameRootPath_RejectsPathsOutsideGameRoot(string path)
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => GetAdjustedGameRootPath(path));

            Assert.IsInstanceOf<InvalidDataException>(exception.InnerException);
        }

        private static IMod CreateMod(List<string> files)
        {
            return InterfaceProxy<IMod>.Create(new Dictionary<string, Func<object[], object>>
            {
                { "get_Filename", args => @"C:\Mods\SKSE64.7z" },
                { "get_FileName", args => "SKSE64.7z" },
                { "get_DownloadId", args => string.Empty },
                { "GetFileList", args => files }
            });
        }

        private static IGameMode CreateGameMode()
        {
            return InterfaceProxy<IGameMode>.Create(new Dictionary<string, Func<object[], object>>
            {
                { "get_Name", args => "Test Game" },
                { "get_RequiresSpecialFileInstallation", args => false },
                { "IsSpecialFile", args => false },
                { "get_RequiresModFileMerge", args => false },
                { "get_MergedFileName", args => "merged.file" },
                { "get_PluginDirectory", args => "Data" },
                { "HardlinkRequiredFilesType", args => false }
            });
        }

        private static IVirtualModActivator CreateVirtualModActivator(IModLinkInstaller linkInstaller)
        {
            return InterfaceProxy<IVirtualModActivator>.Create(new Dictionary<string, Func<object[], object>>
            {
                { "GetModLinkInstaller", args => linkInstaller },
                { "get_VirtualPath", args => @"C:\VirtualInstall" },
                { "get_HDLinkFolder", args => @"C:\HDLink" },
                { "get_MultiHDMode", args => false },
                { "get_DisableLinkCreation", args => false },
                { "SaveList", args => true }
            });
        }

        private static void InvokeDoWork(BasicInstallTask task)
        {
            MethodInfo method = typeof(BasicInstallTask).GetMethod("DoWork", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(object[]) }, null);
            method.Invoke(task, new object[] { new object[0] });
        }
        private static List<KeyValuePair<string, string>> CreateFiles(params string[] paths)
        {
            List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
            foreach (string path in paths)
                files.Add(new KeyValuePair<string, string>(path, null));

            return files;
        }

        private static string[] ExtractDestinations(List<KeyValuePair<string, string>> files)
        {
            string[] destinations = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
                destinations[i] = files[i].Value ?? files[i].Key;

            return destinations;
        }

        private static string[] BuildGameRootInstallDestinations(List<KeyValuePair<string, string>> files)
        {
            List<KeyValuePair<string, string>> normalized = StripGameRootWrapper(files);
            string[] destinations = new string[normalized.Count];
            for (int i = 0; i < normalized.Count; i++)
            {
                string destination = normalized[i].Value;
                if (string.IsNullOrWhiteSpace(destination))
                    destination = normalized[i].Key;

                destinations[i] = GetAdjustedGameRootPath(destination);
            }

            return destinations;
        }
        private static List<KeyValuePair<string, string>> StripGameRootWrapper(List<KeyValuePair<string, string>> files)
        {
            BasicInstallTask task = CreateGameRootInstallTask();
            MethodInfo method = typeof(BasicInstallTask).GetMethod("StripGameRootWrapperFolder", BindingFlags.Instance | BindingFlags.NonPublic);

            return (List<KeyValuePair<string, string>>)method.Invoke(task, new object[] { files });
        }

        private static string GetAdjustedGameRootPath(string path)
        {
            BasicInstallTask task = CreateGameRootInstallTask();
            MethodInfo method = typeof(BasicInstallTask).GetMethod("GetAdjustedPath", BindingFlags.Instance | BindingFlags.NonPublic);

            return (string)method.Invoke(task, new object[] { path, ModPathContext.GameInstall });
        }

        private static BasicInstallTask CreateGameRootInstallTask()
        {
            return new BasicInstallTask(null, null, null, null, null, false, null, null, ModInstallRoot.GameRoot);
        }

        private sealed class RecordingFileInstaller : IModFileInstaller
        {
            public readonly List<InstallCall> InstallCalls = new List<InstallCall>();

            public List<string> InstallErrors { get; } = new List<string>();

            public bool InstallFileFromMod(string p_strModFilePath, string p_strInstallPath)
            {
                InstallCalls.Add(new InstallCall(p_strModFilePath, p_strInstallPath));
                return true;
            }

            public bool GenerateDataFile(string p_strPath, byte[] p_bteData)
            {
                throw new NotImplementedException();
            }

            public bool PluginCheck(string p_strPath, bool p_booRemove)
            {
                return false;
            }

            public bool UninstallDataFile(string p_strPath)
            {
                throw new NotImplementedException();
            }

            public void FinalizeInstall()
            {
            }
        }

        private sealed class RecordingModLinkInstaller : IModLinkInstaller
        {
            public readonly List<LinkCall> LinkCalls = new List<LinkCall>();

            public string AddFileLink(IMod mod, string baseFilePath, string sourceFile, bool isSwitching)
            {
                return AddFileLink(mod, baseFilePath, sourceFile, isSwitching, false, ModInstallRoot.Default);
            }

            public string AddFileLink(IMod mod, string baseFilePath, string sourceFile, bool isSwitching, bool handlePlugin)
            {
                return AddFileLink(mod, baseFilePath, sourceFile, isSwitching, handlePlugin, ModInstallRoot.Default);
            }

            public string AddFileLink(IMod mod, string baseFilePath, string sourceFile, bool isSwitching, bool handlePlugin, ModInstallRoot installRoot)
            {
                LinkCalls.Add(new LinkCall(baseFilePath, sourceFile, installRoot));
                return string.Empty;
            }
        }

        private sealed class InstallCall
        {
            public InstallCall(string sourcePath, string installPath)
            {
                SourcePath = sourcePath;
                InstallPath = installPath;
            }

            public string SourcePath { get; private set; }
            public string InstallPath { get; private set; }
        }

        private sealed class LinkCall
        {
            public LinkCall(string baseFilePath, string sourceFile, ModInstallRoot installRoot)
            {
                BaseFilePath = baseFilePath;
                SourceFile = sourceFile;
                InstallRoot = installRoot;
            }

            public string BaseFilePath { get; private set; }
            public string SourceFile { get; private set; }
            public ModInstallRoot InstallRoot { get; private set; }
        }

        private sealed class InterfaceProxy<T> : RealProxy where T : class
        {
            private readonly Dictionary<string, Func<object[], object>> m_dicHandlers;

            private InterfaceProxy(Dictionary<string, Func<object[], object>> handlers)
                : base(typeof(T))
            {
                m_dicHandlers = handlers ?? new Dictionary<string, Func<object[], object>>();
            }

            public static T Create(Dictionary<string, Func<object[], object>> handlers)
            {
                return (T)new InterfaceProxy<T>(handlers).GetTransparentProxy();
            }

            public override IMessage Invoke(IMessage msg)
            {
                IMethodCallMessage methodCall = (IMethodCallMessage)msg;
                Func<object[], object> handler;
                object returnValue = null;

                if (m_dicHandlers.TryGetValue(methodCall.MethodName, out handler))
                    returnValue = handler(methodCall.Args);
                else
                    returnValue = GetDefaultReturnValue(((MethodInfo)methodCall.MethodBase).ReturnType);

                return new ReturnMessage(returnValue, null, 0, methodCall.LogicalCallContext, methodCall);
            }

            private static object GetDefaultReturnValue(Type type)
            {
                if (type == typeof(void))
                    return null;

                if (type == typeof(string))
                    return string.Empty;

                if (type.IsValueType)
                    return Activator.CreateInstance(type);

                return null;
            }
        }
    }
}
