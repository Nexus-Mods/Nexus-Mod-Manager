using System;
using System.IO;
using Nexus.Client.Games;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.UI;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Checks plugin exports without initializing a game, native sorter or plugin-state writers.</summary>
	[TestFixture]
	public class PluginExportTests
	{
		/// <summary>The success notification must expose a complete, closed file in the existing import-compatible format.</summary>
		[TestCase("Morrowind")]
		[TestCase("Skyrim")]
		public void FileExport_IsCompleteBeforeSuccessIsReported(string modeId)
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmPluginExport-" + Guid.NewGuid().ToString("N") + ".txt");
			try
			{
				PluginManagerVM viewModel = CreateViewModel(modeId);
				string exported = null;
				int exportedCount = -1;
				Exception failure = null;
				viewModel.ExportFailed += (sender, args) => failure = args.Error;
				viewModel.ExportSucceeded += (sender, args) =>
				{
					exportedCount = args.ExportedPluginCount;
					using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
					using (StreamReader reader = new StreamReader(stream))
						exported = reader.ReadToEnd();
				};

				viewModel.ExportLoadOrderToFileCommand.Execute(path);

				Assert.That(failure, Is.Null);
				Assert.That(exportedCount, Is.EqualTo(2));
				Assert.That(exported, Is.EqualTo("GameMode=" + modeId + Environment.NewLine + Environment.NewLine +
					"Morrowind.esm=1" + Environment.NewLine + "Disabled.esp=0" + Environment.NewLine));
			}
			finally { if (File.Exists(path)) File.Delete(path); }
		}

		/// <summary>An unusable destination must report failure instead of reporting a successful empty export.</summary>
		[Test]
		public void FileExport_UnavailableDirectoryReportsFailure()
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmMissingExport-" + Guid.NewGuid().ToString("N"), "loadorder.txt");
			PluginManagerVM viewModel = CreateViewModel("Morrowind");
			ExportFailedEventArgs failure = null;
			bool succeeded = false;
			viewModel.ExportFailed += (sender, args) => failure = args;
			viewModel.ExportSucceeded += (sender, args) => succeeded = true;

			viewModel.ExportLoadOrderToFileCommand.Execute(path);

			Assert.That(succeeded, Is.False);
			Assert.That(failure, Is.Not.Null);
			Assert.That(failure.Filename, Is.EqualTo(path));
			Assert.That(failure.Error, Is.InstanceOf<DirectoryNotFoundException>());
		}

		/// <summary>Provides an observed order and active set while rejecting any unexpected game-state writes.</summary>
		private static PluginManagerVM CreateViewModel(string modeId)
		{
			Plugin active = new Plugin(@"C:\Game\Data Files\Morrowind.esm", String.Empty, null);
			Plugin disabled = new Plugin(@"C:\Game\Data Files\Disabled.esp", String.Empty, null);
			var managed = new ReadOnlyObservableList<Plugin>(new ThreadSafeObservableList<Plugin>(new[] { active, disabled }));
			var activePlugins = new ReadOnlyObservableList<Plugin>(new ThreadSafeObservableList<Plugin>(new[] { active }));
			IPluginManager manager = InterfaceStub<IPluginManager>.Create((method, args) =>
			{
				if (method.Name == "get_ManagedPlugins") return managed;
				if (method.Name == "get_ActivePlugins") return activePlugins;
				throw new InvalidOperationException("Export must not mutate plugin state: " + method.Name);
			});
			ILoadOrderManager order = InterfaceStub<ILoadOrderManager>.Create((method, args) => null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) =>
			{
				if (method.Name == "get_ModeId") return modeId;
				if (method.Name == "get_UsesPlugins") return true;
				if (method.Name == "get_LoadOrderManager") return order;
				return null;
			});
			return new PluginManagerVM(manager, null, gameMode, null, null);
		}
	}
}
