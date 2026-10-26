using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Exercises fresh and partial read-only-file preferences without opening a game or writing user settings.</summary>
	[TestFixture]
	public class ReadOnlyStartupTests
	{
		private string _root;
		private string _iniPath;
		private PerGameModeSettings<PerGameModeSettings<object>> _custom;
		private Nexus.Client.EnvironmentInfo _environment;
		private int _saves;

		/// <summary>Creates a read-only INI and an isolated in-memory settings store.</summary>
		[SetUp]
		public void SetUp()
		{
			_saves = 0;
			_root = Path.Combine(Path.GetTempPath(), "NmmReadOnlyStartup-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_root);
			_iniPath = Path.Combine(_root, "Fallout.ini");
			File.WriteAllText(_iniPath, "[General]");
			File.SetAttributes(_iniPath, File.GetAttributes(_iniPath) | FileAttributes.ReadOnly);
			_custom = new PerGameModeSettings<PerGameModeSettings<object>>();
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				if (method.Name == "get_CustomGameModeSettings") return _custom;
				if (method.Name == "Save") _saves++;
				return null;
			});
			_environment = (Nexus.Client.EnvironmentInfo)FormatterServices.GetUninitializedObject(typeof(Nexus.Client.EnvironmentInfo));
			typeof(Nexus.Client.EnvironmentInfo).GetField("<Settings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(_environment, settings);
		}

		/// <summary>Removes only this fixture's INI and directory, restoring writable attributes first.</summary>
		[TearDown]
		public void TearDown()
		{
			File.SetAttributes(_iniPath, FileAttributes.Normal);
			Directory.Delete(_root, true);
		}

		/// <summary>Missing or invalid preferences must prompt instead of unboxing null or silently changing a file.</summary>
		[TestCase("missing-container")]
		[TestCase("missing-keys")]
		[TestCase("missing-ask")]
		[TestCase("missing-decision")]
		[TestCase("invalid-ask")]
		[TestCase("invalid-decision")]
		public void ReadOnlyScan_IncompletePreferencesPromptWithoutChangingFile(string state)
		{
			if (state != "missing-container")
			{
				var preferences = new PerGameModeSettings<object>();
				if (state != "missing-keys" && state != "missing-ask")
					preferences["AskAboutReadOnlySettingsFiles"] = state == "invalid-ask" ? (object)"false" : false;
				if (state != "missing-keys" && state != "missing-decision")
					preferences["UnReadOnlySettingsFiles"] = state == "invalid-decision" ? (object)"true" : true;
				_custom["FalloutNV"] = preferences;
			}
			var initializer = new ReadOnlyProbe(_environment);
			int prompts = 0;
			initializer.ConfirmMakeWritable = delegate(IEnvironmentInfo environment, string file, out bool remember)
			{
				prompts++;
				remember = false;
				return false;
			};

			Assert.DoesNotThrow(() => initializer.Scan(Mode()));

			Assert.That(prompts, Is.EqualTo(1));
			Assert.That((File.GetAttributes(_iniPath) & FileAttributes.ReadOnly) != 0, Is.True);
			Assert.That(File.ReadAllText(_iniPath), Is.EqualTo("[General]"));
			Assert.That(_saves, Is.Zero);
		}

		/// <summary>Existing remembered decisions retain their behavior without another prompt or settings write.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void ReadOnlyScan_PreservesRememberedDecision(bool makeWritable)
		{
			_custom["FalloutNV"] = new PerGameModeSettings<object>
			{
				{ "AskAboutReadOnlySettingsFiles", false }, { "UnReadOnlySettingsFiles", makeWritable }
			};
			var initializer = new ReadOnlyProbe(_environment);
			initializer.ConfirmMakeWritable = delegate(IEnvironmentInfo environment, string file, out bool remember)
			{
				remember = false;
				Assert.Fail("Remembered choices must not prompt again.");
				return false;
			};

			initializer.Scan(Mode());

			Assert.That((File.GetAttributes(_iniPath) & FileAttributes.ReadOnly) != 0, Is.EqualTo(!makeWritable));
			Assert.That(_saves, Is.Zero);
		}

		/// <summary>A fresh read-only decision can be remembered even when the per-game settings container is absent.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void ReadOnlyScan_RemembersExplicitDecision(bool makeWritable)
		{
			var initializer = new ReadOnlyProbe(_environment);
			initializer.ConfirmMakeWritable = delegate(IEnvironmentInfo environment, string file, out bool remember)
			{
				remember = true;
				return makeWritable;
			};

			initializer.Scan(Mode());

			Assert.That(_custom["FalloutNV"]["AskAboutReadOnlySettingsFiles"], Is.EqualTo(false));
			Assert.That(_custom["FalloutNV"]["UnReadOnlySettingsFiles"], Is.EqualTo(makeWritable));
			Assert.That(_saves, Is.EqualTo(1));
		}

		/// <summary>Factory defaults are idempotent and preserve an existing decision when the prompt preference is absent.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void FactoryReadOnlyDefaults_PreserveExistingDecision(bool existingDecision)
		{
			if (existingDecision)
				_custom["FalloutNV"] = new PerGameModeSettings<object> { { "UnReadOnlySettingsFiles", false } };
			var definition = new GameModeDefinition { ModeId = "FalloutNV", Name = "Fallout: New Vegas", BehaviorProfile = "gamebryo" };
			var factory = new DataDrivenGameModeFactory(_environment, definition);
			MethodInfo initialize = typeof(DataDrivenGameModeFactory).GetMethod("EnsureLegacyReadOnlySettingsDefaults", BindingFlags.Instance | BindingFlags.NonPublic);

			initialize.Invoke(factory, null);
			initialize.Invoke(factory, null);

			Assert.That(_custom["FalloutNV"]["AskAboutReadOnlySettingsFiles"], Is.EqualTo(true));
			Assert.That(_custom["FalloutNV"]["UnReadOnlySettingsFiles"], Is.EqualTo(!existingDecision));
			Assert.That(_saves, Is.EqualTo(1));
		}

		/// <summary>Exposes only the mode identity and the isolated INI to the startup scanner.</summary>
		private IGameMode Mode()
		{
			return InterfaceStub<IGameMode>.Create((method, args) =>
			{
				if (method.Name == "get_ModeId") return "FalloutNV";
				if (method.Name == "get_WritablePaths") return new[] { _iniPath };
				return null;
			});
		}

		/// <summary>Calls the real read-only-file scanner without starting application initialization.</summary>
		private sealed class ReadOnlyProbe : ApplicationInitializer
		{
			/// <summary>Uses the fixture's in-memory environment and default confirmation delegates.</summary>
			public ReadOnlyProbe(Nexus.Client.EnvironmentInfo environment) : base(environment, null, new List<string>()) { }
			/// <summary>Runs the production scanner synchronously against the supplied mode.</summary>
			public void Scan(IGameMode mode) { ScanForReadonlyFiles(mode); }
		}
	}
}
