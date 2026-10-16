using System;
using System.Linq;
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
			return new CollectionNativeStateIndex(CollectionTargetIdentity.FromFingerprint("plugin-order-policy"),
				new CollectionNativeRootState[0], new CollectionNativeModState[0], new CollectionNativeFileState[0],
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], plugins, CollectionNativeStateCoverage.Complete,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0, null, masterSection);
		}
	}
}
