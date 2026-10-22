using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Games.Gamebryo.PluginManagement.LoadOrder;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Checks startup fallbacks without initializing a real game, watchers or plugin-state writers.</summary>
	[TestFixture]
	public class PluginStartupStateTests
	{
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
