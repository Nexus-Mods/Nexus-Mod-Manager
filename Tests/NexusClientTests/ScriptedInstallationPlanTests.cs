namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;

    using Nexus.Client.ModManagement.Scripting;
    using Nexus.Client.ModManagement.Scripting.Operations;

    using NUnit.Framework;

    /// <summary>
    /// Verifies the ordered scripted-installation operation model without exercising the runtime installation pipeline.
    /// </summary>
    [TestFixture]
    public class ScriptedInstallationPlanTests
    {
        /// <summary>
        /// Verifies that a new plan is empty and exposes its operation collection as read-only.
        /// </summary>
        [Test]
        public void NewPlan_IsEmptyAndReadOnly()
        {
            ScriptedInstallationPlan sipPlan = new ScriptedInstallationPlan();

            Assert.AreEqual(0, sipPlan.Count);
            Assert.AreEqual(0, sipPlan.Operations.Count);

            IList<ScriptedInstallOperation> lstOperations = sipPlan.Operations as IList<ScriptedInstallOperation>;
            Assert.IsNotNull(lstOperations);
            Assert.IsTrue(lstOperations.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => lstOperations.Add(new InstallModFileOperation("source", "destination")));
        }

        /// <summary>
        /// Verifies that operations retain their exact submission order and object identity.
        /// </summary>
        [Test]
        public void Add_PreservesOperationOrder()
        {
            ScriptedInstallationPlan sipPlan = new ScriptedInstallationPlan();
            InstallModFileOperation imoFirst = new InstallModFileOperation("first.dds", "textures/first.dds");
            EditIniOperation eioSecond = new EditIniOperation("game.ini", "Section", "Key", "Value");
            SetPluginActivationOperation saoThird = new SetPluginActivationOperation("plugin.esp", true);

            sipPlan.Add(imoFirst);
            sipPlan.Add(eioSecond);
            sipPlan.Add(saoThird);

            Assert.AreEqual(3, sipPlan.Count);
            Assert.AreSame(imoFirst, sipPlan.Operations[0]);
            Assert.AreSame(eioSecond, sipPlan.Operations[1]);
            Assert.AreSame(saoThird, sipPlan.Operations[2]);
        }

        /// <summary>
        /// Verifies that null operations cannot be appended to a plan.
        /// </summary>
        [Test]
        public void Add_NullOperationThrowsArgumentNullException()
        {
            ScriptedInstallationPlan sipPlan = new ScriptedInstallationPlan();

            Assert.Throws<ArgumentNullException>(() => sipPlan.Add(null));
            Assert.AreEqual(0, sipPlan.Count);
        }

        /// <summary>
        /// Verifies that file operations contain only the logical archive source and requested destination payload.
        /// </summary>
        [Test]
        public void FileOperations_PreserveLogicalPayload()
        {
            byte[] bteData = { 1, 2, 3, 4 };
            PerformBasicInstallOperation bioBasicInstall = new PerformBasicInstallOperation();
            InstallModFileOperation imoInstall = new InstallModFileOperation("meshes/source.nif", "meshes/target.nif");
            GenerateDataFileOperation gdoGenerate = new GenerateDataFileOperation("config/generated.bin", bteData);

            Assert.IsInstanceOf<ScriptedInstallOperation>(bioBasicInstall);
            Assert.AreEqual("meshes/source.nif", imoInstall.SourcePath);
            Assert.AreEqual("meshes/target.nif", imoInstall.DestinationPath);
            Assert.AreEqual("config/generated.bin", gdoGenerate.DestinationPath);
            CollectionAssert.AreEqual(bteData, gdoGenerate.Data);
        }

        /// <summary>
        /// Verifies that INI and game-specific operations preserve their requested logical values.
        /// </summary>
        [Test]
        public void ConfigurationOperations_PreserveLogicalPayload()
        {
            byte[] bteValue = { 10, 20, 30 };
            EditIniOperation eioIni = new EditIniOperation("Skyrim.ini", "Display", "fShadowDistance", "8000.0000");
            EditGameSpecificValueOperation egoGameValue = new EditGameSpecificValueOperation("Shader:15", bteValue);

            Assert.AreEqual("Skyrim.ini", eioIni.SettingsFileName);
            Assert.AreEqual("Display", eioIni.Section);
            Assert.AreEqual("fShadowDistance", eioIni.Key);
            Assert.AreEqual("8000.0000", eioIni.Value);
            Assert.AreEqual("Shader:15", egoGameValue.Key);
            CollectionAssert.AreEqual(bteValue, egoGameValue.Value);
        }

        /// <summary>
        /// Verifies that plugin activation and absolute plugin-order operations preserve their requested state.
        /// </summary>
        [Test]
        public void PluginOperations_PreserveLogicalPayload()
        {
            SetPluginActivationOperation saoActivation = new SetPluginActivationOperation("plugins/example.esp", false);
            SetPluginOrderIndexOperation sooOrder = new SetPluginOrderIndexOperation("plugins/example.esp", 7);

            Assert.AreEqual("plugins/example.esp", saoActivation.PluginPath);
            Assert.IsFalse(saoActivation.Activate);
            Assert.AreEqual("plugins/example.esp", sooOrder.PluginPath);
            Assert.AreEqual(7, sooOrder.NewIndex);
        }

        /// <summary>
        /// Verifies that legacy indexed load-order requests retain their distinct operation semantics.
        /// </summary>
        [Test]
        public void IndexedLoadOrderOperations_PreserveDistinctSemantics()
        {
            int[] intFullOrder = { 2, 0, 1 };
            int[] intMovedPlugins = { 3, 1 };
            SetLoadOrderOperation sloSet = new SetLoadOrderOperation(intFullOrder);
            MovePluginsInLoadOrderOperation mloMove = new MovePluginsInLoadOrderOperation(intMovedPlugins, 4);

            CollectionAssert.AreEqual(new[] { 2, 0, 1 }, sloSet.PluginIndices);
            CollectionAssert.AreEqual(new[] { 3, 1 }, mloMove.PluginIndices);
            Assert.AreEqual(4, mloMove.Position);
        }

        /// <summary>
        /// Verifies that indexed load-order operations copy their input arrays so later script-side mutations do not alter the plan.
        /// </summary>
        [Test]
        public void IndexedLoadOrderOperations_CopyInputArrays()
        {
            int[] intFullOrder = { 0, 1, 2 };
            int[] intMovedPlugins = { 0, 2 };
            SetLoadOrderOperation sloSet = new SetLoadOrderOperation(intFullOrder);
            MovePluginsInLoadOrderOperation mloMove = new MovePluginsInLoadOrderOperation(intMovedPlugins, 1);

            intFullOrder[0] = 99;
            intMovedPlugins[0] = 99;

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, sloSet.PluginIndices);
            CollectionAssert.AreEqual(new[] { 0, 2 }, mloMove.PluginIndices);
        }

        /// <summary>
        /// Verifies that relative load-order operations preserve path order independently of the caller's array.
        /// </summary>
        [Test]
        public void RelativeLoadOrderOperation_CopiesOrderedPluginPaths()
        {
            string[] strPluginPaths = { "A.esm", "B.esp", "C.esp" };
            SetRelativeLoadOrderOperation rloOperation = new SetRelativeLoadOrderOperation(strPluginPaths);

            strPluginPaths[0] = "Changed.esm";

            CollectionAssert.AreEqual(new[] { "A.esm", "B.esp", "C.esp" }, rloOperation.PluginPaths);
        }

        /// <summary>
        /// Verifies that operation objects preserve legacy payloads without applying execution-time validation.
        /// </summary>
        [Test]
        public void OperationConstructors_PreserveNullableLegacyPayloads()
        {
            EditIniOperation eioIni = new EditIniOperation("game.ini", "Section", "Key", null);
            SetLoadOrderOperation sloLoadOrder = new SetLoadOrderOperation(null);
            SetRelativeLoadOrderOperation rloRelativeOrder = new SetRelativeLoadOrderOperation(null);

            Assert.IsNull(eioIni.Value);
            Assert.IsNull(sloLoadOrder.PluginIndices);
            Assert.IsNull(rloRelativeOrder.PluginPaths);
        }
    }
}
