using System;
using System.IO;
using System.Linq;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies launch-only prelauncher selection without starting a process or changing game discovery.</summary>
	[TestFixture]
	public class DataDrivenPrelauncherTests
	{
		private string _root;
		private GameModeDefinition _definition;

		/// <summary>Creates an isolated older/newer Cyberpunk installation layout.</summary>
		[SetUp]
		public void SetUp()
		{
			_root = Path.Combine(Path.GetTempPath(), "nmm-prelauncher-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(_root, "bin", "x64"));
			_definition = new GameModeDefinition
			{
				SchemaVersion = 2,
				ModeId = "Cyberpunk2077",
				Name = "Cyberpunk 2077",
				BehaviorProfile = "generic",
				GameExecutables = new[] { @"bin\x64\Cyberpunk2077.exe" },
				Launcher = new GameModeLauncherDefinition
				{
					DefaultExecutable = @"bin\x64\Cyberpunk2077.exe",
					PrelauncherExecutable = "REDprelauncher.exe",
					AllowCustomCommand = true
				}
			};
			File.WriteAllText(Path.Combine(_root, _definition.Launcher.DefaultExecutable), "game fixture");
		}

		/// <summary>Removes only the temporary installation layout.</summary>
		[TearDown]
		public void TearDown() { Directory.Delete(_root, true); }

		/// <summary>Default launch uses the prelauncher while explicit direct launch retains the real executable.</summary>
		[Test]
		public void InstalledPrelauncherIsPreferredOnlyForDefaultLaunch()
		{
			string prelauncher = Path.Combine(_root, "REDprelauncher.exe");
			File.WriteAllText(prelauncher, "prelauncher fixture");
			Assert.That(DataDrivenGameLauncher.ResolveLaunchExecutable(_definition, _root, true), Is.EqualTo(prelauncher));
			Assert.That(DataDrivenGameLauncher.ResolveLaunchExecutable(_definition, _root, false),
				Is.EqualTo(Path.Combine(_root, _definition.Launcher.DefaultExecutable)));
			CollectionAssert.AreEqual(new[] { @"bin\x64\Cyberpunk2077.exe" }, _definition.GameExecutables);
		}

		/// <summary>Older installations and prelaunchers removed after command creation retain direct launching.</summary>
		[Test]
		public void MissingPrelauncherFallsBackAndIsRecheckedOnEverySelection()
		{
			string game = Path.Combine(_root, _definition.Launcher.DefaultExecutable);
			string prelauncher = Path.Combine(_root, "REDprelauncher.exe");
			Assert.That(DataDrivenGameLauncher.ResolveLaunchExecutable(_definition, _root, true), Is.EqualTo(game));
			File.WriteAllText(prelauncher, "prelauncher fixture");
			Assert.That(DataDrivenGameLauncher.ResolveLaunchExecutable(_definition, _root, true), Is.EqualTo(prelauncher));
			File.Delete(prelauncher);
			Assert.That(DataDrivenGameLauncher.ResolveLaunchExecutable(_definition, _root, true), Is.EqualTo(game));
		}

		/// <summary>Other definitions must opt in even if a similarly named executable exists.</summary>
		[Test]
		public void UnconfiguredPrelauncherDoesNotChangeExistingLaunchBehavior()
		{
			File.WriteAllText(Path.Combine(_root, "REDprelauncher.exe"), "prelauncher fixture");
			_definition.Launcher.PrelauncherExecutable = null;
			Assert.That(DataDrivenGameLauncher.ResolveLaunchExecutable(_definition, _root, true),
				Is.EqualTo(Path.Combine(_root, _definition.Launcher.DefaultExecutable)));
		}

		/// <summary>A missing preferred direct executable still uses the existing game-executable fallback order.</summary>
		[Test]
		public void MissingPrelauncherPreservesOtherDirectExecutableFallbacks()
		{
			string fallback = Path.Combine(_root, "fallback.exe");
			File.WriteAllText(fallback, "fallback fixture");
			File.Delete(Path.Combine(_root, _definition.Launcher.DefaultExecutable));
			_definition.GameExecutables = new[] { _definition.Launcher.DefaultExecutable, "fallback.exe" };
			Assert.That(DataDrivenGameLauncher.ResolveLaunchExecutable(_definition, _root, true), Is.EqualTo(fallback));
		}

		/// <summary>Validation permits a launch-only executable without adding it to discovery/version candidates.</summary>
		[Test]
		public void PrelauncherDoesNotNeedToBeListedAsAGameExecutable()
		{
			Assert.That(new GameModeDefinitionValidator().Validate(_definition).Any(issue =>
				issue.Severity == GameModeDefinitionIssueSeverity.Error && issue.PropertyPath.StartsWith("launcher", StringComparison.Ordinal)), Is.False);
		}

		/// <summary>Custom commands still win, and the legacy menu option appears only for definitions which opt in.</summary>
		[TestCase(true)]
		[TestCase(false)]
		public void DefaultLaunchPreservesCustomCommandPrecedence(bool usePrelauncher)
		{
			if (!usePrelauncher) _definition.Launcher.PrelauncherExecutable = null;
			File.WriteAllText(Path.Combine(_root, "REDprelauncher.exe"), "prelauncher fixture");
			var commands = new PerGameModeSettings<string> { ["Cyberpunk2077"] = "custom.exe" };
			var arguments = new PerGameModeSettings<string> { ["Cyberpunk2077"] = "--custom-argument" };
			bool customArgumentsRead = false;
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				if (method.Name == "get_CustomLaunchCommands") return commands;
				if (method.Name == "get_CustomLaunchCommandArguments") { customArgumentsRead = true; return arguments; }
				return null;
			});
			IEnvironmentInfo environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
				method.Name == "get_Settings" ? settings : null);
			IGameModeEnvironmentInfo gameEnvironment = InterfaceStub<IGameModeEnvironmentInfo>.Create((method, args) =>
				method.Name == "get_ExecutablePath" || method.Name == "get_InstallationPath" ? _root : null);
			IGameMode mode = InterfaceStub<IGameMode>.Create((method, args) =>
			{
				if (method.Name == "get_GameModeEnvironmentInfo") return gameEnvironment;
				if (method.Name == "get_ModeId") return "Cyberpunk2077";
				if (method.Name == "get_Name") return "Cyberpunk 2077";
				return null;
			});
			var launcher = new DataDrivenGameLauncher(mode, environment, _definition);
			int launchAttempts = 0;
			launcher.GameLaunching += (sender, e) => { launchAttempts++; e.Cancel = true; };
			Assert.That(launcher.LaunchCommands.Any(command => command.Id == "DirectLaunch"), Is.EqualTo(usePrelauncher));
			launcher.DefaultLaunchCommand.Execute();
			Assert.That(customArgumentsRead, Is.True);
			Assert.That(launchAttempts, Is.EqualTo(1), "Cancellation prevents every process launch.");
			Assert.That(commands["Cyberpunk2077"], Is.EqualTo("custom.exe"));
			Assert.That(arguments["Cyberpunk2077"], Is.EqualTo("--custom-argument"));
		}

		/// <summary>Prelauncher configuration uses the same safe game-relative executable rules as other launch targets.</summary>
		[TestCase(@"..\REDprelauncher.exe")]
		[TestCase(@"C:\Games\REDprelauncher.exe")]
		[TestCase("REDprelauncher.lnk")]
		[TestCase("REDprelauncher.exe --launcher-skip")]
		[TestCase("")]
		public void InvalidPrelauncherPathIsRejected(string path)
		{
			_definition.Launcher.PrelauncherExecutable = path;
			Assert.That(new GameModeDefinitionValidator().Validate(_definition).Any(issue =>
				issue.Severity == GameModeDefinitionIssueSeverity.Error && issue.PropertyPath == "launcher.prelauncherExecutable"), Is.True);
		}
	}
}
