namespace GamebryoBaseTests.PluginManagement
{
    using System.Collections.Generic;

    using Nexus.Client.Games.Gamebryo.PluginManagement;
    using Nexus.Client.Games.Gamebryo.Plugins;
    using Nexus.Client.Plugins;

    using NUnit.Framework;

    [TestFixture]
    public class GamebryoPluginOrderValidatorTests
    {
        private GamebryoPluginOrderValidator _validator;

        private Plugin _fakeTrueMaster;
        private Plugin _fakeLightMaster;
        private Plugin _fakeBothMasters;
        private Plugin _fakeRegularMod;


        [SetUp]
        public void SetUp()
        {
            _fakeTrueMaster = new GamebryoPlugin("FakePath", "Fake Master", null, true, false);
            _fakeLightMaster = new GamebryoPlugin("FakePath", "Fake Light Master", null, false, true);
            _fakeBothMasters = new GamebryoPlugin("FakePath", "Fake True Light Master", null, true, true);
            _fakeRegularMod = new GamebryoPlugin("FakePath", "Fake regular mod", null, false, false);

            _validator = new GamebryoPluginOrderValidator(new string[0]);
        }

        [Test]
        public void ValidateOrderTest()
        {
            var plugins = new List<Plugin> {_fakeTrueMaster, _fakeLightMaster, _fakeRegularMod};
            Assert.IsTrue(_validator.ValidateOrder(plugins));

            plugins.Add(_fakeTrueMaster);
            Assert.IsFalse(_validator.ValidateOrder(plugins));
        }

        [Test]
        public void CorrectOrderTest()
        {
            var plugins = new List<Plugin> { _fakeTrueMaster, _fakeLightMaster, _fakeRegularMod };
            Assert.IsTrue(_validator.ValidateOrder(plugins));

            _validator.CorrectOrder(plugins);
            Assert.IsTrue(_validator.ValidateOrder(plugins));

            plugins.Add(_fakeTrueMaster);
            Assert.IsFalse(_validator.ValidateOrder(plugins));

            _validator.CorrectOrder(plugins);
            Assert.IsTrue(_validator.ValidateOrder(plugins));
        }

        /// <summary>
        /// Test to ensure the plugin sorting issue doesn't reappear.
        /// </summary>
        [Test]
        public void PluginSortingRegressionTest()
        {
            var esmAfterEsl = new List<Plugin> { _fakeBothMasters, _fakeTrueMaster };
            Assert.IsTrue(_validator.ValidateOrder(esmAfterEsl));
        }
    }
}
