namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Remoting.Messaging;
    using System.Runtime.Remoting.Proxies;
    using System.Xml.Linq;

    using Nexus.Client;
    using Nexus.Client.Games;
    using Nexus.Client.ModManagement;
    using Nexus.Client.ModManagement.Scripting;
    using Nexus.Client.ModManagement.Scripting.XmlScript;
    using Nexus.Client.Mods;
    using Nexus.Client.PluginManagement;
    using Nexus.Client.Plugins;
    using Nexus.Client.Util.Collections;

    /// <summary>
    /// Provides lightweight interface stubs for characterization tests without introducing a mocking-framework dependency.
    /// </summary>
    /// <typeparam name="T">The interface type exposed by the stub.</typeparam>
    internal sealed class InterfaceStub<T> : RealProxy where T : class
    {
        private readonly Func<MethodInfo, object[], object> m_fncHandler;

        /// <summary>
        /// Initializes a transparent proxy that delegates interface calls to the supplied handler.
        /// </summary>
        /// <param name="p_fncHandler">The handler used to process interface method calls.</param>
        public InterfaceStub(Func<MethodInfo, object[], object> p_fncHandler)
            : base(typeof(T))
        {
            m_fncHandler = p_fncHandler ?? throw new ArgumentNullException(nameof(p_fncHandler));
        }

        /// <summary>
        /// Creates a transparent proxy for the requested interface type.
        /// </summary>
        /// <param name="p_fncHandler">The handler used to process interface method calls.</param>
        /// <returns>A transparent proxy implementing <typeparamref name="T"/>.</returns>
        public static T Create(Func<MethodInfo, object[], object> p_fncHandler)
        {
            return (T)new InterfaceStub<T>(p_fncHandler).GetTransparentProxy();
        }

        /// <summary>
        /// Dispatches a proxied interface call to the configured handler and returns the resulting value to the caller.
        /// </summary>
        /// <param name="p_msgMessage">The remoting message describing the interface call.</param>
        /// <returns>A remoting response containing either the handler result or the raised exception.</returns>
        public override IMessage Invoke(IMessage p_msgMessage)
        {
            IMethodCallMessage mcmCall = (IMethodCallMessage)p_msgMessage;
            MethodInfo mifMethod = (MethodInfo)mcmCall.MethodBase;

            try
            {
                object objResult = m_fncHandler(mifMethod, mcmCall.Args);
                if ((objResult == null) && mifMethod.ReturnType.IsValueType && (mifMethod.ReturnType != typeof(void)))
                    objResult = Activator.CreateInstance(mifMethod.ReturnType);

                return new ReturnMessage(objResult, null, 0, mcmCall.LogicalCallContext, mcmCall);
            }
            catch (Exception ex)
            {
                return new ReturnMessage(ex, mcmCall);
            }
        }
    }

    /// <summary>
    /// Records file-installer calls made by scripted installer proxies while preserving configurable return values.
    /// </summary>
    internal sealed class RecordingModFileInstaller : IModFileInstaller
    {
        /// <summary>
        /// Gets or sets the result returned by archive-file installation requests.
        /// </summary>
        public bool InstallResult { get; set; } = true;

        /// <summary>
        /// Gets the last archive source path supplied to the installer.
        /// </summary>
        public string LastModFilePath { get; private set; }

        /// <summary>
        /// Gets the last staging path supplied to the installer.
        /// </summary>
        public string LastInstallPath { get; private set; }

        /// <summary>
        /// Gets the last generated-file staging path supplied to the installer.
        /// </summary>
        public string LastGeneratedPath { get; private set; }

        /// <summary>
        /// Gets the last generated-file payload supplied to the installer.
        /// </summary>
        public byte[] LastGeneratedData { get; private set; }

        /// <summary>
        /// Gets the number of archive-file installation requests received by the installer.
        /// </summary>
        public int InstallCallCount { get; private set; }

        /// <summary>
        /// Gets the number of generated-file requests received by the installer.
        /// </summary>
        public int GenerateCallCount { get; private set; }

        /// <summary>
        /// Records an archive-file installation request.
        /// </summary>
        /// <param name="p_strModFilePath">The source path inside the mod archive.</param>
        /// <param name="p_strInstallPath">The physical staging path selected by the caller.</param>
        /// <returns>The configured installation result.</returns>
        public bool InstallFileFromMod(string p_strModFilePath, string p_strInstallPath)
        {
            InstallCallCount++;
            LastModFilePath = p_strModFilePath;
            LastInstallPath = p_strInstallPath;
            return InstallResult;
        }

        /// <summary>
        /// Records a generated-file request.
        /// </summary>
        /// <param name="p_strPath">The physical staging path selected by the caller.</param>
        /// <param name="p_bteData">The generated file contents.</param>
        /// <returns><c>true</c> for characterization purposes.</returns>
        public bool GenerateDataFile(string p_strPath, byte[] p_bteData)
        {
            GenerateCallCount++;
            LastGeneratedPath = p_strPath;
            LastGeneratedData = p_bteData;
            return true;
        }

        /// <summary>
        /// Returns a neutral plugin-check result because plugin registration is not exercised by these tests.
        /// </summary>
        /// <param name="p_strPath">The path supplied by the caller.</param>
        /// <param name="p_booRemove">Whether the caller is removing the file.</param>
        /// <returns><c>false</c>.</returns>
        public bool PluginCheck(string p_strPath, bool p_booRemove)
        {
            return false;
        }

        /// <summary>
        /// Returns a neutral uninstall result because uninstall behavior is outside this characterization fixture.
        /// </summary>
        /// <param name="p_strPath">The path supplied by the caller.</param>
        /// <returns><c>false</c>.</returns>
        public bool UninstallDataFile(string p_strPath)
        {
            return false;
        }

        /// <summary>
        /// Performs no work because installation finalization is outside this characterization fixture.
        /// </summary>
        public void FinalizeInstall()
        {
        }

        /// <summary>
        /// Gets an empty installation-error collection.
        /// </summary>
        public List<string> InstallErrors => new List<string>();
    }

    /// <summary>
    /// Records data-file queries made by script APIs and returns configurable results.
    /// </summary>
    internal sealed class RecordingDataFileUtil : IDataFileUtil
    {
        /// <summary>
        /// Gets the most recent path queried through the data-file utility.
        /// </summary>
        public string LastPath { get; private set; }

        /// <summary>
        /// Gets or sets the existence result returned by <see cref="DataFileExists"/>.
        /// </summary>
        public bool ExistsResult { get; set; }

        /// <summary>
        /// Gets or sets the data returned by <see cref="GetExistingDataFile"/>.
        /// </summary>
        public byte[] ExistingData { get; set; }

        /// <summary>
        /// Gets or sets the file list returned by <see cref="GetExistingDataFileList"/>.
        /// </summary>
        public string[] ExistingFiles { get; set; } = new string[0];

        /// <summary>
        /// Records the path validated by the caller.
        /// </summary>
        /// <param name="p_strPath">The path to validate.</param>
        public void AssertFilePathIsSafe(string p_strPath)
        {
            LastPath = p_strPath;
        }

        /// <summary>
        /// Records an existence query and returns the configured result.
        /// </summary>
        /// <param name="p_strPath">The path whose existence is queried.</param>
        /// <returns>The configured existence result.</returns>
        public bool DataFileExists(string p_strPath)
        {
            LastPath = p_strPath;
            return ExistsResult;
        }

        /// <summary>
        /// Records a file-list query and returns the configured result.
        /// </summary>
        /// <param name="p_strPath">The path whose files are queried.</param>
        /// <param name="p_strPattern">The file pattern supplied by the caller.</param>
        /// <param name="p_booAllFolders">Whether subdirectories should be included.</param>
        /// <returns>The configured file list.</returns>
        public string[] GetExistingDataFileList(string p_strPath, string p_strPattern, bool p_booAllFolders)
        {
            LastPath = p_strPath;
            return ExistingFiles;
        }

        /// <summary>
        /// Records a file-content query and returns the configured data.
        /// </summary>
        /// <param name="p_strPath">The path whose data is requested.</param>
        /// <returns>The configured file contents.</returns>
        public byte[] GetExistingDataFile(string p_strPath)
        {
            LastPath = p_strPath;
            return ExistingData;
        }
    }

    /// <summary>
    /// Provides an in-memory INI implementation that exposes the immediate read-after-write behavior used by scripts today.
    /// </summary>
    internal sealed class RecordingIniInstaller : IIniInstaller
    {
        private readonly Dictionary<string, string> m_dicValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Retrieves the current in-memory value for the requested INI key.
        /// </summary>
        /// <param name="p_strSettingsFileName">The INI file name.</param>
        /// <param name="p_strSection">The INI section.</param>
        /// <param name="p_strKey">The INI key.</param>
        /// <returns>The current value, or <c>null</c> when no value has been recorded.</returns>
        public string GetIniString(string p_strSettingsFileName, string p_strSection, string p_strKey)
        {
            string strValue;
            return m_dicValues.TryGetValue(GetKey(p_strSettingsFileName, p_strSection, p_strKey), out strValue) ? strValue : null;
        }

        /// <summary>
        /// Retrieves the current in-memory integer value for the requested INI key.
        /// </summary>
        /// <param name="p_strSettingsFileName">The INI file name.</param>
        /// <param name="p_strSection">The INI section.</param>
        /// <param name="p_strKey">The INI key.</param>
        /// <returns>The parsed integer value, or zero when the value is absent or not numeric.</returns>
        public int GetIniInt(string p_strSettingsFileName, string p_strSection, string p_strKey)
        {
            int intValue;
            return Int32.TryParse(GetIniString(p_strSettingsFileName, p_strSection, p_strKey), out intValue) ? intValue : 0;
        }

        /// <summary>
        /// Updates the in-memory INI value immediately and reports a successful edit.
        /// </summary>
        /// <param name="p_strSettingsFileName">The INI file name.</param>
        /// <param name="p_strSection">The INI section.</param>
        /// <param name="p_strKey">The INI key.</param>
        /// <param name="p_strValue">The value to store.</param>
        /// <returns><c>true</c>.</returns>
        public bool EditIni(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
        {
            m_dicValues[GetKey(p_strSettingsFileName, p_strSection, p_strKey)] = p_strValue;
            return true;
        }

        /// <summary>
        /// Removes the requested key from the in-memory INI state.
        /// </summary>
        /// <param name="p_strSettingsFileName">The INI file name.</param>
        /// <param name="p_strSection">The INI section.</param>
        /// <param name="p_strKey">The INI key.</param>
        public void UneditIni(string p_strSettingsFileName, string p_strSection, string p_strKey)
        {
            m_dicValues.Remove(GetKey(p_strSettingsFileName, p_strSection, p_strKey));
        }

        /// <summary>
        /// Performs no work because INI finalization is outside this characterization fixture.
        /// </summary>
        public void FinalizeInstall()
        {
        }

        /// <summary>
        /// Builds a stable dictionary key for an INI file, section, and value name.
        /// </summary>
        /// <param name="p_strSettingsFileName">The INI file name.</param>
        /// <param name="p_strSection">The INI section.</param>
        /// <param name="p_strKey">The INI key.</param>
        /// <returns>A case-insensitive composite key.</returns>
        private static string GetKey(string p_strSettingsFileName, string p_strSection, string p_strKey)
        {
            return String.Concat(p_strSettingsFileName, "\0", p_strSection, "\0", p_strKey);
        }
    }

    /// <summary>
    /// Owns an isolated temporary directory used by filesystem-based characterization tests.
    /// </summary>
    internal sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>
        /// Initializes a unique temporary directory for the current test.
        /// </summary>
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NMMCE-ScriptedInstallerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>
        /// Gets the full path of the temporary directory.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Removes the temporary directory and all files created by the test.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }

    /// <summary>
    /// Exposes the protected scripted replay-cache methods of <see cref="ModInstaller"/> for regression testing.
    /// </summary>
    internal sealed class TestableModInstaller : ModInstaller
    {
        /// <summary>
        /// Initializes a minimal installer instance required to exercise scripted replay-cache behavior.
        /// </summary>
        /// <param name="p_modMod">The mod associated with the replay cache.</param>
        /// <param name="p_gmdGameMode">The game mode supplying installation paths.</param>
        /// <param name="p_prmProfileManager">The optional profile manager supplying a profile-specific replay cache.</param>
        public TestableModInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IProfileManager p_prmProfileManager)
            : base(p_modMod, p_gmdGameMode, null, null, null, null, null, null, p_prmProfileManager, null, null)
        {
        }

        /// <summary>
        /// Returns whether the installer currently detects a scripted replay cache.
        /// </summary>
        /// <returns><c>true</c> when a replay cache is available; otherwise, <c>false</c>.</returns>
        public bool HasScriptedReplayCache()
        {
            return CheckScriptedModLog();
        }

        /// <summary>
        /// Loads the file mappings stored in the currently selected scripted replay cache.
        /// </summary>
        /// <returns>The cached file mappings, or <c>null</c> when no usable mappings are available.</returns>
        public List<KeyValuePair<string, string>> LoadScriptedReplayFiles()
        {
            return LoadXMLModFilesToInstall();
        }
    }

    /// <summary>
    /// Exposes the XML installer's protected file-install method for characterization testing.
    /// </summary>
    internal sealed class TestableXmlScriptInstaller : XmlScriptInstaller
    {
        /// <summary>
        /// Initializes the XML installer with the supplied test dependencies.
        /// </summary>
        /// <param name="p_modMod">The mod being installed.</param>
        /// <param name="p_gmdGameMode">The game mode supplying path rules.</param>
        /// <param name="p_igpInstallers">The installer group used by the XML installer.</param>
        /// <param name="p_ivaVirtualModActivator">The virtual mod activator used for staging and linking.</param>
        public TestableXmlScriptInstaller(IMod p_modMod, IGameMode p_gmdGameMode, InstallerGroup p_igpInstallers, IVirtualModActivator p_ivaVirtualModActivator)
            : base(p_modMod, p_gmdGameMode, p_igpInstallers, p_ivaVirtualModActivator)
        {
        }

        /// <summary>
        /// Adds a single archive file to the XML installer's deferred installation plan.
        /// </summary>
        /// <param name="p_strFrom">The archive source path.</param>
        /// <param name="p_strTo">The logical destination path.</param>
        /// <returns>The result returned by the XML installer while planning the operation.</returns>
        public bool InstallSingleFile(string p_strFrom, string p_strTo)
        {
            return InstallFileFromMod(p_strFrom, p_strTo);
        }

        /// <summary>
        /// Adds a complete XML-script file entry, including any plugin activation request, to the deferred installation plan.
        /// </summary>
        /// <param name="p_ilfFile">The XML-script file entry to plan.</param>
        /// <param name="p_booActivate">Whether the file should be activated when it represents a plugin.</param>
        /// <returns>The result returned by the XML installer while planning the entry.</returns>
        public bool InstallFileEntry(InstallableFile p_ilfFile, bool p_booActivate)
        {
            return InstallFile(p_ilfFile, p_booActivate);
        }

        /// <summary>
        /// Gets the number of operations currently recorded in the deferred XML installation plan.
        /// </summary>
        public int PlannedOperationCount
        {
            get { return InstallationSession.Plan.Count; }
        }

        /// <summary>
        /// Executes all operations currently queued by the XML installer.
        /// </summary>
        /// <returns><c>true</c> when all planned operations complete successfully; otherwise, <c>false</c>.</returns>
        public bool ExecutePlannedOperations()
        {
            return ExecuteInstallationPlan();
        }
    }

    /// <summary>
    /// Exposes the shadow installation plan produced by <see cref="ScriptFunctionProxy"/> for regression testing.
    /// </summary>
    internal sealed class TestableScriptFunctionProxy : ScriptFunctionProxy
    {
        /// <summary>
        /// Initializes the script-function proxy with the supplied test dependencies.
        /// </summary>
        /// <param name="p_modMod">The mod being installed.</param>
        /// <param name="p_gmdGameMode">The game mode supplying path rules.</param>
        /// <param name="p_eifEnvironmentInfo">The environment information exposed to the proxy.</param>
        /// <param name="p_ivaVirtualModActivator">The virtual mod activator used for deployment.</param>
        /// <param name="p_igpInstallers">The grouped installer dependencies.</param>
        /// <param name="p_uipUIProxy">The UI utility exposed to scripted calls.</param>
        public TestableScriptFunctionProxy(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, UIUtil p_uipUIProxy)
            : base(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_igpInstallers, p_uipUIProxy)
        {
        }

        /// <summary>
        /// Gets the ordered shadow plan recorded by the scripted installation session.
        /// </summary>
        public ScriptedInstallationPlan ShadowPlan
        {
            get { return InstallationSession.Plan; }
        }
    }

    /// <summary>
    /// Creates lightweight plugin instances from projected plugin files used by scripted-installer tests.
    /// </summary>
    internal sealed class ScriptedInstallerTestPluginFactory : IPluginFactory
    {
        /// <summary>
        /// Creates a plugin whose metadata can be copied into the projected plugin snapshot.
        /// </summary>
        public Plugin CreatePlugin(string p_strPluginPath)
        {
            return new Plugin(p_strPluginPath, Path.GetFileName(p_strPluginPath), null);
        }

        /// <summary>
        /// Returns no external plugin information for the isolated test fixture.
        /// </summary>
        public string GetUpdatedPluginInfo(string p_strPluginPath)
        {
            return null;
        }

        /// <summary>
        /// Treats test plugin files as activatable.
        /// </summary>
        public bool IsActivatiblePluginFile(string p_strPath)
        {
            return true;
        }
    }

    /// <summary>
    /// Builds and records the dependency graph required to exercise ScriptFunctionProxy and XmlScriptInstaller behavior.
    /// </summary>
    internal sealed class ScriptProxyContext
    {
        private readonly ThreadSafeObservableList<Plugin> m_oclManagedPlugins = new ThreadSafeObservableList<Plugin>();
        private readonly Dictionary<string, byte[]> m_dicModFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a scripted-installer context rooted in the supplied temporary directory.
        /// </summary>
        /// <param name="p_strRootPath">The temporary root used for virtual and install-info paths.</param>
        /// <param name="p_strDownloadId">The mod download identifier.</param>
        /// <param name="p_booMultiHd">Whether MultiHD mode is enabled.</param>
        /// <param name="p_booGameRequiresHardlink">Whether arbitrary files require HD-link staging.</param>
        /// <param name="p_strLinkResult">The value returned by virtual link creation.</param>
        /// <param name="p_pgfPluginFactory">The optional plugin factory used to parse projected plugin contents.</param>
        public ScriptProxyContext(string p_strRootPath, string p_strDownloadId, bool p_booMultiHd, bool p_booGameRequiresHardlink, string p_strLinkResult, IPluginFactory p_pgfPluginFactory = null)
        {
            VirtualPath = Path.Combine(p_strRootPath, "Virtual");
            HdLinkPath = Path.Combine(p_strRootPath, "HdLink");
            string strInstallInfoPath = Path.Combine(p_strRootPath, "InstallInfo");
            string strInstallationPath = Path.Combine(p_strRootPath, "Game", "Data");
            Directory.CreateDirectory(VirtualPath);
            Directory.CreateDirectory(HdLinkPath);
            Directory.CreateDirectory(strInstallInfoPath);
            Directory.CreateDirectory(strInstallationPath);

            FileInstaller = new RecordingModFileInstaller();
            DataFileUtil = new RecordingDataFileUtil();
            IniInstaller = new RecordingIniInstaller();

            // The transparent stubs expose only the members exercised by scripted installer code and return neutral defaults for unrelated APIs.
            IGameModeEnvironmentInfo gmeEnvironment = InterfaceStub<IGameModeEnvironmentInfo>.Create((p_mifMethod, p_objArgs) =>
            {
                switch (p_mifMethod.Name)
                {
                    case "get_InstallInfoDirectory":
                        return strInstallInfoPath;
                    case "get_InstallationPath":
                        return strInstallationPath;
                    default:
                        return null;
                }
            });

            GameMode = InterfaceStub<IGameMode>.Create((p_mifMethod, p_objArgs) =>
            {
                switch (p_mifMethod.Name)
                {
                    case "get_GameModeEnvironmentInfo":
                        return gmeEnvironment;
                    case "get_InstallationPath":
                        return strInstallationPath;
                    case "get_PluginExtensions":
                        return new[] { ".esp", ".esm", ".esl" };
                    case "HardlinkRequiredFilesType":
                        return p_booGameRequiresHardlink;
                    case "GetModFormatAdjustedPath":
                        string strRequestedPath = (string)p_objArgs[1];
                        return strRequestedPath == null ? String.Empty : "adjusted:" + strRequestedPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    case "GetPluginFactory":
                        return p_pgfPluginFactory;
                    default:
                        return null;
                }
            });

            Mod = InterfaceStub<IMod>.Create((p_mifMethod, p_objArgs) =>
            {
                switch (p_mifMethod.Name)
                {
                    case "get_Filename":
                    case "get_FileName":
                        return Path.Combine(p_strRootPath, "ExampleMod.zip");
                    case "get_DownloadId":
                        return p_strDownloadId;
                    case "get_ModName":
                        return "Example Mod";
                    case "get_HumanReadableVersion":
                        return "1.2.3";
                    case "get_Format":
                        return null;
                    case "GetFileList":
                        return new List<string>(m_dicModFiles.Keys);
                    case "GetFile":
                        ModGetFileCallCount++;
                        byte[] bteData;
                        return m_dicModFiles.TryGetValue((string)p_objArgs[0], out bteData) ? bteData : null;
                    default:
                        return null;
                }
            });

            IModLinkInstaller mliLinkInstaller = InterfaceStub<IModLinkInstaller>.Create((p_mifMethod, p_objArgs) =>
            {
                if (p_mifMethod.Name == "AddFileLink")
                {
                    LinkCallCount++;
                    LastLinkedDestination = (string)p_objArgs[1];
                    LastLinkedSource = (string)p_objArgs[2];
                    return p_strLinkResult;
                }
                return null;
            });

            VirtualModActivator = InterfaceStub<IVirtualModActivator>.Create((p_mifMethod, p_objArgs) =>
            {
                switch (p_mifMethod.Name)
                {
                    case "get_MultiHDMode":
                        return p_booMultiHd;
                    case "get_VirtualPath":
                        return VirtualPath;
                    case "get_HDLinkFolder":
                        return HdLinkPath;
                    case "GetModLinkInstaller":
                        return mliLinkInstaller;
                    default:
                        return null;
                }
            });

            // Keep a mutable observable backing list so load-order tests can change the managed-plugin snapshot without replacing the proxy.
            ReadOnlyObservableList<Plugin> rolManagedPlugins = new ReadOnlyObservableList<Plugin>(m_oclManagedPlugins);
            IPluginManager pmgPluginManager = InterfaceStub<IPluginManager>.Create((p_mifMethod, p_objArgs) =>
            {
                switch (p_mifMethod.Name)
                {
                    case "get_ManagedPlugins":
                        return rolManagedPlugins;
                    case "get_ActivePlugins":
                        return rolManagedPlugins;
                    case "IsActivatiblePluginFile":
                        PluginActivationQueryInstallCallCount = FileInstaller.InstallCallCount;
                        return ActivatablePluginResult;
                    case "SetPluginActivation":
                        LastPluginActivationPath = (string)p_objArgs[0];
                        LastPluginActivationState = (bool)p_objArgs[1];
                        return null;
                    case "SetPluginOrderIndex":
                        PluginOrderCalls.Add(new KeyValuePair<Plugin, int>((Plugin)p_objArgs[0], (int)p_objArgs[1]));
                        return null;
                    case "CanChangeActiveState":
                    case "CanChangePluginOrder":
                        return true;
                    default:
                        return null;
                }
            });

            IGameSpecificValueInstaller gviInstaller = InterfaceStub<IGameSpecificValueInstaller>.Create((p_mifMethod, p_objArgs) => null);
            Installers = new InstallerGroup(DataFileUtil, FileInstaller, IniInstaller, gviInstaller, pmgPluginManager);
            Proxy = new TestableScriptFunctionProxy(Mod, GameMode, null, VirtualModActivator, Installers, null);
        }

        /// <summary>
        /// Gets the script-function proxy under test.
        /// </summary>
        public TestableScriptFunctionProxy Proxy { get; }

        /// <summary>
        /// Gets the mod exposed to the scripted installer.
        /// </summary>
        public IMod Mod { get; }

        /// <summary>
        /// Gets the game mode exposed to the scripted installer.
        /// </summary>
        public IGameMode GameMode { get; }

        /// <summary>
        /// Gets the virtual mod activator exposed to the scripted installer.
        /// </summary>
        public IVirtualModActivator VirtualModActivator { get; }

        /// <summary>
        /// Gets the grouped installer dependencies used by the scripted installer.
        /// </summary>
        public InstallerGroup Installers { get; }

        /// <summary>
        /// Gets the recording file installer used by the fixture.
        /// </summary>
        public RecordingModFileInstaller FileInstaller { get; }

        /// <summary>
        /// Gets the recording data-file utility used by the fixture.
        /// </summary>
        public RecordingDataFileUtil DataFileUtil { get; }

        /// <summary>
        /// Gets the in-memory INI installer used by the fixture.
        /// </summary>
        public RecordingIniInstaller IniInstaller { get; }

        /// <summary>
        /// Gets the virtual staging root used by the fixture.
        /// </summary>
        public string VirtualPath { get; }

        /// <summary>
        /// Gets the HD-link staging root used by the fixture.
        /// </summary>
        public string HdLinkPath { get; }

        /// <summary>
        /// Gets the number of archive-file content requests observed by the mod stub.
        /// </summary>
        public int ModGetFileCallCount { get; private set; }

        /// <summary>
        /// Gets the number of file-link requests observed by the fixture.
        /// </summary>
        public int LinkCallCount { get; private set; }

        /// <summary>
        /// Gets the most recent logical destination passed to the file-link installer.
        /// </summary>
        public string LastLinkedDestination { get; private set; }

        /// <summary>
        /// Gets the most recent staging source passed to the file-link installer.
        /// </summary>
        public string LastLinkedSource { get; private set; }

        /// <summary>
        /// Gets or sets whether the plugin manager should treat queried files as activatable plugins.
        /// </summary>
        public bool ActivatablePluginResult { get; set; }

        /// <summary>
        /// Gets the archive-file install count observed when plugin activatability was last queried.
        /// </summary>
        public int PluginActivationQueryInstallCallCount { get; private set; }

        /// <summary>
        /// Gets the most recent path supplied to plugin activation.
        /// </summary>
        public string LastPluginActivationPath { get; private set; }

        /// <summary>
        /// Gets the most recent activation state supplied to plugin activation.
        /// </summary>
        public bool? LastPluginActivationState { get; private set; }

        /// <summary>
        /// Gets the recorded plugin reorder requests in invocation order.
        /// </summary>
        public List<KeyValuePair<Plugin, int>> PluginOrderCalls { get; } = new List<KeyValuePair<Plugin, int>>();

        /// <summary>
        /// Adds or replaces an archive file exposed by the mod stub.
        /// </summary>
        /// <param name="p_strPath">The archive-relative file path.</param>
        /// <param name="p_bteData">The file contents returned by the mod stub.</param>
        public void AddModFile(string p_strPath, byte[] p_bteData)
        {
            m_dicModFiles[p_strPath] = p_bteData;
        }

        /// <summary>
        /// Adds a managed plugin to the plugin-manager view exposed to scripted APIs.
        /// </summary>
        /// <param name="p_strFileName">The plugin filename to add.</param>
        public void AddManagedPlugin(string p_strFileName)
        {
            m_oclManagedPlugins.Add(new Plugin(Path.Combine(GameMode.InstallationPath, p_strFileName), String.Empty, null));
        }
    }
}
