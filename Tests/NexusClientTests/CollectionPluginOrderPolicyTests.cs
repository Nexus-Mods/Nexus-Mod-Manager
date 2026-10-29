using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Games.DataDriven;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Collection verification agrees with the native precedence used during activation, including old retained reviews.</summary>
	[TestFixture]
	public class CollectionPluginOrderPolicyTests
	{
		/// <summary>The opt-in comes from typed JSON for any game definition; omission preserves existing behavior.</summary>
		[TestCase("", false)]
		[TestCase(",\"ignoreOfficialPluginOrder\":false", false)]
		[TestCase(",\"ignoreOfficialPluginOrder\":true", true)]
		public void GameManagedOfficialOrder_IsConfiguredByJson(string setting, bool expected)
		{
			GameModeDefinition definition = JsonConvert.DeserializeObject<GameModeDefinition>(
				"{\"modeId\":\"AnotherGame\",\"plugin\":{\"policy\":{\"schemaVersion\":1" + setting + "}}}");
			PluginManagementPolicy policy = DataDrivenPluginPolicyBuilder.Build(definition, new string[0],
				new[] { "Base.esm" }, new[] { "Official.esm" }, new[] { "Creation.esm" }, 0, null);
			Assert.AreEqual(expected, policy.IgnoreOfficialPluginOrder);
			Assert.AreEqual(expected, policy.IsPluginOrderManagedByGame("Official.esm"));
			Assert.AreEqual(expected, policy.IsPluginOrderManagedByGame("Base.esm"));
			Assert.AreEqual(expected, policy.IsPluginOrderManagedByGame("Creation.esm"));
			Assert.IsFalse(policy.IsPluginOrderManagedByGame("Mod.esm"));
		}

		/// <summary>Official positions are ignored only after opt-in; mod order and activation are still verified.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void OfficialOrderChecks_FollowCapturedPolicy(bool ignoreOfficialOrder)
		{
			CollectionNativeStateIndex state = CreateState(true, ignoreOfficialOrder,
				Official("BlueprintShips-Starfield.esm", 0), Plugin("A.esm", 1, true), Plugin("B.esm", 2, true));
			Assert.AreEqual(ignoreOfficialOrder, CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state,
				new[] { "A.esm", "BlueprintShips-Starfield.esm" }));
			Assert.AreEqual(ignoreOfficialOrder, CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state, new[] { "A.esm", "BlueprintShips-Starfield.esm", "B.esm" }));
			Assert.IsFalse(CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state, new[] { "B.esm", "A.esm" }));
			Assert.AreEqual(ignoreOfficialOrder, CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(state,
				CollectionPlannedPluginEffect.AbsoluteOrder("BlueprintShips-Starfield.esm", 10)));
			Assert.IsFalse(CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(state,
				CollectionPlannedPluginEffect.Activation("BlueprintShips-Starfield.esm", false)));
		}

		/// <summary>A game-owned master position is not a validation error, but missing and inactive masters remain errors.</summary>
		[Test]
		public void OfficialOrderExclusion_PreservesDependencyPresenceAndActivationChecks()
		{
			var policy = new PluginManagementPolicy { IgnoreOfficialPluginOrder = true };
			policy.AddOfficialPlugin("Official.esm");
			policy.AddFixedOrderPlugin("Official.esm");
			Plugin official = new Plugin("Official.esm", "Official", null);
			official.SetMetadata(policy.Classify(official.Filename, PluginHeaderFlags.Master, 0, new string[0], PluginParseStatus.Parsed));
			Plugin mod = new Plugin("Mod.esm", "Mod", null);
			mod.SetMetadata(policy.Classify(mod.Filename, PluginHeaderFlags.Master, 0, new[] { "Official.esm" }, PluginParseStatus.Parsed));
			var builder = new PluginSnapshotBuilder();
			var order = new[] { mod, official };
			CollectionAssert.AreEqual(order, builder.CorrectStable(policy, order));
			PluginSnapshot valid = builder.Build(policy, order, new HashSet<Plugin>(order));
			Assert.IsFalse(valid.Diagnostics.Any(x => x.Kind == PluginValidationIssueKind.MasterBelowDependent || x.Kind == PluginValidationIssueKind.InvalidFixedPluginPlacement));
			Assert.IsTrue(builder.Build(policy, order, new HashSet<Plugin> { mod }).Diagnostics.Any(x => x.Kind == PluginValidationIssueKind.InactiveRequiredMaster));
			Assert.IsTrue(builder.Build(policy, new[] { mod }, new HashSet<Plugin> { mod }).Diagnostics.Any(x => x.Kind == PluginValidationIssueKind.MissingMaster));
			policy.IgnoreOfficialPluginOrder = false;
			Assert.IsTrue(builder.Build(policy, order, new HashSet<Plugin>(order)).Diagnostics.Any(x => x.Kind == PluginValidationIssueKind.MasterBelowDependent));
		}

		/// <summary>Blueprint header flags are recognized even when a Starfield plugin does not use the BlueprintShips prefix.</summary>
		[Test]
		public void StarfieldDefinitionRecognizesBlueprintHeaderFlag()
		{
			string path = System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "GameModes", "Definitions", "Starfield", "game.json");
			GameModeDefinition definition = JsonConvert.DeserializeObject<GameModeDefinition>(System.IO.File.ReadAllText(path));
			PluginManagementPolicy policy = DataDrivenPluginPolicyBuilder.Build(definition, new string[0],
				new string[0], new string[0], new string[0], 0, null);
			PluginHeaderFlags flags = policy.MapHeaderFlags(0x00000800, 0, 0);
			Assert.That((flags & PluginHeaderFlags.Blueprint) != 0, Is.True);
			Assert.That((policy.Classify("Custom.esm", flags, 0, new string[0], PluginParseStatus.Parsed).SpecialFlags &
				PluginSpecialFlags.Blueprint) != 0, Is.True);
		}

		/// <summary>Official exclusions must not strand BlueprintShips above their parents after an external sort.</summary>
		[Test]
		public void OfficialBlueprintsFollowOrdinaryParentsAndTheirOwnBlueprintMasters()
		{
			var policy = new PluginManagementPolicy { IgnoreOfficialPluginOrder = true, UseStarfieldActivationRules = true };
			policy.AddBlueprintPluginPrefix("BlueprintShips-");
			policy.AddOfficialPlugin("Starfield.esm");
			policy.AddOfficialPlugin("BlueprintShips-Starfield.esm");
			policy.AddOfficialPlugin("SFBGS050.esm");
			policy.AddOfficialPlugin("BlueprintShips-SFBGS050.esm");
			string[] names = { "BlueprintShips-SFBGS050.esm", "Starfield.esm", "BlueprintShips-SFTA03.esm",
				"SFTA03.esm", "BlueprintShips-Starfield.esm", "SFBGS050.esm" };
			var plugins = new List<Plugin>();
			foreach (string name in names)
			{
				var plugin = new Plugin(name, name, null);
				string[] masters = name == "BlueprintShips-SFTA03.esm"
					? new[] { "SFTA03.esm", "BlueprintShips-Starfield.esm" }
					: name == "BlueprintShips-SFBGS050.esm" ? new[] { "SFBGS050.esm" } : new string[0];
				plugin.SetMetadata(policy.Classify(name, PluginHeaderFlags.Master, 0, masters, PluginParseStatus.Parsed));
				plugins.Add(plugin);
			}
			var builder = new PluginSnapshotBuilder();
			List<Plugin> corrected = builder.CorrectStable(policy, plugins);
			CollectionAssert.AreEqual(new[] { "Starfield.esm", "SFTA03.esm", "SFBGS050.esm",
				"BlueprintShips-SFBGS050.esm", "BlueprintShips-Starfield.esm", "BlueprintShips-SFTA03.esm" },
				corrected.Select(plugin => plugin.Filename));
			Assert.That(builder.Build(policy, corrected, new HashSet<Plugin>(corrected)).Diagnostics
				.Any(issue => issue.Kind == PluginValidationIssueKind.MasterBelowDependent), Is.False);
			CollectionAssert.AreEqual(corrected, builder.CorrectStable(policy, corrected));
		}

		/// <summary>Moving official files cannot stale a review, while mod order and official activation remain managed state.</summary>
		[Test]
		public void OfficialOrderFingerprint_IgnoresOnlyGameOwnedOrder()
		{
			CollectionNativeStateIndex first = CreateState(true, true, Plugin("A.esm", 0, true), Official("Official.esm", 1), Plugin("B.esm", 2, true));
			CollectionNativeStateIndex moved = CreateState(true, true, Official("Official.esm", 0), Plugin("A.esm", 1, true), Plugin("B.esm", 2, true));
			Assert.AreEqual(first.Fingerprint, moved.Fingerprint);
			Assert.AreNotEqual(first.Fingerprint, CreateState(true, true, Official("Official.esm", 0), Plugin("B.esm", 1, true), Plugin("A.esm", 2, true)).Fingerprint);
			Assert.AreNotEqual(first.Fingerprint, CreateState(true, true, Official("Official.esm", 0, false), Plugin("A.esm", 1, true), Plugin("B.esm", 2, true)).Fingerprint);
			Assert.AreNotEqual(CreateState(true, Plugin("A.esm", 0, true), Official("Official.esm", 1)).Fingerprint,
				CreateState(true, Official("Official.esm", 0), Plugin("A.esm", 1, true)).Fingerprint);
		}

		/// <summary>Creates a parsed official Blueprint with display indexes derived from its observed position.</summary>
		private static CollectionNativePluginState Official(string name, int priority, bool active = true)
		{
			return new CollectionNativePluginState(name, active, priority, priority, priority.ToString("X2"), PluginParseStatus.Parsed,
				PluginAddressClass.Full, PluginHeaderFlags.Master, PluginSpecialFlags.Official | PluginSpecialFlags.Blueprint,
				true, 0, new string[0], new CollectionNativePluginDiagnostic[0]);
		}

		/// <summary>Reproduces the HeavyBurns Lux group edge across the game's master section.</summary>
		[TestCase(true, true)]
		[TestCase(false, false)]
		public void ReversedGroupPreference_UsesCurrentGameMasterSectionPolicy(bool masterSection, bool expected)
		{
			CollectionNativeStateIndex state = CreateState(masterSection,
				Plugin("Lux - Master plugin.esm", 0, true), Plugin("Lux.esp", 1, false));

			Assert.AreEqual(expected, CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state,
				new[] { "Lux.esp", "Lux - Master plugin.esm" }));
		}

		/// <summary>Master references take precedence regardless of extension and through intermediate dependencies.</summary>
		[Test]
		public void TransitiveMasters_CorrectInvalidPreferenceButStillRejectInvalidNativeOrder()
		{
			CollectionNativeStateIndex valid = CreateState(false, Plugin("Master.esp", 0, false),
				Plugin("Intermediate.esp", 1, false, "Master.esp"), Plugin("Dependent.esp", 2, false, "Intermediate.esp"));
			CollectionNativeStateIndex invalid = CreateState(false, Plugin("Master.esp", 2, false),
				Plugin("Intermediate.esp", 1, false, "Master.esp"), Plugin("Dependent.esp", 0, false, "Intermediate.esp"));

			Assert.IsTrue(CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(valid, new[] { "Dependent.esp", "Master.esp" }));
			Assert.IsFalse(CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(invalid, new[] { "Dependent.esp", "Master.esp" }));
		}

		/// <summary>Ordinary curator order is still required when no mandatory native precedence contradicts it.</summary>
		[Test]
		public void CompatibleOrderAndMissingEndpoints_AreStillVerified()
		{
			CollectionNativeStateIndex state = CreateState(true, Plugin("A.esp", 0, false), Plugin("B.esp", 1, false));

			Assert.IsTrue(CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state, new[] { "A.esp", "B.esp" }));
			Assert.IsFalse(CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state, new[] { "B.esp", "A.esp" }));
			Assert.IsFalse(CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state, new[] { "Missing.esp", "B.esp" }));
		}

		/// <summary>Application and verification agree that ordinary plugins precede their legacy Blueprint master references.</summary>
		[Test]
		public void BlueprintMasterReference_RequiresBlueprintLastWithoutArtificialNativeCycle()
		{
			CollectionNativePluginState blueprint = new CollectionNativePluginState("BlueprintShips-Starfield.esm", true, 2, null,
				String.Empty, PluginParseStatus.Parsed, PluginAddressClass.Full, PluginHeaderFlags.Master,
				PluginSpecialFlags.Blueprint, true, 0, new[] { "Starfield.esm" }, new CollectionNativePluginDiagnostic[0]);
			CollectionNativeStateIndex state = CreateState(true, Plugin("Starfield.esm", 0, true),
				Plugin("AstralGenders.esm", 1, true, "Starfield.esm", "BlueprintShips-Starfield.esm"), blueprint);
			var native = new NativePluginOrderConstraints(new[]
			{
				new NativePluginOrderNode("Starfield.esm", null, true, false),
				new NativePluginOrderNode("AstralGenders.esm", new[] { "Starfield.esm", "BlueprintShips-Starfield.esm" }, true, false),
				new NativePluginOrderNode("BlueprintShips-Starfield.esm", new[] { "Starfield.esm" }, true, true)
			}, true);
			Assert.IsTrue(native.RequiresBefore("AstralGenders.esm", "BlueprintShips-Starfield.esm"));
			Assert.IsFalse(native.RequiresBefore("BlueprintShips-Starfield.esm", "AstralGenders.esm"));
			Assert.IsFalse(native.RequiresBefore("AstralGenders.esm", "AstralGenders.esm"));
			Assert.IsTrue(CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state,
				new[] { "BlueprintShips-Starfield.esm", "AstralGenders.esm" }));
		}

		/// <summary>Blueprint references remain real dependencies even though their ordering diagnostic is advisory.</summary>
		[Test]
		public void BlueprintMasterReference_StrictValidationStillRequiresPresentActiveMaster()
		{
			var policy = new PluginManagementPolicy();
			var blueprint = new Plugin("BlueprintShips-Starfield.esm", "Blueprint", null);
			blueprint.SetMetadata(new PluginMetadata(".esm", PluginHeaderFlags.Master, 0, new string[0], true,
				PluginAddressClass.Full, PluginSpecialFlags.Blueprint, PluginParseStatus.Parsed));
			var ordinary = new Plugin("AstralGenders.esm", "Mod", null);
			ordinary.SetMetadata(new PluginMetadata(".esm", PluginHeaderFlags.Master, 0, new[] { blueprint.Filename }, true,
				PluginAddressClass.Full, PluginSpecialFlags.None, PluginParseStatus.Parsed));
			var builder = new PluginSnapshotBuilder();
			List<Plugin> order = builder.CorrectStable(policy, new[] { blueprint, ordinary });
			Assert.AreEqual(blueprint, order.Last());
			PluginSnapshot valid = builder.Build(policy, order, new HashSet<Plugin>(order), PluginRestrictionMode.Enforced);
			Assert.IsFalse(valid.HasErrors);
			Assert.IsTrue(valid.Diagnostics.Any(x => x.Kind == PluginValidationIssueKind.MasterBelowDependent && x.Severity == PluginValidationSeverity.Warning));
			PluginSnapshot inactive = builder.Build(policy, order, new HashSet<Plugin> { ordinary }, PluginRestrictionMode.Enforced);
			Assert.IsTrue(inactive.Diagnostics.Any(x => x.Kind == PluginValidationIssueKind.InactiveRequiredMaster && x.Severity == PluginValidationSeverity.Error));
			PluginSnapshot missing = builder.Build(policy, new[] { ordinary }, new HashSet<Plugin> { ordinary }, PluginRestrictionMode.Enforced);
			Assert.IsTrue(missing.Diagnostics.Any(x => x.Kind == PluginValidationIssueKind.MissingMaster && x.Severity == PluginValidationSeverity.Error));
		}

		/// <summary>Creates detached metadata without reading a plugin archive or filesystem.</summary>
		private static CollectionNativePluginState Plugin(string name, int priority, bool master, params string[] masters)
		{
			return new CollectionNativePluginState(name, true, priority, null, String.Empty, PluginParseStatus.Parsed,
				PluginAddressClass.Full, master ? PluginHeaderFlags.Master : PluginHeaderFlags.None, PluginSpecialFlags.None,
				master, 0, masters, new CollectionNativePluginDiagnostic[0]);
		}

		/// <summary>Captures explicit per-game ordering policy alongside immutable native metadata.</summary>
		private static CollectionNativeStateIndex CreateState(bool masterSection, params CollectionNativePluginState[] plugins)
		{
			return CreateState(masterSection, false, plugins);
		}

		/// <summary>Captures optional game ownership of official ordering without a persisted setting change.</summary>
		private static CollectionNativeStateIndex CreateState(bool masterSection, bool ignoreOfficialOrder, params CollectionNativePluginState[] plugins)
		{
			return new CollectionNativeStateIndex(CollectionTargetIdentity.FromFingerprint("plugin-order-policy"),
				new CollectionNativeRootState[0], new CollectionNativeModState[0], new CollectionNativeFileState[0],
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], plugins, CollectionNativeStateCoverage.Complete,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0, null, masterSection,
				pluginOrderIgnoresOfficialPlugins: ignoreOfficialOrder);
		}
	}
}
