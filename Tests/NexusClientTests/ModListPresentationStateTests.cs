namespace NexusClientTests
{
	using System;
	using System.IO;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.ModManagement.UI;
	using Nexus.Client.Mods;
	using NUnit.Framework;

	/// <summary>Checks the shared status snapshot independently of GridView, TreeList and actual mod installations.</summary>
	[TestFixture]
	public sealed class ModListPresentationStateTests
	{
		/// <summary>Replacing a live nested archive with its persisted InstallLog identity must not erase the installed/active status.</summary>
		[Test]
		public void NestedArchiveStatusSurvivesReloadAndDisable()
		{
			string archive = Path.Combine(Path.GetTempPath(), "nmm-status-fixture", "Outer.zip");
			IMod managed = Mod(archive, "Nested.fomod");
			IMod restored = new InstallLog.DummyMod("Restored", archive);
			var state = new ModListPresentationState();
			state.RebuildActivationState(new[] { managed }, new[] { "Nested.fomod" });
			Assert.That(state.GetModVisualStatus(managed), Is.EqualTo(ModVisualStatus.InstalledActive));
			state.RebuildActivationState(new[] { restored }, new[] { "Nested.fomod" });
			Assert.That(state.IsModInstalled(managed), Is.True);
			Assert.That(state.GetModVisualStatus(managed), Is.EqualTo(ModVisualStatus.InstalledActive));
			state.RebuildActivationState(new[] { restored }, new string[0]);
			Assert.That(state.GetModVisualStatus(managed), Is.EqualTo(ModVisualStatus.InstalledUnlinked));
			state.RebuildActivationState(new[] { new InstallLog.DummyMod("Reloaded again", archive) }, new string[0]);
			Assert.That(state.GetModVisualStatus(managed), Is.EqualTo(ModVisualStatus.InstalledUnlinked));
			state.RebuildActivationState(new IMod[0], new[] { "Nested.fomod" });
			Assert.That(state.GetModVisualStatus(managed), Is.EqualTo(ModVisualStatus.Uninstalled));
		}

		/// <summary>Activator basename aliases do not mark a distinct archive in another directory as installed.</summary>
		[Test]
		public void SameBasenameDoesNotConflateDifferentInstalledArchives()
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-status-fixture");
			IMod installed = Mod(Path.Combine(root, "First", "Mod.zip"), "Mod.zip");
			IMod other = Mod(Path.Combine(root, "Second", "Mod.zip"), "Mod.zip");
			var state = new ModListPresentationState();
			state.RebuildActivationState(new[] { installed }, new[] { "Mod.zip" });
			Assert.That(state.GetModVisualStatus(installed), Is.EqualTo(ModVisualStatus.InstalledActive));
			Assert.That(state.GetModVisualStatus(other), Is.EqualTo(ModVisualStatus.Uninstalled));
		}

		/// <summary>Legacy mods without a separate archive path retain the historical Filename fallback.</summary>
		[Test]
		public void LegacyFilenameFallbackAndNormalizedPathsMatchRestoredRecords()
		{
			string archive = Path.Combine(Path.GetTempPath(), "nmm-status-fixture", "Mod.zip");
			IMod restored = new InstallLog.DummyMod("Restored", archive);
			IMod legacy = Mod(null, archive);
			IMod normalized = Mod(Path.Combine(Path.GetDirectoryName(archive), ".", Path.GetFileName(archive)), "Nested.fomod");
			var state = new ModListPresentationState();
			state.RebuildActivationState(new[] { restored }, new[] { archive });
			Assert.That(state.GetModVisualStatus(legacy), Is.EqualTo(ModVisualStatus.InstalledActive));
			Assert.That(state.GetModVisualStatus(normalized), Is.EqualTo(ModVisualStatus.InstalledActive));
		}

		/// <summary>Supplies distinct physical and installer-facing identities without opening an archive.</summary>
		private static IMod Mod(string archive, string filename) => InterfaceStub<IMod>.Create((method, args) =>
		{
			if (method.Name == "get_ModArchivePath") return archive;
			if (method.Name == "get_Filename") return filename;
			return null;
		});
	}
}
