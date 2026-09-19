namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Nexus.Client.Games;
    using Nexus.Client.PluginManagement;
    using Nexus.Client.PluginManagement.InstallationLog;
    using Nexus.Client.PluginManagement.OrderLog;
    using Nexus.Client.Plugins;

    using NUnit.Framework;

    /// <summary>
    /// Exercises scripted plugin reconciliation against the real plugin dependency validator and activation logs.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class ScriptedPluginReconciliationIntegrationTests
    {
        /// <summary>
        /// Verifies that a dependant deployed before its master is activated successfully once the full install state is reconciled.
        /// </summary>
        [Test]
        public void DependentBeforeMaster_FinalReconciliationActivatesBothOnce()
        {
            using (PluginManagerTestContext context = new PluginManagerTestContext(
                new[]
                {
                    new PluginDefinition("Dependent.esp", "Master.esm"),
                    new PluginDefinition("Master.esm")
                }))
            {
                IList<PluginValidationDiagnostic> diagnostics;
                bool reconciled = context.Manager.TryReconcileDeployedPlugins(
                    new[] { context.PathOf("Dependent.esp"), context.PathOf("Master.esm") },
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        { context.PathOf("Dependent.esp"), true },
                        { context.PathOf("Master.esm"), true }
                    },
                    out diagnostics);

                Assert.IsTrue(reconciled);
                Assert.AreEqual(0, diagnostics.Count);
                CollectionAssert.AreEquivalent(new[] { "Dependent.esp", "Master.esm" }, context.ActivePluginNames);
                Assert.AreEqual(1, context.ActivePluginSaveCount);
            }
        }

        /// <summary>
        /// Verifies that an unrelated active dependant prevents a scripted installation from disabling its required master.
        /// </summary>
        [Test]
        public void ExplicitMasterDisable_ExternalActiveDependentRejectsDisable()
        {
            using (PluginManagerTestContext context = new PluginManagerTestContext(
                new[]
                {
                    new PluginDefinition("Master.esm"),
                    new PluginDefinition("External.esp", "Master.esm")
                },
                new[] { "Master.esm", "External.esp" }))
            {
                IList<PluginValidationDiagnostic> diagnostics;
                bool reconciled = context.Manager.TryReconcileDeployedPlugins(
                    new[] { context.PathOf("Master.esm") },
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        { context.PathOf("Master.esm"), false }
                    },
                    out diagnostics);

                Assert.IsFalse(reconciled);
                CollectionAssert.AreEquivalent(new[] { "Master.esm", "External.esp" }, context.ActivePluginNames);
                Assert.IsTrue(diagnostics.Any(x =>
                    x.Kind == PluginValidationIssueKind.InactiveRequiredMaster &&
                    String.Equals(Path.GetFileName(x.Plugin.Filename), "External.esp", StringComparison.OrdinalIgnoreCase)));
                Assert.AreEqual(0, context.ActivePluginSaveCount);
            }
        }

        /// <summary>
        /// Verifies that a scripted dependant may be dropped so an explicit master-disable request can be honored safely.
        /// </summary>
        [Test]
        public void ExplicitMasterDisable_TrackedDependentIsDroppedAndMasterIsDisabled()
        {
            using (PluginManagerTestContext context = new PluginManagerTestContext(
                new[]
                {
                    new PluginDefinition("Master.esm"),
                    new PluginDefinition("Dependent.esp", "Master.esm")
                },
                new[] { "Master.esm", "Dependent.esp" }))
            {
                IList<PluginValidationDiagnostic> diagnostics;
                bool reconciled = context.Manager.TryReconcileDeployedPlugins(
                    new[] { context.PathOf("Master.esm"), context.PathOf("Dependent.esp") },
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        { context.PathOf("Master.esm"), false },
                        { context.PathOf("Dependent.esp"), true }
                    },
                    out diagnostics);

                Assert.IsFalse(reconciled);
                Assert.AreEqual(0, context.ActivePluginNames.Count);
                Assert.IsTrue(diagnostics.Any(x =>
                    x.Kind == PluginValidationIssueKind.InactiveRequiredMaster &&
                    String.Equals(Path.GetFileName(x.Plugin.Filename), "Dependent.esp", StringComparison.OrdinalIgnoreCase)));
                Assert.AreEqual(1, context.ActivePluginSaveCount);
            }
        }

        /// <summary>
        /// Verifies that a dependant whose required master never arrives remains inactive and reports the real dependency error.
        /// </summary>
        [Test]
        public void MissingMaster_FinalReconciliationLeavesDependentInactive()
        {
            using (PluginManagerTestContext context = new PluginManagerTestContext(
                new[] { new PluginDefinition("Dependent.esp", "Missing.esm") }))
            {
                IList<PluginValidationDiagnostic> diagnostics;
                bool reconciled = context.Manager.TryReconcileDeployedPlugins(
                    new[] { context.PathOf("Dependent.esp") },
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        { context.PathOf("Dependent.esp"), true }
                    },
                    out diagnostics);

                Assert.IsFalse(reconciled);
                Assert.AreEqual(0, context.ActivePluginNames.Count);
                Assert.IsTrue(diagnostics.Any(x =>
                    x.Kind == PluginValidationIssueKind.MissingMaster &&
                    String.Equals(Path.GetFileName(x.Plugin.Filename), "Dependent.esp", StringComparison.OrdinalIgnoreCase)));
                Assert.AreEqual(0, context.ActivePluginSaveCount);
            }
        }

        /// <summary>
        /// Verifies additive final reconciliation preserves unrelated active plugins while applying the incoming plugin request.
        /// </summary>
        [Test]
        public void IncomingPluginActivation_PreservesUnrelatedActivePlugin()
        {
            using (PluginManagerTestContext context = new PluginManagerTestContext(
                new[]
                {
                    new PluginDefinition("Existing.esp"),
                    new PluginDefinition("Incoming.esp")
                },
                new[] { "Existing.esp" }))
            {
                IList<PluginValidationDiagnostic> diagnostics;
                bool reconciled = context.Manager.TryReconcileDeployedPlugins(
                    new[] { context.PathOf("Incoming.esp") },
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        { context.PathOf("Incoming.esp"), true }
                    },
                    out diagnostics);

                Assert.IsTrue(reconciled);
                Assert.AreEqual(0, diagnostics.Count);
                CollectionAssert.AreEquivalent(new[] { "Existing.esp", "Incoming.esp" }, context.ActivePluginNames);
                Assert.AreEqual(1, context.ActivePluginSaveCount);
            }
        }

        /// <summary>
        /// Verifies that deployment-only plugin registration preserves the historical generated-file inactive state.
        /// </summary>
        [Test]
        public void DeploymentOnlyPlugin_RemainsInactiveWithoutActivationIntent()
        {
            using (PluginManagerTestContext context = new PluginManagerTestContext(
                new[] { new PluginDefinition("Generated.esp") }))
            {
                IList<PluginValidationDiagnostic> diagnostics;
                bool reconciled = context.Manager.TryReconcileDeployedPlugins(
                    new[] { context.PathOf("Generated.esp") },
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                    out diagnostics);

                Assert.IsTrue(reconciled);
                Assert.AreEqual(0, diagnostics.Count);
                Assert.AreEqual(0, context.ActivePluginNames.Count);
                Assert.AreEqual(0, context.ActivePluginSaveCount);
            }
        }

        /// <summary>
        /// Verifies that a deployment-only plugin can still be activated by a later explicit scripted instruction.
        /// </summary>
        [Test]
        public void DeploymentOnlyPlugin_ExplicitActivationIsApplied()
        {
            using (PluginManagerTestContext context = new PluginManagerTestContext(
                new[] { new PluginDefinition("Generated.esp") }))
            {
                IList<PluginValidationDiagnostic> diagnostics;
                bool reconciled = context.Manager.TryReconcileDeployedPlugins(
                    new[] { context.PathOf("Generated.esp") },
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        { context.PathOf("Generated.esp"), true }
                    },
                    out diagnostics);

                Assert.IsTrue(reconciled);
                Assert.AreEqual(0, diagnostics.Count);
                CollectionAssert.AreEquivalent(new[] { "Generated.esp" }, context.ActivePluginNames);
                Assert.AreEqual(1, context.ActivePluginSaveCount);
            }
        }

        /// <summary>
        /// Describes one plugin and its declared masters for the isolated plugin-manager fixture.
        /// </summary>
        private sealed class PluginDefinition
        {
            /// <summary>
            /// Initializes a plugin definition with an optional master list.
            /// </summary>
            public PluginDefinition(string p_strName, params string[] p_strMasters)
            {
                Name = p_strName;
                Masters = p_strMasters ?? new string[0];
            }

            public string Name { get; private set; }
            public string[] Masters { get; private set; }
        }

        /// <summary>
        /// Provides an isolated real PluginManager with in-memory serializers and synthetic plugin metadata.
        /// </summary>
        private sealed class PluginManagerTestContext : IDisposable
        {
            private readonly string m_strRoot;
            private readonly Dictionary<string, PluginDefinition> m_dicDefinitions;
            private ActivePluginLog m_aplActivePluginLog;
            private PluginOrderLog m_polPluginOrderLog;

            /// <summary>
            /// Initializes a plugin manager with the supplied plugins and initial active set.
            /// </summary>
            public PluginManagerTestContext(IList<PluginDefinition> p_lstDefinitions, IList<string> p_lstInitiallyActive = null)
            {
                m_strRoot = Path.Combine(Path.GetTempPath(), "NMM-ScriptedPlugin-Reconcile-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(m_strRoot);
                m_dicDefinitions = p_lstDefinitions.ToDictionary(x => PathOf(x.Name), x => x, StringComparer.OrdinalIgnoreCase);

                foreach (string pluginPath in m_dicDefinitions.Keys)
                    File.WriteAllBytes(pluginPath, new byte[] { 0 });

                IPluginFactory factory = InterfaceStub<IPluginFactory>.Create((method, args) =>
                {
                    switch (method.Name)
                    {
                        case "CreatePlugin":
                            PluginDefinition definition;
                            string path = (string)args[0];
                            if (!m_dicDefinitions.TryGetValue(path, out definition))
                                return null;
                            Plugin plugin = new Plugin(path, definition.Name, null);
                            bool isMaster = String.Equals(Path.GetExtension(definition.Name), ".esm", StringComparison.OrdinalIgnoreCase);
                            plugin.SetMetadata(new PluginMetadata(
                                Path.GetExtension(definition.Name),
                                isMaster ? PluginHeaderFlags.Master : PluginHeaderFlags.None,
                                0,
                                definition.Masters,
                                isMaster,
                                PluginAddressClass.Full,
                                PluginSpecialFlags.None,
                                PluginParseStatus.Parsed));
                            return plugin;
                        case "GetUpdatedPluginInfo":
                            return null;
                        case "IsActivatiblePluginFile":
                            return m_dicDefinitions.ContainsKey((string)args[0]);
                        default:
                            return null;
                    }
                });

                PluginRegistry registry = new PluginRegistry(factory);
                foreach (string pluginPath in m_dicDefinitions.Keys)
                    Assert.IsTrue(registry.RegisterPlugin(pluginPath), "Failed to register test plugin: " + pluginPath);

                HashSet<string> initiallyActive = new HashSet<string>(
                    (p_lstInitiallyActive ?? new string[0]).Select(PathOf),
                    StringComparer.OrdinalIgnoreCase);

                IActivePluginLogSerializer activeSerializer = InterfaceStub<IActivePluginLogSerializer>.Create((method, args) =>
                {
                    switch (method.Name)
                    {
                        case "LoadPluginLog":
                            return initiallyActive.ToList();
                        case "SavePluginLog":
                            ActivePluginSaveCount++;
                            return null;
                        default:
                            return null;
                    }
                });

                IPluginOrderLogSerializer orderSerializer = InterfaceStub<IPluginOrderLogSerializer>.Create((method, args) =>
                {
                    switch (method.Name)
                    {
                        case "LoadPluginOrder":
                            return p_lstDefinitions.Select(x => PathOf(x.Name)).ToList();
                        case "SavePluginOrder":
                            return null;
                        default:
                            return null;
                    }
                });

                IPluginOrderValidator orderValidator = InterfaceStub<IPluginOrderValidator>.Create((method, args) =>
                {
                    if (method.Name == "ValidateOrder")
                        return true;
                    return null;
                });

                PluginManagementPolicy policy = new PluginManagementPolicy();
                IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
                {
                    switch (method.Name)
                    {
                        case "get_PluginManagementPolicy":
                            return policy;
                        case "get_PluginDirectory":
                            return m_strRoot;
                        case "get_OrderedCriticalPluginNames":
                        case "get_OrderedOfficialUnmanagedPluginNames":
                            return new string[0];
                        case "get_MaxAllowedActivePluginsCount":
                            return 0;
                        case "IsCriticalPlugin":
                            return false;
                        case "GetModFormatAdjustedPath":
                            return args[1];
                        default:
                            return null;
                    }
                });

                try
                {
                    m_aplActivePluginLog = ActivePluginLog.Initialize(registry, activeSerializer);
                    m_polPluginOrderLog = PluginOrderLog.Initialize(registry, orderSerializer, orderValidator);
                    Manager = PluginManager.Initialize(gameMode, registry, m_aplActivePluginLog, m_polPluginOrderLog, orderValidator);
                    ActivePluginSaveCount = 0;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Gets the real plugin manager under test.
            /// </summary>
            public IPluginManager Manager { get; private set; }

            /// <summary>
            /// Gets the number of persisted active-plugin updates performed after fixture initialization.
            /// </summary>
            public int ActivePluginSaveCount { get; private set; }

            /// <summary>
            /// Gets the active plugin filenames after the latest reconciliation.
            /// </summary>
            public IList<string> ActivePluginNames
            {
                get
                {
                    return Manager.ActivePlugins
                        .Where(x => x != null)
                        .Select(x => Path.GetFileName(x.Filename))
                        .ToList();
                }
            }

            /// <summary>
            /// Resolves a test plugin filename to its physical fixture path.
            /// </summary>
            public string PathOf(string p_strPluginName)
            {
                return Path.Combine(m_strRoot, p_strPluginName);
            }

            /// <summary>
            /// Releases plugin-management singletons and removes the temporary fixture files.
            /// </summary>
            public void Dispose()
            {
                if (Manager != null)
                {
                    Manager.Release();
                    Manager = null;
                }
                if (m_polPluginOrderLog != null)
                {
                    m_polPluginOrderLog.Release();
                    m_polPluginOrderLog = null;
                }
                if (m_aplActivePluginLog != null)
                {
                    m_aplActivePluginLog.Release();
                    m_aplActivePluginLog = null;
                }
                if (Directory.Exists(m_strRoot))
                    Directory.Delete(m_strRoot, true);
            }
        }
    }
}
