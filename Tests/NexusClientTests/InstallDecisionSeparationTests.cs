using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Verifies the capability boundaries used to separate overwrite decisions from installation mutations.
    /// </summary>
    [TestFixture]
    public class InstallDecisionSeparationTests
    {
        /// <summary>
        /// Verifies that INI installers expose decision and approved-apply operations through the optional capability interface.
        /// </summary>
        [Test]
        public void IniInstaller_ExposesSeparatedDecisionCapability()
        {
            Assert.That(typeof(IIniEditDecisionSupport).IsAssignableFrom(typeof(IniInstaller)), Is.True);
            Assert.That(typeof(IIniEditDecisionSupport).IsAssignableFrom(typeof(IniUpgradeInstaller)), Is.True);
        }

        /// <summary>
        /// Verifies that file installers expose decision and approved-write operations through the optional capability interface.
        /// </summary>
        [Test]
        public void ModFileInstaller_ExposesSeparatedDecisionCapability()
        {
            Assert.That(typeof(IModFileInstallDecisionSupport).IsAssignableFrom(typeof(ModFileInstaller)), Is.True);
            Assert.That(typeof(IModFileInstallDecisionSupport).IsAssignableFrom(typeof(ModFileUpgradeInstaller)), Is.True);
        }

        /// <summary>
        /// Verifies that virtual-link deployment can accept a conflict decision resolved before deployment.
        /// </summary>
        [Test]
        public void ModLinkInstaller_ExposesSeparatedDecisionCapability()
        {
            Assert.That(typeof(IModLinkInstallDecisionSupport).IsAssignableFrom(typeof(ModLinkInstaller)), Is.True);
        }

        /// <summary>
        /// Verifies the unresolved decision used when planning does not encounter a user-visible virtual-link conflict.
        /// </summary>
        [Test]
        public void ModLinkInstallDecision_DefaultDecision_IsUnresolved()
        {
            ModLinkInstallDecision midDecision = new ModLinkInstallDecision();

            Assert.That(midDecision.IsResolved, Is.False);
            Assert.That(midDecision.Overwrite, Is.False);
        }

        /// <summary>
        /// Verifies that an explicitly resolved virtual-link conflict retains the selected overwrite action.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ModLinkInstallDecision_ResolvedDecision_RetainsOverwriteChoice(bool p_booOverwrite)
        {
            ModLinkInstallDecision midDecision = new ModLinkInstallDecision(p_booOverwrite);

            Assert.That(midDecision.IsResolved, Is.True);
            Assert.That(midDecision.Overwrite, Is.EqualTo(p_booOverwrite));
        }
    }
}
