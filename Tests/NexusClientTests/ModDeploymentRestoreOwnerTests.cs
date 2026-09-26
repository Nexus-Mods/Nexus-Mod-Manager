using System;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.10b1 native retained-owner input contract tests.</summary>
	[TestFixture]
	public class ModDeploymentRestoreOwnerTests
	{
		[Test]
		public void OriginalValue_RejectsManagedModReference()
		{
			IMod mod = new InstallLog.DummyMod("Managed", "Managed.7z");
			Assert.Throws<ArgumentException>(() => new ModDeploymentRestoreOwner("original-values",
				ModDeploymentRestoreOwnerKind.OriginalValue, mod, ModInstallRoot.Data, "payload.bin"));
		}

		[Test]
		public void ManagedOwner_RequiresLiveModReference()
		{
			Assert.Throws<ArgumentNullException>(() => new ModDeploymentRestoreOwner("native-a",
				ModDeploymentRestoreOwnerKind.Direct, null, ModInstallRoot.Data, "payload.bin"));
			Assert.Throws<ArgumentNullException>(() => new ModDeploymentRestoreOwner("native-a",
				ModDeploymentRestoreOwnerKind.Virtual, null, ModInstallRoot.Data, "payload.bin"));
		}

		[Test]
		public void ManagedOwner_PreservesExactRemappedContextAndPayloadPath()
		{
			IMod mod = new InstallLog.DummyMod("Managed", "Managed.7z");
			var owner = new ModDeploymentRestoreOwner("native-remapped", ModDeploymentRestoreOwnerKind.Virtual,
				mod, ModInstallRoot.GameRoot, "retained.payload");

			Assert.AreEqual("native-remapped", owner.OwnerKey);
			Assert.AreEqual(ModDeploymentRestoreOwnerKind.Virtual, owner.Kind);
			Assert.AreSame(mod, owner.Mod);
			Assert.AreEqual(ModInstallRoot.GameRoot, owner.InstallRoot);
			Assert.AreEqual("retained.payload", owner.PayloadPath);
		}
	}
}
