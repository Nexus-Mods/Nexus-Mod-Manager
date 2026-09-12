namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;

	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;

	using NUnit.Framework;

	/// <summary>
	/// Verifies the Step 2 method-neutral coordinator keeps pure Virtual ownership on VMA while honoring promoted registry authority.
	/// </summary>
	[TestFixture]
	public class DirectInstallDeploymentCoordinatorTests
	{
		[Test]
		public void PureVirtualTarget_DelegatesOwnershipToVmaWithoutPromotion()
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"textures\foo.dds");
			int virtualOwnerQueries = 0;
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
			{
				if (method.Name == "IsDeploymentTargetPromoted")
					return false;
				return null;
			});
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
			{
				if (method.Name == "GetVirtualOwnerKeys")
				{
					virtualOwnerQueries++;
					return new[] { "VirtualA", "VirtualB" };
				}
				return null;
			});
			var manager = new ModDeploymentManager(installLog, virtualModActivator);

			CollectionAssert.AreEqual(new[] { "VirtualA", "VirtualB" }, manager.GetOwnerKeys(target));
			Assert.AreEqual("VirtualB", manager.GetCurrentOwnerKey(target));
			Assert.AreEqual(2, virtualOwnerQueries);
			Assert.IsFalse(manager.IsPromoted(target));
		}

		[Test]
		public void PromotedTarget_UsesInstallLogAsAuthorityWithoutVmaOwnerLookup()
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.GameRoot, "loader.dll");
			int virtualOwnerQueries = 0;
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
			{
				if (method.Name == "IsDeploymentTargetPromoted")
					return true;
				if (method.Name == "GetDeploymentOwnerKeys")
					return new[] { "VirtualA", "DirectB" };
				return null;
			});
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
			{
				if (method.Name == "GetVirtualOwnerKeys")
					virtualOwnerQueries++;
				return null;
			});
			var manager = new ModDeploymentManager(installLog, virtualModActivator);

			CollectionAssert.AreEqual(new[] { "VirtualA", "DirectB" }, manager.GetOwnerKeys(target));
			Assert.AreEqual("DirectB", manager.GetCurrentOwnerKey(target));
			Assert.AreEqual(0, virtualOwnerQueries);
			Assert.IsTrue(manager.IsPromoted(target));
		}

		[Test]
		public void HasManagedFiles_PrefersSparseRegistryAndFallsBackToVmaForPureVirtualMods()
		{
			var promotedMod = new InstallLog.DummyMod("Promoted", "Promoted.7z");
			var virtualMod = new InstallLog.DummyMod("Virtual", "Virtual.7z");
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"meshes\foo.nif");
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
			{
				if (method.Name == "GetModKey")
					return ReferenceEquals(args[0], promotedMod) ? "PromotedKey" : "VirtualKey";
				if (method.Name == "GetDeploymentTargetsForMod")
					return string.Equals((string)args[0], "PromotedKey", StringComparison.OrdinalIgnoreCase)
						? (IReadOnlyCollection<ModDeploymentTarget>)new[] { target }
						: new ModDeploymentTarget[0];
				return null;
			});
			int virtualChecks = 0;
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
			{
				if (method.Name == "CheckHasActiveLinks")
				{
					virtualChecks++;
					return ReferenceEquals(args[0], virtualMod);
				}
				return null;
			});
			var manager = new ModDeploymentManager(installLog, virtualModActivator);

			Assert.IsTrue(manager.HasManagedFiles(promotedMod));
			Assert.AreEqual(0, virtualChecks, "Promoted target lookup should not require a VMA scan/check.");
			Assert.IsTrue(manager.HasManagedFiles(virtualMod));
			Assert.AreEqual(1, virtualChecks);
		}
	}
}
