namespace GamebryoBaseTests.PluginManagement
{
    using System.Collections.Generic;
    using System.Linq;

    using Nexus.Client.PluginManagement;
    using Nexus.Client.Plugins;

    using NUnit.Framework;

    [TestFixture]
    public class PluginManagementPolicyTests
    {
        [Test]
        public void ClassifyUsesPolicyDrivenAddressClassesAndSpecialFlags()
        {
            PluginManagementPolicy policy = new PluginManagementPolicy();
            policy.AddHeaderFlagMapping(new PluginHeaderFlagMapping(PluginHeaderFlagSource.RecordFlags1, 0x00000400, PluginHeaderFlags.Medium));
            policy.AddHeaderFlagMapping(new PluginHeaderFlagMapping(PluginHeaderFlagSource.RecordFlags1, 0x00000800, PluginHeaderFlags.Small));
            policy.AddBlueprintPluginPrefix("BlueprintShips-");

            PluginMetadata fullMaster = policy.Classify("Master.esm", PluginHeaderFlags.None, 0, null, PluginParseStatus.Parsed);
            PluginMetadata lightPlugin = policy.Classify("LightPlugin.esp", PluginHeaderFlags.Light, 0, null, PluginParseStatus.Parsed);
            PluginMetadata esmFe = policy.Classify("LightMaster.esm", PluginHeaderFlags.Light, 0, null, PluginParseStatus.Parsed);
            PluginMetadata medium = policy.Classify("Medium.esm", policy.MapHeaderFlags(0x00000400, 0, 0), 0, null, PluginParseStatus.Parsed);
            PluginMetadata small = policy.Classify("Small.esm", policy.MapHeaderFlags(0x00000800, 0, 0), 0, null, PluginParseStatus.Parsed);
            PluginMetadata blueprint = policy.Classify("BlueprintShips-Test.esm", PluginHeaderFlags.None, 0, null, PluginParseStatus.Parsed);

            Assert.That(fullMaster.AddressClass, Is.EqualTo(PluginAddressClass.Full));
            Assert.That(fullMaster.EffectiveMaster, Is.True);
            Assert.That(lightPlugin.AddressClass, Is.EqualTo(PluginAddressClass.Light));
            Assert.That(lightPlugin.EffectiveMaster, Is.False);
            Assert.That(esmFe.AddressClass, Is.EqualTo(PluginAddressClass.Light));
            Assert.That(esmFe.EffectiveMaster, Is.True);
            Assert.That(medium.AddressClass, Is.EqualTo(PluginAddressClass.Medium));
            Assert.That(small.AddressClass, Is.EqualTo(PluginAddressClass.Small));
            Assert.That((blueprint.SpecialFlags & PluginSpecialFlags.Blueprint), Is.EqualTo(PluginSpecialFlags.Blueprint));
        }

        [Test]
        public void SnapshotEnforcesEveryConfiguredAddressSpaceLimit()
        {
            PluginManagementPolicy policy = new PluginManagementPolicy();
            policy.AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Full, 0, 1, "{0:X2}"));
            policy.AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Light, 0, 1, "FE:{0:X3}"));
            policy.AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Medium, 0, 1, "FD:{0:X2}"));
            policy.AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Small, 0, 1, "FE:{0:X3}"));

            List<Plugin> plugins = new List<Plugin>
            {
                CreatePlugin("FullA.esp", PluginAddressClass.Full, false),
                CreatePlugin("FullB.esp", PluginAddressClass.Full, false),
                CreatePlugin("LightA.esp", PluginAddressClass.Light, false),
                CreatePlugin("LightB.esp", PluginAddressClass.Light, false),
                CreatePlugin("MediumA.esm", PluginAddressClass.Medium, true),
                CreatePlugin("MediumB.esm", PluginAddressClass.Medium, true),
                CreatePlugin("SmallA.esm", PluginAddressClass.Small, true),
                CreatePlugin("SmallB.esm", PluginAddressClass.Small, true)
            };

            PluginSnapshot snapshot = new PluginSnapshotBuilder().Build(policy, plugins, new HashSet<Plugin>(plugins));

            Assert.That(snapshot.Diagnostics.Count(x => x.Kind == PluginValidationIssueKind.AddressSpaceExhausted), Is.EqualTo(4));
        }

        [Test]
        public void CorrectStablePreservesFixedOrderAndMovesMastersAboveDependents()
        {
            PluginManagementPolicy policy = new PluginManagementPolicy();
            policy.AddFixedOrderPlugin("Skyrim.esm");
            policy.AddFixedOrderPlugin("Update.esm");

            Plugin skyrim = CreatePlugin("Skyrim.esm", PluginAddressClass.Full, true);
            Plugin update = CreatePlugin("Update.esm", PluginAddressClass.Full, true, "Skyrim.esm");
            Plugin dependent = CreatePlugin("Dependent.esp", PluginAddressClass.Full, false, "Update.esm");
            List<Plugin> unordered = new List<Plugin> { dependent, update, skyrim };

            PluginSnapshot invalidSnapshot = new PluginSnapshotBuilder().Build(policy, unordered, new HashSet<Plugin>(unordered));
            List<Plugin> corrected = new PluginSnapshotBuilder().CorrectStable(policy, unordered);
            PluginSnapshot correctedSnapshot = new PluginSnapshotBuilder().Build(policy, corrected, new HashSet<Plugin>(corrected));

            Assert.That(invalidSnapshot.Diagnostics.Any(x => x.Kind == PluginValidationIssueKind.InvalidFixedPluginPlacement), Is.True);
            Assert.That(corrected.Select(x => System.IO.Path.GetFileName(x.Filename)).ToArray(), Is.EqualTo(new[] { "Skyrim.esm", "Update.esm", "Dependent.esp" }));
            Assert.That(correctedSnapshot.HasErrors, Is.False);
        }

        private static Plugin CreatePlugin(string filename, PluginAddressClass addressClass, bool effectiveMaster, params string[] masters)
        {
            Plugin plugin = new Plugin(filename, filename, null);
            PluginHeaderFlags flags = effectiveMaster ? PluginHeaderFlags.Master : PluginHeaderFlags.None;
            plugin.SetMetadata(new PluginMetadata(System.IO.Path.GetExtension(filename), flags, 0, masters, effectiveMaster, addressClass, PluginSpecialFlags.None, PluginParseStatus.Parsed));
            return plugin;
        }
    }
}