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
        /// Verifies that final reconciliation integrates the complete deployment, resets tracked plugins together, then reapplies only final requested activations.
        /// </summary>
        [Test]
        public void Reconcile_IntegratesFullDeploymentThenReappliesFinalRequestedSet()
        {
            const string dependent = @"C:\Game\Data\Dependent.esp";
            const string master = @"C:\Game\Data\Master.esm";
            ScriptedPluginActivationState state = new ScriptedPluginActivationState();
            RecordingPluginManagerProxy pluginManager = new RecordingPluginManagerProxy(new[] { dependent, master });

            state.RecordDeployedPlugin(dependent);
            state.RecordDeployedPlugin(master);
            state.RecordActivationRequest(master, false);

            state.Reconcile(pluginManager.Manager);

            Assert.AreEqual(2, pluginManager.IntegrationCalls.Count);
            CollectionAssert.AreEquivalent(new[] { dependent, master }, pluginManager.IntegrationCalls[0]);
            CollectionAssert.AreEquivalent(new[] { dependent }, pluginManager.IntegrationCalls[1]);
            Assert.IsFalse(pluginManager.LastBatchActivationState.Value);
            CollectionAssert.AreEquivalent(new[] { dependent, master }, pluginManager.LastBatchPluginPaths);
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
            Assert.AreEqual(0, pluginManager.IntegrationCalls.Count);
            Assert.AreEqual(0, pluginManager.LastBatchPluginPaths.Count);
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
        /// Gets each deployed-plugin integration request in invocation order.
        /// </summary>
        public List<IList<string>> IntegrationCalls { get; } = new List<IList<string>>();

        /// <summary>
        /// Gets the plugin paths passed to the most recent batch activation request.
        /// </summary>
        public List<string> LastBatchPluginPaths { get; } = new List<string>();

        /// <summary>
        /// Gets the activation state passed to the most recent batch activation request.
        /// </summary>
        public bool? LastBatchActivationState { get; private set; }

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
                    case "IntegrateDeployedPlugins":
                        IntegrationCalls.Add(new List<string>((IList<string>)call.Args[0]));
                        return CreateReturnMessage(call, null, null);
                    case "GetRegisteredPlugin":
                        Plugin plugin;
                        m_dicRegisteredPlugins.TryGetValue((string)call.Args[0], out plugin);
                        return CreateReturnMessage(call, plugin, null);
                    case "TrySetPluginActivation":
                        LastBatchPluginPaths.Clear();
                        LastBatchPluginPaths.AddRange(((IList<Plugin>)call.Args[0]).Select(x => x.Filename));
                        LastBatchActivationState = (bool)call.Args[1];
                        return CreateReturnMessage(call, true, new object[] { new List<PluginValidationDiagnostic>() });
                    default:
                        throw new NotSupportedException("Unexpected plugin-manager call: " + call.MethodName);
                }
            }
            catch (Exception ex)
            {
                return new ReturnMessage(ex, call);
            }
        }

        /// <summary>
        /// Creates a successful remoting response with optional out-parameter values.
        /// </summary>
        private static IMessage CreateReturnMessage(IMethodCallMessage p_mcmCall, object p_objReturnValue, object[] p_objOutArgs)
        {
            object[] outArgs = p_objOutArgs ?? new object[0];
            return new ReturnMessage(p_objReturnValue, outArgs, outArgs.Length, p_mcmCall.LogicalCallContext, p_mcmCall);
        }
    }
}
