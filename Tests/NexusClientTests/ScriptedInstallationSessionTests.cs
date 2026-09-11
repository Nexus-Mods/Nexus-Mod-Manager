namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;

    using Nexus.Client.ModManagement.Scripting;
    using Nexus.Client.ModManagement.Scripting.Operations;

    using NUnit.Framework;

    /// <summary>
    /// Verifies immediate and deferred scripted-installation session behavior and ordered plan execution.
    /// </summary>
    [TestFixture]
    public class ScriptedInstallationSessionTests
    {
        /// <summary>
        /// Verifies that a session requires an operation executor.
        /// </summary>
        [Test]
        public void Constructor_NullExecutorThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ScriptedInstallationSession(null));
        }

        /// <summary>
        /// Verifies that null operations are rejected before the installation plan is changed.
        /// </summary>
        [Test]
        public void Submit_NullOperationThrowsWithoutChangingPlan()
        {
            RecordingScriptedInstallOperationExecutor sioExecutor = new RecordingScriptedInstallOperationExecutor(true);
            ScriptedInstallationSession sisSession = new ScriptedInstallationSession(sioExecutor);

            Assert.Throws<ArgumentNullException>(() => sisSession.Submit(null));
            Assert.AreEqual(0, sisSession.Plan.Count);
            Assert.AreEqual(0, sioExecutor.Operations.Count);
        }

        /// <summary>
        /// Verifies that submitted operations are retained in order and executed synchronously.
        /// </summary>
        [Test]
        public void Submit_RecordsOperationsAndExecutesImmediately()
        {
            RecordingScriptedInstallOperationExecutor sioExecutor = new RecordingScriptedInstallOperationExecutor(true);
            ScriptedInstallationSession sisSession = new ScriptedInstallationSession(sioExecutor);
            InstallModFileOperation imoFirst = new InstallModFileOperation("Textures/Foo.dds", "textures/Foo.dds");
            EditIniOperation eioSecond = new EditIniOperation("game.ini", "Display", "Mode", "Borderless");

            Assert.IsTrue(sisSession.Submit(imoFirst));
            Assert.IsTrue(sisSession.Submit(eioSecond));

            Assert.AreEqual(2, sisSession.Plan.Count);
            Assert.AreSame(imoFirst, sisSession.Plan.Operations[0]);
            Assert.AreSame(eioSecond, sisSession.Plan.Operations[1]);
            CollectionAssert.AreEqual(new ScriptedInstallOperation[] { imoFirst, eioSecond }, sioExecutor.Operations);
        }

        /// <summary>
        /// Verifies that the session returns the executor result without altering the recorded operation.
        /// </summary>
        [Test]
        public void Submit_ReturnsExecutorResult()
        {
            RecordingScriptedInstallOperationExecutor sioExecutor = new RecordingScriptedInstallOperationExecutor(false);
            ScriptedInstallationSession sisSession = new ScriptedInstallationSession(sioExecutor);
            GenerateDataFileOperation gdoOperation = new GenerateDataFileOperation("config/test.bin", new byte[] { 1 });

            bool booResult = sisSession.Submit(gdoOperation);

            Assert.IsFalse(booResult);
            Assert.AreEqual(1, sisSession.Plan.Count);
            Assert.AreSame(gdoOperation, sisSession.Plan.Operations[0]);
        }

        /// <summary>
        /// Verifies that deferred sessions record operations without executing them during submission.
        /// </summary>
        [Test]
        public void Submit_DeferredModeRecordsWithoutExecuting()
        {
            RecordingScriptedInstallOperationExecutor sioExecutor = new RecordingScriptedInstallOperationExecutor(true);
            ScriptedInstallationSession sisSession = new ScriptedInstallationSession(sioExecutor, ScriptedInstallationSessionMode.Deferred);
            InstallModFileOperation imoOperation = new InstallModFileOperation("meshes/foo.nif", "meshes/foo.nif");

            Assert.IsTrue(sisSession.Submit(imoOperation));

            Assert.AreEqual(1, sisSession.Plan.Count);
            Assert.AreEqual(0, sioExecutor.Operations.Count);
            Assert.IsTrue(sisSession.HasPendingOperations);
            Assert.AreSame(imoOperation, sisSession.NextPendingOperation);
        }

        /// <summary>
        /// Verifies that deferred operations execute later in their original submission order.
        /// </summary>
        [Test]
        public void ExecutePendingOperations_DeferredModePreservesSubmissionOrder()
        {
            RecordingScriptedInstallOperationExecutor sioExecutor = new RecordingScriptedInstallOperationExecutor(true);
            ScriptedInstallationSession sisSession = new ScriptedInstallationSession(sioExecutor, ScriptedInstallationSessionMode.Deferred);
            InstallModFileOperation imoFirst = new InstallModFileOperation("textures/a.dds", "textures/a.dds");
            InstallModFileOperation imoSecond = new InstallModFileOperation("textures/b.dds", "textures/b.dds");

            sisSession.Submit(imoFirst);
            sisSession.Submit(imoSecond);

            Assert.IsTrue(sisSession.ExecutePendingOperations());
            CollectionAssert.AreEqual(new ScriptedInstallOperation[] { imoFirst, imoSecond }, sioExecutor.Operations);
            Assert.IsFalse(sisSession.HasPendingOperations);
            Assert.IsNull(sisSession.NextPendingOperation);
        }

        /// <summary>
        /// Verifies that ScriptFunctionProxy records logical operations while preserving immediate execution.
        /// </summary>
        [Test]
        public void ScriptProxy_RecordsOrderedLogicalOperationsInShadowPlan()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, "12345", false, false, "linked");
                byte[] bteData = { 1, 2, 3 };

                ctx.Proxy.InstallFileFromMod("Textures/Foo.DDS", "textures/Foo.dds");
                ctx.Proxy.GenerateDataFile("Config/Generated.bin", bteData);
                ctx.Proxy.EditIni("game.ini", "Display", "Mode", "Borderless");
                ctx.Proxy.SetPluginActivation("plugins/Test.esp", true);

                Assert.AreEqual(4, ctx.Proxy.ShadowPlan.Count);
                Assert.IsInstanceOf<InstallModFileOperation>(ctx.Proxy.ShadowPlan.Operations[0]);
                Assert.IsInstanceOf<GenerateDataFileOperation>(ctx.Proxy.ShadowPlan.Operations[1]);
                Assert.IsInstanceOf<EditIniOperation>(ctx.Proxy.ShadowPlan.Operations[2]);
                Assert.IsInstanceOf<SetPluginActivationOperation>(ctx.Proxy.ShadowPlan.Operations[3]);

                InstallModFileOperation imoInstall = (InstallModFileOperation)ctx.Proxy.ShadowPlan.Operations[0];
                Assert.AreEqual("Textures/Foo.DDS", imoInstall.SourcePath);
                Assert.AreEqual("textures/Foo.dds", imoInstall.DestinationPath);
                Assert.AreEqual(1, ctx.FileInstaller.InstallCallCount);
                Assert.AreEqual(1, ctx.FileInstaller.GenerateCallCount);
                Assert.AreEqual("Borderless", ctx.Proxy.GetIniString("game.ini", "Display", "Mode"));
                Assert.IsTrue(ctx.LastPluginActivationState.Value);
            }
        }
    }

    /// <summary>
    /// Records scripted installation operations and returns a configurable immediate execution result.
    /// </summary>
    internal sealed class RecordingScriptedInstallOperationExecutor : IScriptedInstallOperationExecutor
    {
        private readonly bool m_booResult;

        /// <summary>
        /// Initializes the executor with the result returned for each operation.
        /// </summary>
        /// <param name="p_booResult">The result to return from <see cref="Execute"/>.</param>
        public RecordingScriptedInstallOperationExecutor(bool p_booResult)
        {
            m_booResult = p_booResult;
        }

        /// <summary>
        /// Gets the operations received by the executor in execution order.
        /// </summary>
        public List<ScriptedInstallOperation> Operations { get; } = new List<ScriptedInstallOperation>();

        /// <summary>
        /// Records the supplied operation and returns the configured result.
        /// </summary>
        /// <param name="p_sioOperation">The operation submitted by the session.</param>
        /// <returns>The configured execution result.</returns>
        public bool Execute(ScriptedInstallOperation p_sioOperation)
        {
            Operations.Add(p_sioOperation);
            return m_booResult;
        }
    }
}
