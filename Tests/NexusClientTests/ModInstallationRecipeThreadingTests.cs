using System;
using System.Reflection;
using System.Runtime.Serialization;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C5.2 carries the optional installation recipe input through native installer construction without changing manual behavior.
	/// </summary>
	[TestFixture]
	public class ModInstallationRecipeThreadingTests
	{
		/// <summary>
		/// Verifies the factory preserves an explicit recipe input without moving C3 operation-identity assignment below ModManager.
		/// </summary>
		[Test]
		public void Factory_ExplicitRecipeReachesInstaller()
		{
			ReadOnlyObservableList<IMod> activeMods;
			IInstallLog installLog = CreateInstallLog(out activeMods);
			ModInstallerFactory factory = CreateFactory(installLog);
			IMod mod = CreateMod("Recipe mod", "recipe.7z", "1.0");
			ModInstallationRecipeInput recipeInput = CreateRecipeInput(ModInstallMethod.Virtual, ModInstallRoot.Data);

			ModInstaller installer = factory.CreateInstaller(mod, null, activeMods, recipeInput.InstallContext, recipeInput);

			Assert.That(GetRecipeInput(installer), Is.SameAs(recipeInput));
			Assert.That(installer.OperationIdentity, Is.Null, "Factory construction must not bypass the ModManager/C3 identity-assignment boundary.");
		}

		/// <summary>
		/// Verifies a validated recipe cannot enter a native installer whose actual method/root differs from its captured context.
		/// </summary>
		[Test]
		public void Factory_RejectsRecipeContextMismatch()
		{
			ReadOnlyObservableList<IMod> activeMods;
			IInstallLog installLog = CreateInstallLog(out activeMods);
			ModInstallerFactory factory = CreateFactory(installLog);
			IMod mod = CreateMod("Recipe mod", "recipe-context.7z", "1.0");
			ModInstallationRecipeInput recipeInput = CreateRecipeInput(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var differentContext = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.GameRoot);

			Assert.Throws<ArgumentException>(() => factory.CreateInstaller(mod, null, activeMods, differentContext, recipeInput));
		}

		/// <summary>
		/// Verifies the upgrade constructor shares the same context guard instead of silently carrying a recipe across a context change.
		/// </summary>
		[Test]
		public void Factory_UpgradeRejectsRecipeContextMismatch()
		{
			ReadOnlyObservableList<IMod> activeMods;
			IInstallLog installLog = CreateInstallLog(out activeMods);
			ModInstallerFactory factory = CreateFactory(installLog);
			IMod oldMod = CreateMod("Recipe mod", "recipe-old.7z", "1.0");
			IMod newMod = CreateMod("Recipe mod", "recipe-new.7z", "2.0");
			ModInstallationRecipeInput recipeInput = CreateRecipeInput(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var differentContext = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.GameRoot);

			Assert.Throws<ArgumentException>(() => factory.CreateUpgradeInstaller(oldMod, newMod, null, differentContext, recipeInput));
		}

		/// <summary>
		/// Verifies existing recipe-less factory construction remains recipe-less and does not acquire a new operation identity.
		/// </summary>
		[Test]
		public void Factory_ExistingManualOverloadRemainsRecipeLess()
		{
			ReadOnlyObservableList<IMod> activeMods;
			IInstallLog installLog = CreateInstallLog(out activeMods);
			ModInstallerFactory factory = CreateFactory(installLog);
			IMod mod = CreateMod("Manual mod", "manual.7z", "1.0");
			var installContext = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);

			ModInstaller installer = factory.CreateInstaller(mod, null, activeMods, installContext);

			Assert.That(GetRecipeInput(installer), Is.Null);
			Assert.That(installer.OperationIdentity, Is.Null);
		}

		/// <summary>
		/// Verifies the activation upgrade branch carries the same recipe input into the ModUpgrader rather than dropping it.
		/// </summary>
		[Test]
		public void Activator_UpgradeBranchPreservesRecipeInput()
		{
			var activeList = new ThreadSafeObservableList<IMod>();
			IMod oldMod = CreateMod("Shared mod", "old-version.7z", "1.0");
			IMod newMod = CreateMod("Shared mod", "new-version.7z", "2.0");
			activeList.Add(oldMod);
			var activeMods = new ReadOnlyObservableList<IMod>(activeList);
			IInstallLog installLog = CreateInstallLog(activeMods);
			var activator = new ModActivator(installLog, CreateFactory(installLog));
			ModInstallationRecipeInput recipeInput = CreateRecipeInput(ModInstallMethod.Virtual, ModInstallRoot.Data);

			var installer = (ModInstaller)activator.Activate(newMod, (oldVersion, newVersion) => ConfirmUpgradeResult.Upgrade,
				null, activeMods, false, recipeInput.InstallContext, true, recipeInput);

			Assert.That(installer, Is.TypeOf<ModUpgrader>());
			Assert.That(GetRecipeInput(installer), Is.SameAs(recipeInput));
			Assert.That(installer.OperationIdentity, Is.Null, "Activator construction must leave operation identity assignment to ModManager.");
		}

		/// <summary>
		/// Verifies ModManager carries an explicit recipe input through activation without replacing its collection-owned operation identity.
		/// </summary>
		[Test]
		public void ModManager_RecipeActivationPreservesRecipeIdentity()
		{
			ReadOnlyObservableList<IMod> activeMods;
			IInstallLog installLog = CreateInstallLog(out activeMods);
			ModActivator activator = new ModActivator(installLog, CreateFactory(installLog));
			ModManager manager = CreateManagerShell(installLog, activator);
			IMod mod = CreateMod("Collection mod", "collection.7z", "1.0");
			ModInstallationRecipeInput recipeInput = CreateRecipeInput(ModInstallMethod.Virtual, ModInstallRoot.Data);

			var installer = (ModInstaller)manager.ActivateMod(mod, (oldVersion, newVersion) => ConfirmUpgradeResult.NormalActivation,
				null, activeMods, recipeInput.InstallContext, true, recipeInput);

			Assert.That(GetRecipeInput(installer), Is.SameAs(recipeInput));
			Assert.That(installer.OperationIdentity, Is.SameAs(recipeInput.OperationIdentity));
			Assert.That(installer.OperationIdentity.Origin, Is.EqualTo(ModOperationOrigin.Collection));
		}

		/// <summary>
		/// Verifies C6.7 can construct an explicit unstarted upgrade while preserving the prepared recipe operation identity.
		/// </summary>
		[Test]
		public void ModManager_CreateUpgradeModOperation_PreservesRecipeIdentityWithoutStart()
		{
			IMod oldMod = CreateMod("Collection mod", "collection-old.7z", "1.0");
			IMod newMod = CreateMod("Collection mod", "collection-new.7z", "2.0");
			var activeList = new ThreadSafeObservableList<IMod>();
			activeList.Add(oldMod);
			var activeMods = new ReadOnlyObservableList<IMod>(activeList);
			IInstallLog installLog = CreateInstallLog(activeMods);
			ModInstallerFactory factory = CreateFactory(installLog);
			ModManager manager = CreateManagerShell(installLog, new ModActivator(installLog, factory));
			SetField(manager, "<InstallerFactory>k__BackingField", factory);
			ModInstallationRecipeInput recipeInput = CreateRecipeInput(ModInstallMethod.Virtual, ModInstallRoot.Data);

			var operation = (ModInstaller)manager.CreateUpgradeModOperation(oldMod, newMod, null,
				recipeInput.InstallContext, recipeInput);

			Assert.That(operation, Is.TypeOf<ModUpgrader>());
			Assert.That(GetRecipeInput(operation), Is.SameAs(recipeInput));
			Assert.That(operation.OperationIdentity, Is.SameAs(recipeInput.OperationIdentity));
			Assert.That(operation.IsCompleted, Is.False);
		}

		/// <summary>
		/// Verifies existing ModManager activation still creates a manual recipe-less operation through the old overload.
		/// </summary>
		[Test]
		public void ModManager_ExistingManualActivationRemainsRecipeLess()
		{
			ReadOnlyObservableList<IMod> activeMods;
			IInstallLog installLog = CreateInstallLog(out activeMods);
			ModActivator activator = new ModActivator(installLog, CreateFactory(installLog));
			ModManager manager = CreateManagerShell(installLog, activator);
			IMod mod = CreateMod("Manual mod", "manual-manager.7z", "1.0");
			var installContext = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);

			var installer = (ModInstaller)manager.ActivateMod(mod, (oldVersion, newVersion) => ConfirmUpgradeResult.NormalActivation,
				null, activeMods, installContext, false);

			Assert.That(GetRecipeInput(installer), Is.Null);
			Assert.That(installer.OperationIdentity, Is.Not.Null);
			Assert.That(installer.OperationIdentity.Origin, Is.EqualTo(ModOperationOrigin.Manual));
			Assert.That(installer.OperationIdentity.Fingerprint.RecipeFingerprint, Is.Null);
		}

		/// <summary>
		/// Creates the immutable explicit recipe input used by C5.2 threading tests.
		/// </summary>
		private static ModInstallationRecipeInput CreateRecipeInput(ModInstallMethod p_mimMethod, ModInstallRoot p_mirRoot)
		{
			var context = new ModInstallContext(p_mimMethod, p_mirRoot);
			var fingerprint = new ModOperationFingerprint("target-sha256:" + new string('b', 64), context, "recipe:c5.2-v1");
			var validation = new ModInstallationRecipeValidation(
				"nmm-ce.test", 1, context, new ModInstallationRecipeExpectedContent(new string('c', 64), 4096),
				new[] { new ModInstallationRecipeCapability("simple-file", 1) },
				new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"meshes\body.nif") });
			return new ModInstallationRecipeInput(ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint), validation);
		}

		/// <summary>
		/// Creates an empty install log and returns its read-only active-mod view.
		/// </summary>
		private static IInstallLog CreateInstallLog(out ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			var activeMods = new ThreadSafeObservableList<IMod>();
			p_rolActiveMods = new ReadOnlyObservableList<IMod>(activeMods);
			return CreateInstallLog(p_rolActiveMods);
		}

		/// <summary>
		/// Creates a lightweight install-log proxy exposing the supplied active-mod view.
		/// </summary>
		private static IInstallLog CreateInstallLog(ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			return InterfaceStub<IInstallLog>.Create((method, args) => method.Name == "get_ActiveMods" ? p_rolActiveMods : null);
		}

		/// <summary>
		/// Creates a factory sufficient for constructor-only Virtual install tests.
		/// </summary>
		private static ModInstallerFactory CreateFactory(IInstallLog p_ilgInstallLog)
		{
			return new ModInstallerFactory(null, null, null, null, p_ilgInstallLog, null, null, null);
		}

		/// <summary>
		/// Creates a minimal mod proxy with repository identity sufficient for normal and upgrade construction paths.
		/// </summary>
		private static IMod CreateMod(string p_strName, string p_strFilename, string p_strVersion)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Id":
						return "42";
					case "get_DownloadId":
						return "84";
					case "get_ModName":
						return p_strName;
					case "get_Filename":
					case "get_ModArchivePath":
						return p_strFilename;
					case "get_HumanReadableVersion":
						return p_strVersion;
					default:
						return null;
				}
			});
		}

		/// <summary>
		/// Creates a minimal ModManager shell with the real activation path but without application bootstrap.
		/// </summary>
		private static ModManager CreateManagerShell(IInstallLog p_ilgInstallLog, ModActivator p_macActivator)
		{
			var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
			SetField(manager, "<InstallationLog>k__BackingField", p_ilgInstallLog);
			SetField(manager, "m_macModActivator", p_macActivator);
			return manager;
		}

		/// <summary>
		/// Reads the protected recipe input without widening the production API surface for tests.
		/// </summary>
		private static ModInstallationRecipeInput GetRecipeInput(ModInstaller p_minInstaller)
		{
			PropertyInfo property = typeof(ModInstaller).GetProperty("InstallationRecipeInput", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(property, Is.Not.Null);
			return (ModInstallationRecipeInput)property.GetValue(p_minInstaller, null);
		}

		/// <summary>
		/// Assigns one private field on a lightweight test shell.
		/// </summary>
		private static void SetField(object p_objTarget, string p_strFieldName, object p_objValue)
		{
			FieldInfo field = p_objTarget.GetType().GetField(p_strFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, "Missing field: " + p_strFieldName);
			field.SetValue(p_objTarget, p_objValue);
		}
	}
}
