using System;
using System.IO;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies that descriptors retained across rescans use the newly confirmed game paths.
	/// </summary>
	[TestFixture]
	public class GamebryoDescriptorRescanTests
	{
		private const string ModeId = "SkyrimGOG";
		private string _root;
		private DescriptorEnvironment _environment;

		/// <summary>
		/// Creates isolated settings and temporary game folders without saving user configuration.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-descriptor-rescan-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
			_environment = new DescriptorEnvironment(new Nexus.Client.Properties.Settings
			{
				InstallationPaths = new PerGameModeSettings<string>(),
				ExecutablePaths = new PerGameModeSettings<string>()
			});
		}

		/// <summary>
		/// Removes only the temporary folders created for the current test.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			Directory.Delete(_root, true);
		}

		/// <summary>
		/// Exercises the missing-master startup check after a rescan, with each plugin list read first.
		/// </summary>
		[TestCase(false, "critical")]
		[TestCase(false, "official")]
		[TestCase(false, "unmanaged")]
		[TestCase(true, "critical")]
		[TestCase(true, "official")]
		[TestCase(true, "unmanaged")]
		public void ConfirmedRelocationRefreshesAllPluginPaths(bool legacy, string firstList)
		{
			string oldPath = Path.Combine(_root, "OldGame");
			string newPath = Path.Combine(_root, "NewGame");
			SetGamePath(oldPath);
			IGameModeDescriptor descriptor = CreateDescriptor(legacy);
			Assert.That(descriptor.OrderedCriticalPluginNames, Is.EqualTo(new[] { Path.Combine(oldPath, "Data", "Skyrim.esm") }));
			Assert.That(descriptor.OrderedOfficialPluginNames, Is.EqualTo(new[] { Path.Combine(oldPath, "Data", "Update.esm") }));
			Assert.That(descriptor.OrderedOfficialUnmanagedPluginNames, Is.EqualTo(new[] { Path.Combine(oldPath, "Data", "Creation.esl") }));
			Assert.That(File.Exists(descriptor.OrderedCriticalPluginNames[0]), Is.False);

			Directory.CreateDirectory(Path.Combine(newPath, "Data"));
			File.WriteAllText(Path.Combine(newPath, "Data", "Skyrim.esm"), string.Empty);
			SetGamePath(newPath);
			string[] firstPaths = firstList == "critical" ? descriptor.OrderedCriticalPluginNames :
				firstList == "official" ? descriptor.OrderedOfficialPluginNames : descriptor.OrderedOfficialUnmanagedPluginNames;
			Assert.That(firstPaths[0], Does.StartWith(newPath + Path.DirectorySeparatorChar));
			Assert.That(descriptor.PluginDirectory, Is.EqualTo(Path.Combine(newPath, "Data")));
			Assert.That(descriptor.OrderedCriticalPluginNames, Is.EqualTo(new[] { Path.Combine(newPath, "Data", "Skyrim.esm") }));
			Assert.That(File.Exists(descriptor.OrderedCriticalPluginNames[0]), Is.True);
			Assert.That(descriptor.OrderedOfficialPluginNames, Is.EqualTo(new[] { Path.Combine(newPath, "Data", "Update.esm") }));
			Assert.That(descriptor.OrderedOfficialUnmanagedPluginNames, Is.EqualTo(new[] { Path.Combine(newPath, "Data", "Creation.esl") }));
		}

		/// <summary>
		/// Preserves caching when the current game paths remain unchanged, including changes to another game.
		/// </summary>
		[TestCase(false)]
		[TestCase(true)]
		public void UnchangedGamePathsPreserveCachedLists(bool legacy)
		{
			SetGamePath(Path.Combine(_root, "Game"));
			IGameModeDescriptor descriptor = CreateDescriptor(legacy);
			string[] critical = descriptor.OrderedCriticalPluginNames;
			string[] official = descriptor.OrderedOfficialPluginNames;
			string[] unmanaged = descriptor.OrderedOfficialUnmanagedPluginNames;
			_environment.Settings.InstallationPaths["SkyrimSE"] = Path.Combine(_root, "OtherGame");
			Assert.That(descriptor.OrderedCriticalPluginNames, Is.SameAs(critical));
			Assert.That(descriptor.OrderedOfficialPluginNames, Is.SameAs(official));
			Assert.That(descriptor.OrderedOfficialUnmanagedPluginNames, Is.SameAs(unmanaged));
		}

		/// <summary>
		/// Reloads installation-specific plugin lists even when the managed plugin directory stays fixed.
		/// </summary>
		[TestCase("{GamePath}")]
		[TestCase("{ExecutablePath}")]
		public void RelocationReloadsOfficialUnmanagedPluginList(string listPathToken)
		{
			string oldPath = Path.Combine(_root, "OldGame");
			string newPath = Path.Combine(_root, "NewGame");
			string pluginDirectory = Path.Combine(_root, "ManagedData");
			Directory.CreateDirectory(oldPath);
			Directory.CreateDirectory(newPath);
			File.WriteAllText(Path.Combine(oldPath, "Skyrim.ccc"), "OldCreation.esl");
			File.WriteAllText(Path.Combine(newPath, "Skyrim.ccc"), "NewCreation.esl");
			SetGamePath(oldPath);
			GameModeDefinition definition = CreateDefinition();
			definition.Plugin.PluginDirectorySuffix = pluginDirectory;
			definition.Plugin.OfficialUnmanagedPluginListFiles = new[] { listPathToken + "\\Skyrim.ccc" };
			DataDrivenGameModeFactory factory = new DataDrivenGameModeFactory(_environment, definition);
			Assert.That(factory.GameModeDescriptor.OrderedOfficialUnmanagedPluginNames,
				Is.EquivalentTo(new[] { Path.Combine(pluginDirectory, "Creation.esl"), Path.Combine(pluginDirectory, "OldCreation.esl") }));

			_environment.Settings.ExecutablePaths[ModeId] = newPath;
			if (listPathToken == "{GamePath}")
				_environment.Settings.InstallationPaths[ModeId] = newPath;
			Assert.That(factory.GameModeDescriptor.OrderedOfficialUnmanagedPluginNames,
				Is.EquivalentTo(new[] { Path.Combine(pluginDirectory, "Creation.esl"), Path.Combine(pluginDirectory, "NewCreation.esl") }));
		}

		/// <summary>Starfield uses the first existing CCC file, including an empty override; other modes retain merged lists.</summary>
		[TestCase(true, true, false)]
		[TestCase(true, true, true)]
		[TestCase(true, false, false)]
		[TestCase(false, true, false)]
		public void OfficialLists_PreserveStarfieldCccPrecedence(bool starfield, bool userListExists, bool userListEmpty)
		{
			string gamePath = Path.Combine(_root, "Game");
			Directory.CreateDirectory(gamePath);
			SetGamePath(gamePath);
			string userList = Path.Combine(_root, "User.ccc");
			string gameList = Path.Combine(gamePath, "Starfield.ccc");
			if (userListExists) File.WriteAllText(userList, userListEmpty ? String.Empty : "UserCreation.esm\r\nusercreation.esm\r\nSecondCreation.esm");
			File.WriteAllText(gameList, "GameCreation.esm");
			GameModeDefinition definition = CreateDefinition();
			definition.Plugin.Policy = new GameModePluginPolicyDefinition { SchemaVersion = 1, UseStarfieldActivationRules = starfield };
			definition.Plugin.OfficialUnmanagedPluginListFiles = new[] { userList, gameList };
			var factory = new DataDrivenGameModeFactory(_environment, definition);
			var expected = new System.Collections.Generic.List<string> { Path.Combine(gamePath, "Data", "Creation.esl") };
			if (userListExists && !userListEmpty)
			{
				expected.Add(Path.Combine(gamePath, "Data", "UserCreation.esm"));
				expected.Add(Path.Combine(gamePath, "Data", "SecondCreation.esm"));
			}
			if (!starfield || !userListExists) expected.Add(Path.Combine(gamePath, "Data", "GameCreation.esm"));
			CollectionAssert.AreEqual(expected, factory.GameModeDescriptor.OrderedOfficialUnmanagedPluginNames);
		}

		/// <summary>
		/// Applies the same per-game path settings that a confirmed scan updates.
		/// </summary>
		private void SetGamePath(string path)
		{
			_environment.Settings.InstallationPaths[ModeId] = path;
			_environment.Settings.ExecutablePaths[ModeId] = path;
		}

		/// <summary>
		/// Creates either the current data-driven descriptor or a legacy fallback descriptor.
		/// </summary>
		private IGameModeDescriptor CreateDescriptor(bool legacy)
		{
			return legacy ? (IGameModeDescriptor)new LegacyDescriptor(_environment) :
				new DataDrivenGameModeFactory(_environment, CreateDefinition()).GameModeDescriptor;
		}

		/// <summary>
		/// Defines the Skyrim GOG data layout with all three types of plugin lists.
		/// </summary>
		private static GameModeDefinition CreateDefinition()
		{
			return new GameModeDefinition
			{
				ModeId = ModeId,
				BehaviorProfile = "gamebryo",
				OrderedCriticalPluginNames = new[] { "Skyrim.esm" },
				OrderedOfficialPluginNames = new[] { "Update.esm" },
				OrderedOfficialUnmanagedPluginNames = new[] { "Creation.esl" },
				Plugin = new GameModePluginDefinition { PluginDirectorySuffix = "Data" }
			};
		}

		/// <summary>
		/// Supplies legacy plugin filenames without game-specific initialization or disk state.
		/// </summary>
		private sealed class LegacyDescriptor : GamebryoGameModeDescriptorBase
		{
			/// <summary>
			/// Initializes the descriptor with isolated test settings.
			/// </summary>
			public LegacyDescriptor(IEnvironmentInfo environment) : base(environment) { }
			public override string ModeId => GamebryoDescriptorRescanTests.ModeId;
			public override string Name => "Skyrim GOG";
			public override string[] GameExecutables => new[] { "SkyrimSE.exe" };
			public override Theme ModeTheme => null;
			protected override string[] OrderedCriticalPluginFilenames => new[] { "Skyrim.esm" };
			protected override string[] OrderedOfficialPluginFilenames => new[] { "Update.esm" };
			protected override string[] OrderedOfficialUnmanagedPluginFilenames => new[] { "Creation.esl" };
		}

		/// <summary>
		/// Supplies environment metadata without accessing application configuration or creating user folders.
		/// </summary>
		private sealed class DescriptorEnvironment : IEnvironmentInfo
		{
			/// <summary>
			/// Initializes the environment with unsaved test settings.
			/// </summary>
			public DescriptorEnvironment(ISettings settings) { Settings = settings; }
			public ISettings Settings { get; private set; }
			public string ApplicationPersonalDataFolderPath => null;
			public string PersonalDataFolderPath => null;
			public bool IsMonoMode => false;
			public string TemporaryPath => null;
			public string ProgrammeInfoDirectory => null;
			public bool Is64BitProcess => Environment.Is64BitProcess;
			public Version ApplicationVersion => new Version(1, 0);
		}
	}
}
