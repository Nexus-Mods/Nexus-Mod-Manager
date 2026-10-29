using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Games.Gamebryo.PluginManagement.LoadOrder;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Checks startup fallbacks without initializing a real game, watchers or plugin-state writers.</summary>
	[TestFixture]
	public class PluginStartupStateTests
	{
		/// <summary>Ship blueprints follow installed parents of every supported extension, independently of old explicit markers.</summary>
		[TestCase("esm", true)]
		[TestCase("esp", true)]
		[TestCase("esl", true)]
		[TestCase("esm", false)]
		public void StarfieldPolicy_DerivesBlueprintStateFromInstalledParent(string extension, bool parentActive)
		{
			var policy = new PluginManagementPolicy { UseStarfieldActivationRules = true };
			policy.AddOfficialPlugin("Official.esm");
			policy.AddOfficialPlugin("BlueprintShips-SFTA06.esm");
			string[] installed = { "Official.esm", "sfta03." + extension, "BlueprintShips-SFTA03.esm", "BlueprintShips-SFTA06.esm", "FlagOnly.esm" };
			string[] requested = parentActive ? new[] { "SFTA03." + extension, "BlueprintShips-SFTA06.esm" } : new[] { "BlueprintShips-SFTA03.esm", "BlueprintShips-SFTA06.esm" };
			HashSet<string> active = policy.ResolveActivePluginNames(installed, requested);
			Assert.That(active.Contains("BlueprintShips-SFTA03.esm"), Is.EqualTo(parentActive));
			Assert.That(active.Contains("BlueprintShips-SFTA06.esm"), Is.False);
			Assert.That(active.Contains("Official.esm"), Is.True);
			Assert.That(policy.IsPluginActivationManagedByGame("FlagOnly.esm"), Is.False);
			Assert.That(policy.IsImplicitBlueprintShipsPlugin("BlueprintShips-SFTA03.esp"), Is.False);
			Assert.That(policy.ResolveActivePluginNames(new[] { "BlueprintShips-SFTA03.esm" }, new[] { "SFTA03.esm" })
				.Contains("BlueprintShips-SFTA03.esm"), Is.False, "A missing parent cannot activate an installed blueprint.");
			policy.UseStarfieldActivationRules = false;
			Assert.That(policy.ResolveActivePluginNames(installed, new[] { "BlueprintShips-SFTA03.esm" }),
				Is.EquivalentTo(new[] { "BlueprintShips-SFTA03.esm" }), "Other games retain their explicit activation behavior.");
		}

		/// <summary>Startup and both queued writers retain derived active state while excluding game-owned entries from plugins.txt.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void StarfieldBackend_ReadsEffectiveStateAndOmitsGameManagedWrites(bool missingPluginsFile)
		{
			string root = Path.Combine(Path.GetTempPath(), "NmmStarfieldActivation-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				string[] names = { "Starfield.esm", "CCC-Official.esm", "SFTA03.esm", "SFTA06.esm", "BlueprintShips-Starfield.esm", "BlueprintShips-SFTA03.esm", "BlueprintShips-SFTA06.esm" };
				foreach (string name in names) File.WriteAllText(Path.Combine(root, name), "test");
				string pluginsFile = Path.Combine(root, "plugins.txt");
				if (!missingPluginsFile) File.WriteAllLines(pluginsFile, new[] { "*SFTA03.esm", "*BlueprintShips-SFTA06.esm" });
				var policy = new PluginManagementPolicy { UseStarfieldActivationRules = true };
				policy.AddCriticalPlugin("Starfield.esm");
				policy.AddOfficialPlugin("CCC-Official.esm");
				policy.AddOfficialPlugin("BlueprintShips-SFTA06.esm");
				IGameModeDescriptor descriptor = InterfaceStub<IGameModeDescriptor>.Create((method, args) =>
				{
					if (method.Name == "get_PluginDirectory") return root;
					if (method.Name == "get_OrderedCriticalPluginNames") return new[] { Path.Combine(root, "Starfield.esm") };
					if (method.Name == "get_OrderedOfficialUnmanagedPluginNames") return new[] { Path.Combine(root, "CCC-Official.esm"), Path.Combine(root, "BlueprintShips-SFTA06.esm") };
					if (method.Name.StartsWith("get_Ordered", StringComparison.Ordinal)) return new string[0];
					return null;
				});
				var mode = (DataDrivenGamebryoGameMode)FormatterServices.GetUninitializedObject(typeof(DataDrivenGamebryoGameMode));
				typeof(GameModeBase).GetField("m_gmdGameModeInfo", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode, descriptor);
				typeof(DataDrivenGamebryoGameMode).GetField("_pluginManagementPolicy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode, policy);
				var manager = (PluginOrderManager)FormatterServices.GetUninitializedObject(typeof(PluginOrderManager));
				GC.SuppressFinalize(manager);
				SetField(manager, "<GameMode>k__BackingField", mode);
				SetField(manager, "<PluginsFilePath>k__BackingField", pluginsFile);
				SetField(manager, "<LoadOrderFilePath>k__BackingField", pluginsFile);
				SetField(manager, "<SingleFileManagement>k__BackingField", true);
				SetField(manager, "<IgnoreOfficialPlugins>k__BackingField", true);
				SetField(manager, "<LastValidActiveList>k__BackingField", new List<string> { Path.Combine(root, "SFTA03.esm") });
				SetField(manager, "<LastValidLoadOrder>k__BackingField", names.Select(name => Path.Combine(root, name)).ToList());
				SetField(manager, "Fallout4PluginManagement", true);
				SetField(manager, "m_booUseStarfieldActivationRules", true);
				SetField(manager, "m_rgxPluginFile", new Regex(@"(?i)^.+\.es[mpl]$"));
				SetField(manager, "m_objActivePluginCacheLock", new object());
				SetField(manager, "m_hstActivePluginNames", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
				var queued = new ThreadSafeObservableList<WriteLoadOrderTask>();
				SetField(manager, "TaskList", queued);

				CollectionAssert.AreEquivalent(new[] { "Starfield.esm", "CCC-Official.esm", "SFTA03.esm", "BlueprintShips-Starfield.esm", "BlueprintShips-SFTA03.esm" },
					manager.GetActivePlugins().Select(Path.GetFileName));
				Assert.That(manager.IsPluginActive("BlueprintShips-SFTA03.esm"), Is.True);
				Assert.That(manager.IsPluginActive("BlueprintShips-SFTA06.esm"), Is.False);
				manager.SetActivePlugins(new[] { Path.Combine(root, "SFTA03.esm"), Path.Combine(root, "BlueprintShips-SFTA06.esm") });
				AssertQueuedStarfieldWrite(queued.Single());
				manager.SetLoadOrder(names.Select(name => Path.Combine(root, name)).ToArray());
				AssertQueuedStarfieldWrite(queued.Single());
				Assert.That(File.Exists(pluginsFile), Is.EqualTo(!missingPluginsFile), "The isolated writer queue must not execute.");
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Post-LOOT reading restores timestamp-ordered implicit Blueprints without changing other games or files.</summary>
		[TestCase(true)]
		[TestCase(false)]
		public void StarfieldBlueprintReadOrderUsesFinalTimestampSection(bool starfield)
		{
			string root = Path.Combine(Path.GetTempPath(), "NmmBlueprintOrder-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try
			{
				var policy = new PluginManagementPolicy { UseStarfieldActivationRules = starfield };
				policy.AddBlueprintPluginPrefix("BlueprintShips-");
				var mode = (DataDrivenGamebryoGameMode)FormatterServices.GetUninitializedObject(typeof(DataDrivenGamebryoGameMode));
				typeof(DataDrivenGamebryoGameMode).GetField("_pluginManagementPolicy", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode, policy);
				var manager = (PluginOrderManager)FormatterServices.GetUninitializedObject(typeof(PluginOrderManager));
				GC.SuppressFinalize(manager);
				SetField(manager, "<GameMode>k__BackingField", mode);
				SetField(manager, "m_booUseStarfieldActivationRules", starfield);
				string[] names = { "BlueprintShips-SFBGS050.esm", "SFBGS050.esm", "BlueprintShips-SFTA03.esm", "SFTA03.esm" };
				List<string> order = names.Select(name => Path.Combine(root, name)).ToList();
				foreach (string path in order) File.WriteAllText(path, "test");
				DateTime earlier = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
				File.SetLastWriteTimeUtc(order[0], earlier.AddMinutes(1));
				File.SetLastWriteTimeUtc(order[2], earlier);
				var timestamps = order.ToDictionary(path => path, File.GetLastWriteTimeUtc);
				var method = typeof(PluginOrderManager).GetMethod("RestoreStarfieldBlueprintSection", BindingFlags.NonPublic | BindingFlags.Instance);
				method.Invoke(manager, new object[] { order });
				string[] expected = starfield ? new[] { "SFBGS050.esm", "SFTA03.esm", "BlueprintShips-SFTA03.esm", "BlueprintShips-SFBGS050.esm" } : names;
				CollectionAssert.AreEqual(expected, order.Select(Path.GetFileName));
				method.Invoke(manager, new object[] { order });
				CollectionAssert.AreEqual(expected, order.Select(Path.GetFileName));
				Assert.That(timestamps.All(pair => File.GetLastWriteTimeUtc(pair.Key) == pair.Value), Is.True);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Checks the real writer payload without starting its background task.</summary>
		private static void AssertQueuedStarfieldWrite(WriteLoadOrderTask task)
		{
			string[] lines = (string[])typeof(WriteLoadOrderTask).GetProperty("Plugins", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(task);
			Assert.That(lines.Any(line => line != null && line.IndexOf("BlueprintShips-", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
			Assert.That(lines.Any(line => line != null && line.IndexOf("CCC-Official.esm", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
			Assert.That(lines, Does.Contain("*SFTA03.esm"));
			Assert.That(lines, Does.Contain("SFTA06.esm"));
		}

		/// <summary>Missing state immediately retains the last valid lists without creating or overwriting files.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void MissingPluginState_UsesLastValidListsWithoutRetryDelay(bool missingDirectory)
		{
			string root = Path.Combine(Path.GetTempPath(), "NmmPluginStartup-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			PluginOrderManager manager = (PluginOrderManager)FormatterServices.GetUninitializedObject(typeof(PluginOrderManager));
			GC.SuppressFinalize(manager);
			try
			{
				string plugin = Path.Combine(root, "Example.esp");
				File.WriteAllText(plugin, "test");
				string stateDirectory = missingDirectory ? Path.Combine(root, "missing") : root;
				string pluginsFile = Path.Combine(stateDirectory, "plugins.txt");
				string loadOrderFile = Path.Combine(stateDirectory, "loadorder.txt");
				IGameModeDescriptor descriptor = InterfaceStub<IGameModeDescriptor>.Create((method, args) =>
				{
					if (method.Name == "get_PluginDirectory") return root;
					if (method.Name.StartsWith("get_Ordered", StringComparison.Ordinal)) return new string[0];
					return null;
				});
				DataDrivenGamebryoGameMode mode = (DataDrivenGamebryoGameMode)FormatterServices.GetUninitializedObject(typeof(DataDrivenGamebryoGameMode));
				typeof(GameModeBase).GetField("m_gmdGameModeInfo", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode, descriptor);
				SetField(manager, "<GameMode>k__BackingField", mode);
				SetField(manager, "<PluginsFilePath>k__BackingField", pluginsFile);
				SetField(manager, "<LoadOrderFilePath>k__BackingField", loadOrderFile);
				SetField(manager, "<LastValidActiveList>k__BackingField", new List<string> { plugin });
				SetField(manager, "<LastValidLoadOrder>k__BackingField", new List<string> { plugin });
				SetField(manager, "m_objActivePluginCacheLock", new object());
				SetField(manager, "m_hstActivePluginNames", new HashSet<string>(StringComparer.OrdinalIgnoreCase));

				Stopwatch timer = Stopwatch.StartNew();
				CollectionAssert.AreEqual(new[] { plugin }, manager.GetActivePlugins());
				CollectionAssert.AreEqual(new[] { plugin }, manager.GetLoadOrder());
				CollectionAssert.AreEqual(new[] { plugin }, (List<string>)typeof(PluginOrderManager)
					.GetMethod("GetActiveList", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, null));
				Assert.Less(timer.Elapsed.TotalSeconds, 2, "Missing files must not enter the 10.5-second lock retries.");
				Assert.IsFalse(File.Exists(pluginsFile));
				Assert.IsFalse(File.Exists(loadOrderFile));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Existing locked files still report unavailable rather than empty and retain the recovery path.</summary>
		[Test]
		public void LockedPluginState_IsNotTreatedAsEmpty()
		{
			string path = Path.GetTempFileName();
			try
			{
				File.WriteAllText(path, "Example.esp");
				using (FileStream locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
					object ready = typeof(PluginOrderManager).GetMethod("IsFileReady", BindingFlags.NonPublic | BindingFlags.Static)
						.Invoke(null, new object[] { path, false, true });
					Assert.AreEqual(false, ready);
				}
				Assert.AreEqual("Example.esp", File.ReadAllText(path));
			}
			finally { File.Delete(path); }
		}

		/// <summary>Cached active masters bypass the repeated official-list path resolution.</summary>
		[TestCase("Example.esp")]
		[TestCase("EXAMPLE.ESP")]
		public void CachedActivePlugin_DoesNotResolveOfficialPluginLists(string plugin)
		{
			int officialListReads = 0;
			PluginOrderManager manager = CreateActiveStateManager(property =>
			{
				if (property.StartsWith("get_Ordered", StringComparison.Ordinal))
				{
					officialListReads++;
					return new[] { "Base.esm" };
				}
				return null;
			});
			SetField(manager, "m_hstActivePluginNames", new HashSet<string>(new[] { "Example.esp" }, StringComparer.OrdinalIgnoreCase));
			Assert.IsTrue(manager.IsPluginActive(plugin));
			Assert.AreEqual(0, officialListReads);
			Assert.IsTrue(manager.IsPluginActive(Path.Combine(Path.GetTempPath(), plugin)));
			Assert.AreEqual(0, officialListReads);
		}

		/// <summary>Implicit official activation and cache removal still use the existing per-game rules.</summary>
		[TestCase("Base.esm", true)]
		[TestCase("Official.esm", true)]
		[TestCase("Creation.esl", true)]
		[TestCase("Disabled.esp", false)]
		public void ActiveCacheMiss_PreservesImplicitOfficialActivation(string plugin, bool expected)
		{
			PluginOrderManager manager = CreateActiveStateManager(property =>
			{
				if (property == "get_OrderedCriticalPluginNames") return new[] { "Base.esm" };
				if (property == "get_OrderedOfficialPluginNames") return new[] { "Official.esm" };
				if (property == "get_OrderedOfficialUnmanagedPluginNames") return new[] { "Creation.esl" };
				return null;
			});
			Assert.AreEqual(expected, manager.IsPluginActive(plugin));
			SetField(manager, "m_hstActivePluginNames", new HashSet<string>(new[] { "Disabled.esp" }, StringComparer.OrdinalIgnoreCase));
			Assert.IsTrue(manager.IsPluginActive("Disabled.esp"));
			typeof(PluginOrderManager).GetMethod("UpdateActivePluginCache", BindingFlags.NonPublic | BindingFlags.Instance)
				.Invoke(manager, new object[] { new string[0] });
			Assert.IsFalse(manager.IsPluginActive("Disabled.esp"));
			Assert.AreEqual(expected, manager.IsPluginActive(plugin));
		}

		/// <summary>Creates an in-memory lookup without reading settings, starting watchers or creating game folders.</summary>
		private static PluginOrderManager CreateActiveStateManager(Func<string, object> values)
		{
			IGameModeDescriptor descriptor = InterfaceStub<IGameModeDescriptor>.Create((method, args) =>
				method.Name == "get_PluginDirectory" ? Path.GetTempPath() : values(method.Name));
			DataDrivenGamebryoGameMode mode = (DataDrivenGamebryoGameMode)FormatterServices.GetUninitializedObject(typeof(DataDrivenGamebryoGameMode));
			typeof(GameModeBase).GetField("m_gmdGameModeInfo", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode, descriptor);
			PluginOrderManager manager = (PluginOrderManager)FormatterServices.GetUninitializedObject(typeof(PluginOrderManager));
			GC.SuppressFinalize(manager);
			SetField(manager, "<GameMode>k__BackingField", mode);
			SetField(manager, "Fallout4PluginManagement", true);
			SetField(manager, "m_objActivePluginCacheLock", new object());
			SetField(manager, "m_hstActivePluginNames", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
			SetField(manager, "m_booActivePluginCacheInitialized", true);
			return manager;
		}

		/// <summary>Sets only the startup state required by the real read/fallback methods.</summary>
		private static void SetField(PluginOrderManager manager, string name, object value)
		{
			typeof(PluginOrderManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, value);
		}
	}
}
