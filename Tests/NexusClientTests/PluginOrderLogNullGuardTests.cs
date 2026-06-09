namespace NexusClientTests
{
    using System.Collections.Generic;

    using Nexus.Client.PluginManagement;
    using Nexus.Client.PluginManagement.OrderLog;
    using Nexus.Client.Plugins;

    using NUnit.Framework;

    // Minimal stubs so PluginOrderLog can be instantiated without disk or game infrastructure.

    internal sealed class StubSerializer : IPluginOrderLogSerializer
    {
        private readonly IEnumerable<string> _order;
        public StubSerializer(IEnumerable<string> order = null) => _order = order ?? new string[0];
        public IEnumerable<string> LoadPluginOrder() => _order;
        public void SavePluginOrder(IList<Plugin> p_lstOrderedPlugins) { }
    }

    internal sealed class StubValidator : IPluginOrderValidator
    {
        public bool ValidateOrder(IList<Plugin> p_lstPlugins) => true;
        public void CorrectOrder(IList<Plugin> p_lstPlugins) { }
    }

    internal sealed class StubPluginFactory : IPluginFactory
    {
        public Plugin CreatePlugin(string p_strPluginPath) =>
            new Plugin(p_strPluginPath, System.IO.Path.GetFileName(p_strPluginPath), null);

        public string GetUpdatedPluginInfo(string p_strPluginPath) => null;

        public bool IsActivatiblePluginFile(string p_strPath) => true;
    }

    [TestFixture]
    public class PluginOrderLogNullGuardTests
    {
        private PluginOrderLog _log;

        [SetUp]
        public void SetUp()
        {
            var registry = new PluginRegistry(new StubPluginFactory());
            _log = PluginOrderLog.Initialize(registry, new StubSerializer(), new StubValidator());
        }

        [TearDown]
        public void TearDown()
        {
            _log?.Release();
            _log = null;
        }

        [Test]
        public void SetPluginOrderIndex_NullPlugin_DoesNotThrow()
        {
            // Regression: before the fix this inserted null into the ordered list,
            // corrupting the persisted load order.
            Assert.DoesNotThrow(() => _log.SetPluginOrderIndex(null, 0));
        }

        [Test]
        public void SetPluginOrderIndex_NullPlugin_DoesNotAddToOrderedList()
        {
            int countBefore = _log.OrderedPlugins.Count;
            _log.SetPluginOrderIndex(null, 0);
            Assert.AreEqual(countBefore, _log.OrderedPlugins.Count,
                "A null plugin must not be inserted into the ordered plugin list.");
        }

        [Test]
        public void SetPluginOrderIndex_ValidPlugin_AddsToOrderedList()
        {
            var plugin = new Plugin("test.esp", "test.esp", null);
            int countBefore = _log.OrderedPlugins.Count;
            _log.SetPluginOrderIndex(plugin, 0);
            Assert.AreEqual(countBefore + 1, _log.OrderedPlugins.Count,
                "A valid plugin should be added to the ordered list.");
        }

        [Test]
        public void SetPluginOrderIndex_ValidPlugin_PlacedAtRequestedIndex()
        {
            var pluginA = new Plugin("a.esp", "a.esp", null);
            var pluginB = new Plugin("b.esp", "b.esp", null);
            _log.SetPluginOrderIndex(pluginA, 0);
            _log.SetPluginOrderIndex(pluginB, 0);
            Assert.AreEqual("b.esp", _log.OrderedPlugins[0].Filename,
                "Plugin inserted at index 0 should be first.");
        }
    }
}
