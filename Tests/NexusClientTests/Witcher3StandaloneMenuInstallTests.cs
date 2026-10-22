using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ChinhDo.Transactions;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Protects standalone menu planning, menu registration and transactional removal.</summary>
	[TestFixture]
	public class Witcher3StandaloneMenuInstallTests
	{
		private const string Menu = @"bin\config\r4game\user_config_matrix\pc\ModSettingsMenuFix.xml";
		private const string Script = @"Mods\modModSettingsMenuFix\content\scripts\local\ModSettingsMenuFix.ws";
		private const string Xml = "<?xml version='1.0' encoding='UTF-16'?><UserConfig><Group id='Menu'><PresetsArray><Preset id='0'><Entry varId='A' value='true'/></Preset></PresetsArray><VisibleVars><Var id='Separator' displayType='SUBTLE_SEPARATOR'/><Var id='A'/><Var id='Separator' displayType='SUBTLE_SEPARATOR'/></VisibleVars></Group></UserConfig>";

		/// <summary>Provides actual selected archive members and independent read-only payload streams.</summary>
		private static IMod Mod(IDictionary<string, byte[]> files)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				if (method.Name == "GetFileList") return files.Keys.ToList();
				if (method.Name == "GetFileStream")
				{
					string path = Path.Combine(Path.GetTempPath(), "nmm-menu-stream-" + Guid.NewGuid().ToString("N"));
					File.WriteAllBytes(path, files[(string)args[0]]);
					return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.DeleteOnClose);
				}
				if (method.Name == "get_Filename") return "ModSettingsMenuFix.zip";
				if (method.Name == "get_ModName") return "Mod Settings Menu Fix";
				return null;
			});
		}

		[TestCase(ModInstallMethod.Direct, "")]
		[TestCase(ModInstallMethod.Virtual, "")]
		[TestCase(ModInstallMethod.Direct, @"package\")]
		[TestCase(ModInstallMethod.Virtual, @"package\")]
		public void CustomMenu_RetainsPresetsAndRegistersExactlyOneMenu(ModInstallMethod method, string wrapper)
		{
			IMod mod = Mod(new Dictionary<string, byte[]> { { wrapper + Script, new byte[0] }, { wrapper + Menu, Encoding.UTF8.GetBytes(Xml) } });
			Witcher3StandaloneMenuInstallPlan plan = Witcher3StandaloneMenuInstallPlan.TryCreate(mod, new ModInstallContext(method, ModInstallRoot.Data));
			Assert.That(plan.ComponentFiles.Single().Key, Is.EqualTo(wrapper + Script));
			Assert.That(plan.ComponentFiles.Single().Value, Is.EqualTo(Script));
			GenerateDataFileOperation menu = plan.Operations.OfType<GenerateDataFileOperation>().Single();
			Assert.That(menu.DestinationPath, Is.EqualTo(Menu));
			Assert.That(menu.PreparationSourcePath, Is.EqualTo(wrapper + Menu));
			using (var stream = new MemoryStream(menu.Data))
			{
				XDocument document = XDocument.Load(stream);
				Assert.That(document.Descendants("Preset").Count(), Is.EqualTo(1));
				Assert.That(document.Descendants("Var").Select(item => (string)item.Attribute("id")), Is.EqualTo(new[] { "Separator", "A", "Separator" }));
			}
			EditGameSpecificValueOperation registration = plan.Operations.OfType<EditGameSpecificValueOperation>().Single();
			Assert.That(Encoding.UTF8.GetString(registration.Value), Is.EqualTo("ModSettingsMenuFix.xml;"));
		}

		[TestCase("audio.xml")]
		[TestCase("display.xml")]
		[TestCase("gameplay.xml")]
		[TestCase("graphics.xml")]
		[TestCase("graphicsdx11.xml")]
		[TestCase("hud.xml")]
		[TestCase("input.xml")]
		public void SharedGameSettings_AreStillRejected(string name)
		{
			IMod mod = Mod(new Dictionary<string, byte[]> { { Script, new byte[0] }, { Witcher3StandaloneMenuInstallPlan.MenuRoot + name, Encoding.UTF8.GetBytes(Xml) } });
			Witcher3InstallException error = Assert.Throws<Witcher3InstallException>(() => Witcher3StandaloneMenuInstallPlan.TryCreate(mod, new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data)));
			Assert.That(error.Message, Does.Contain(name));
			Assert.That(error.Message, Does.Not.Contain("ConfigurationMergeRequired").And.Not.Contain("adapter"));
		}

		[TestCase("<UserConfig>")]
		[TestCase("<!DOCTYPE UserConfig [<!ENTITY external SYSTEM 'file:///missing'>]><UserConfig>&external;</UserConfig>")]
		[TestCase("<SomethingElse/>")]
		public void InvalidMenu_IsRejectedBeforeAnyOperationsAreReturned(string xml)
		{
			IMod mod = Mod(new Dictionary<string, byte[]> { { Script, new byte[0] }, { Menu, Encoding.UTF8.GetBytes(xml) } });
			Assert.Throws<Witcher3InstallException>(() => Witcher3StandaloneMenuInstallPlan.TryCreate(mod, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data)));
		}

		[TestCase(@"Mods\modBad\content\..\escape.ws")]
		[TestCase(@"bin\x64_dx12\witcher3.exe")]
		[TestCase(@"input.settings")]
		public void MenuDoesNotMakeOtherUnsupportedFilesInstallable(string invalid)
		{
			IMod mod = Mod(new Dictionary<string, byte[]> { { Script, new byte[0] }, { Menu, Encoding.UTF8.GetBytes(Xml) }, { invalid, new byte[0] } });
			Assert.Throws<Witcher3InstallException>(() => Witcher3StandaloneMenuInstallPlan.TryCreate(mod, new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data)));
		}

		[Test]
		public void ComponentOnlyArchive_RetainsExistingInstallPath()
		{
			Assert.That(Witcher3StandaloneMenuInstallPlan.TryCreate(Mod(new Dictionary<string, byte[]> { { Script, new byte[0] } }),
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data)), Is.Null);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void MenuList_NativeValueInstallerRestoresOriginalRegistration(bool alreadyListed)
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-standalone-menu-" + Guid.NewGuid().ToString("N"));
			try
			{
				string list = Path.Combine(root, Witcher3StandaloneMenuInstallPlan.MenuRoot, "dx12filelist.txt");
				Directory.CreateDirectory(Path.GetDirectoryName(list));
				File.WriteAllText(list, "hud.xml;\r\nother.xml;\r\n" + (alreadyListed ? "ModSettingsMenuFix.xml;\r\n" : ""), new UTF8Encoding(true));
				byte[] original = File.ReadAllBytes(list);
				IMod mod = Mod(new Dictionary<string, byte[]> { { Script, new byte[0] }, { Menu, Encoding.UTF8.GetBytes(Xml) } });
				EditGameSpecificValueOperation edit = Witcher3StandaloneMenuInstallPlan.TryCreate(mod,
					new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data)).Operations.OfType<EditGameSpecificValueOperation>().Single();
				string owner = null; byte[] previous = null;
				IInstallLog log = InterfaceStub<IInstallLog>.Create((method, args) =>
				{
					if (method.Name == "GetModKey") return "mod-owner";
					if (method.Name == "GetCurrentGameSpecificValueEditOwnerKey") return owner;
					if (method.Name == "LogOriginalGameSpecificValue") previous = (byte[])args[1];
					if (method.Name == "AddGameSpecificValueEdit") owner = "mod-owner";
					if (method.Name == "GetPreviousGameSpecificValue") return previous;
					if (method.Name == "RemoveGameSpecificValueEdit") owner = null;
					return null;
				});
				var store = new Witcher3CollectionRecordStore(root, Path.Combine(root, "mods.settings"));
				using (var transaction = new Nexus.Transactions.TransactionScope())
				{
					var installer = new Witcher3CollectionValueInstaller(mod, log, new TxFileManager(), null, store);
					Assert.That(installer.EditGameSpecificValue(edit.Key, edit.Value), Is.True);
					Assert.That(File.ReadAllText(list).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Count(line => line == "ModSettingsMenuFix.xml;"), Is.EqualTo(1));
					installer.UnEditGameSpecificValue(edit.Key);
					CollectionAssert.AreEqual(original, File.ReadAllBytes(list));
					transaction.Complete();
				}
				using (var transaction = new Nexus.Transactions.TransactionScope())
				{
					var files = new TxFileManager();
					var installer = new Witcher3CollectionValueInstaller(mod, log, files, null, store);
					Assert.That(installer.EditGameSpecificValue(edit.Key, edit.Value), Is.True);
					string menu = Path.Combine(root, Menu); files.Snapshot(menu);
					File.WriteAllText(menu, Xml);
					// No Complete: simulate failure after menu registration and file generation.
				}
				CollectionAssert.AreEqual(original, File.ReadAllBytes(list));
				Assert.That(File.Exists(Path.Combine(root, Menu)), Is.False);
			}
			finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
		}
	}
}
