namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.Remoting.Messaging;
    using System.Runtime.Remoting.Proxies;

    using Nexus.Client.ModManagement.Scripting;
    using Nexus.Client.PluginManagement;
    using Nexus.Client.Plugins;

    using NUnit.Framework;

    /// <summary>
    /// Verifies installation-scoped scripted plugin activation intent and final reconciliation sequencing.
    /// </summary>
    [TestFixture]
    public class ScriptedPluginActivationStateTests
    {
        /// <summary>
        /// Verifies that plugin deployment order does not affect the final implicit activation request set.
        /// </summary>
        [Test]
        public void DeployedPlugins_DependentBeforeMasterRetainsBothActivationRequests()
        {
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();

            state.RecordDeployedPlugin(@"C:\Game\Data\Dependent.esp");
            state.RecordDeployedPlugin(@"C:\Game\Data\Master.esm");

            CollectionAssert.AreEquivalent(
                new[] { @"C:\Game\Data\Dependent.esp", @"C:\Game\Data\Master.esm" },
                state.GetRequestedActivePluginPaths());
        }

        /// <summary>
        /// Verifies that an explicit master disable overrides the implicit activation introduced by deployment.
        /// </summary>
        [Test]
        public void ExplicitDisable_MasterRemainsExcludedWhileDependentRequestIsRetained()
        {
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();
            const string dependent = @"C:\Game\Data\Dependent.esp";
            const string master = @"C:\Game\Data\Master.esm";

            state.RecordDeployedPlugin(dependent);
            state.RecordDeployedPlugin(master);
            state.RecordActivationRequest(master, false);

            CollectionAssert.AreEquivalent(new[] { dependent }, state.GetRequestedActivePluginPaths());

            bool masterRequestedActive;
            Assert.IsTrue(state.TryGetRequestedActivation(master, out masterRequestedActive));
            Assert.IsFalse(masterRequestedActive);
        }

        /// <summary>
        /// Verifies that later explicit activation instructions replace earlier instructions for the same plugin.
        /// </summary>
        [Test]
        public void ExplicitActivationRequests_LastInstructionWins()
        {
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();
            const string plugin = @"C:\Game\Data\Toggle.esp";

            state.RecordDeployedPlugin(plugin);
            state.RecordActivationRequest(plugin, false);
            state.RecordActivationRequest(plugin, true);

            bool requestedActive;
            Assert.IsTrue(state.TryGetRequestedActivation(plugin, out requestedActive));
            Assert.IsTrue(requestedActive);

            state.RecordActivationRequest(plugin, false);

            Assert.IsTrue(state.TryGetRequestedActivation(plugin, out requestedActive));
            Assert.IsFalse(requestedActive);
            Assert.AreEqual(0, state.GetRequestedActivePluginPaths().Count);
        }

        /// <summary>
        /// Verifies that deployed and explicit plugin keys are matched without path-case sensitivity.
        /// </summary>
        [Test]
        public void Tracking_IsCaseInsensitive()
        {
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();

            state.RecordDeployedPlugin(@"C:\Game\Data\CaseTest.ESP");
            state.RecordDeployedPlugin(@"c:\game\data\casetest.esp");
            state.RecordActivationRequest(@"c:\GAME\DATA\CASETEST.esp", false);

            Assert.AreEqual(1, state.DeployedPluginPaths.Count);
            Assert.AreEqual(0, state.GetRequestedActivePluginPaths().Count);
        }

        /// <summary>
        /// Verifies that deployment-only tracking does not introduce an implicit plugin activation request.
        /// </summary>
        [Test]
        public void DeploymentOnly_DoesNotRequestActivation()
        {
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();
            const string plugin = @"C:\Game\Data\Generated.esp";

            state.RecordDeployedPlugin(plugin, false);

            CollectionAssert.AreEquivalent(new[] { plugin }, state.DeployedPluginPaths);
            Assert.AreEqual(0, state.GetRequestedActivePluginPaths().Count);
            bool requestedActive;
            Assert.IsFalse(state.TryGetRequestedActivation(plugin, out requestedActive));
        }

        /// <summary>
        /// Verifies that explicit activation intent for an already-registered external plugin is retained even when this installation did not deploy it.
        /// </summary>
        [Test]
        public void ExplicitActivation_UndeployedPluginIsRetainedForFinalReconciliation()
        {
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();
            const string plugin = @"C:\Game\Data\Existing.esp";

            state.RecordActivationRequest(plugin, true);

            CollectionAssert.AreEquivalent(new[] { plugin }, state.GetRequestedActivePluginPaths());
            Assert.AreEqual(0, state.DeployedPluginPaths.Count);
        }

        /// <summary>
        /// Verifies that deployment-only tracking does not expand reconciliation beyond plugins with activation intent.
        /// </summary>
        [Test]
        public void Reconcile_SubmitsCompleteDeploymentAndFinalRequestedStateOnce()
        {
            const string dependent = @"C:\Game\Data\Dependent.esp";
            const string master = @"C:\Game\Data\Master.esm";
            const string generated = @"C:\Game\Data\Generated.esp";
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();
            RecordingPluginManagerProxy pluginManager = new RecordingPluginManagerProxy(new[] { dependent, master, generated });

            state.RecordDeployedPlugin(dependent);
            state.RecordDeployedPlugin(master);
            state.RecordDeployedPlugin(generated, false);
            state.RecordActivationRequest(master, false);

            state.Reconcile(pluginManager.Manager);

            Assert.AreEqual(1, pluginManager.ReconciliationCalls);
            CollectionAssert.AreEquivalent(new[] { dependent, master, generated }, pluginManager.LastDeployedPluginPaths);
            Assert.AreEqual(2, pluginManager.LastRequestedActivationStates.Count);
            Assert.IsTrue(pluginManager.LastRequestedActivationStates[dependent]);
            Assert.IsFalse(pluginManager.LastRequestedActivationStates[master]);
            Assert.IsFalse(pluginManager.LastRequestedActivationStates.ContainsKey(generated));
            Assert.AreEqual(0, state.DeployedPluginPaths.Count);
            Assert.AreEqual(0, state.GetRequestedActivePluginPaths().Count);
        }

        /// <summary>
        /// Verifies that an activation request for a missing, undeployed plugin remains a no-op during final reconciliation.
        /// </summary>
        [Test]
        public void Reconcile_MissingUndeployedExplicitActivationRemainsNoOp()
        {
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();
            RecordingPluginManagerProxy pluginManager = new RecordingPluginManagerProxy(new string[0]);

            state.RecordActivationRequest(@"C:\Game\Data\Missing.esp", true);

            Assert.DoesNotThrow(() => state.Reconcile(pluginManager.Manager));
            Assert.AreEqual(1, pluginManager.ReconciliationCalls);
            Assert.AreEqual(0, pluginManager.LastDeployedPluginPaths.Count);
            Assert.AreEqual(1, pluginManager.LastRequestedActivationStates.Count);
        }

        /// <summary>
        /// Verifies legacy scripted callers retain best-effort completion when native final reconciliation reports a conflict.
        /// </summary>
        [Test]
        public void Reconcile_FailedNativeResultRetainsLegacyBestEffortBehavior()
        {
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();
            RecordingPluginManagerProxy pluginManager = new RecordingPluginManagerProxy(new string[0])
            {
                ReconciliationResult = false
            };

            state.RecordActivationRequest(@"C:\Game\Data\Blocked.esp", true);

            Assert.DoesNotThrow(() => state.Reconcile(pluginManager.Manager));
            Assert.AreEqual(1, pluginManager.ReconciliationCalls);
            Assert.AreEqual(0, state.GetRequestedActivePluginPaths().Count);
        }
    }

    /// <summary>
    /// Records only the plugin-manager calls used by scripted activation reconciliation.
    /// </summary>
    internal sealed class RecordingPluginManagerProxy : RealProxy
    {
        private readonly Dictionary<string, Plugin> m_dicRegisteredPlugins;

        /// <summary>
        /// Initializes the proxy with the plugins considered registered at reconciliation time.
        /// </summary>
        public RecordingPluginManagerProxy(IEnumerable<string> p_enmRegisteredPluginPaths)
            : base(typeof(IPluginManager))
        {
            m_dicRegisteredPlugins = (p_enmRegisteredPluginPaths ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x, x => new Plugin(x, String.Empty, null), StringComparer.OrdinalIgnoreCase);
            Manager = (IPluginManager)GetTransparentProxy();
        }

        /// <summary>
        /// Gets the transparent plugin-manager proxy.
        /// </summary>
        public IPluginManager Manager { get; private set; }

        /// <summary>
        /// Gets the number of final reconciliation requests.
        /// </summary>
        public int ReconciliationCalls { get; private set; }

        /// <summary>
        /// Gets or sets the result returned by final plugin reconciliation.
        /// </summary>
        public bool ReconciliationResult { get; set; } = true;

        /// <summary>
        /// Gets the deployment paths passed to the latest reconciliation request.
        /// </summary>
        public List<string> LastDeployedPluginPaths { get; } = new List<string>();

        /// <summary>
        /// Gets the requested activation states passed to the latest reconciliation request.
        /// </summary>
        public Dictionary<string, bool> LastRequestedActivationStates { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Dispatches the reconciliation-specific plugin-manager calls to recording behavior.
        /// </summary>
        public override IMessage Invoke(IMessage p_msgMessage)
        {
            IMethodCallMessage call = (IMethodCallMessage)p_msgMessage;

            try
            {
                switch (call.MethodName)
                {
                    case "TryReconcileDeployedPlugins":
                        ReconciliationCalls++;
                        LastDeployedPluginPaths.Clear();
                        LastDeployedPluginPaths.AddRange((IList<string>)call.Args[0]);
                        LastRequestedActivationStates.Clear();
                        foreach (KeyValuePair<string, bool> request in (IDictionary<string, bool>)call.Args[1])
                            LastRequestedActivationStates[request.Key] = request.Value;
                        object[] returnArgs = (object[])call.Args.Clone();
                        returnArgs[2] = new List<PluginValidationDiagnostic>();
                        return new ReturnMessage(ReconciliationResult, returnArgs, 1, call.LogicalCallContext, call);
                    case "SetPluginActivation":
                        return new ReturnMessage(null, call.Args, 0, call.LogicalCallContext, call);
                    case "IsActivatiblePluginFile":
                        return new ReturnMessage(true, call.Args, 0, call.LogicalCallContext, call);
                    case "GetRegisteredPlugin":
                        Plugin plugin;
                        m_dicRegisteredPlugins.TryGetValue((string)call.Args[0], out plugin);
                        return new ReturnMessage(plugin, call.Args, 0, call.LogicalCallContext, call);
                    default:
                        throw new NotSupportedException("Unexpected plugin-manager call: " + call.MethodName);
                }
            }
            catch (Exception ex)
            {
                return new ReturnMessage(ex, call);
            }
        }
    }
}
