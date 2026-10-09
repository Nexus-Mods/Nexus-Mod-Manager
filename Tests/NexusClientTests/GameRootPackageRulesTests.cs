using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Verifies per-game package recognition without changing installation or deployment state.</summary>
	[TestFixture]
	public class GameRootPackageRulesTests
	{
		/// <summary>Recognizes exact root markers and a single common wrapper with either separator convention.</summary>
		[TestCase("")]
		[TestCase("Package\\")]
		[TestCase("Package/")]
		public void Match_CompletePreloaderSignature_IdentifiesExactBase(string prefix)
		{
			Dictionary<string, byte[]> files = CreateArchive(prefix);
			GameRootPackageMatch match = Match(files);
			Assert.That(match, Is.Not.Null);
			Assert.That(match.Rule.Id, Is.EqualTo("xse-plugin-preloader"));
			Assert.That(match.ArchiveBaseDirectory, Is.EqualTo(prefix.TrimEnd('\\', '/')));
		}

		/// <summary>Does not promote content folders, plugin subfolders or multiple enclosing folders to the game root.</summary>
		[TestCase("Data\\")]
		[TestCase("dAtA/")]
		[TestCase("F4SE\\")]
		[TestCase("Tools\\")]
		[TestCase("Wrapper\\Data\\")]
		[TestCase("Wrapper\\F4SE\\Plugins\\")]
		[TestCase("Wrapper\\Nested\\")]
		public void Match_ContentOrDeepFolders_DoesNotMatch(string prefix)
		{
			Assert.That(Match(CreateArchive(prefix)), Is.Null);
		}

		/// <summary>Requires the structural signature rather than assuming every DLL or XML is a root package.</summary>
		[TestCase("<Other><PluginPreloader/></Other>")]
		[TestCase("<xSE><Other><PluginPreloader/></Other></xSE>")]
		[TestCase("<xse><PluginPreloader/></xse>")]
		[TestCase("<xSE xmlns='urn:other'><PluginPreloader/></xSE>")]
		[TestCase("<xSE><PluginPreloader/><Unclosed>")]
		[TestCase("<!DOCTYPE xSE [<!ENTITY x SYSTEM 'file:///must-not-be-read'>]><xSE><PluginPreloader>&x;</PluginPreloader></xSE>")]
		public void Match_InvalidOrDifferentXml_DoesNotMatch(string xml)
		{
			Assert.That(Match(CreateArchive(xml: xml)), Is.Null);
		}

		/// <summary>Does not read XML unless every required file name is present at the same base.</summary>
		[Test]
		public void Match_MissingMarker_DoesNotReadArchiveContent()
		{
			GameRootPackageMatch match = GameRootPackageMatcher.Match(CreateGameMode(),
				new[] { "WinHTTP.dll", @"Other\xSE PluginPreloader.xml", @"F4SE\Plugins\plugin.dll", @"Tools\tool.exe" },
				path => throw new InvalidOperationException("No content read should be needed."));
			Assert.That(match, Is.Null);
		}

		/// <summary>Does not strip wrappers that contain files outside the common base or are not enabled by the rule.</summary>
		[Test]
		public void Match_SplitOrDisabledWrapper_DoesNotMatch()
		{
			Dictionary<string, byte[]> files = CreateArchive(@"Package\");
			Assert.That(Match(files, CreateGameMode(new[] { CreateRule(allowWrapper: false) })), Is.Null);
			files.Add("outside.txt", new byte[0]);
			Assert.That(Match(files), Is.Null);
		}

		/// <summary>Preserves normal root-plus-Data layouts and uses original archive names for case-insensitive marker reads.</summary>
		[Test]
		public void Match_MixedLayoutAndMarkerCase_PreservesRootBase()
		{
			Dictionary<string, byte[]> files = CreateArchive();
			byte[] xml = files["xSE PluginPreloader.xml"];
			files.Remove("xSE PluginPreloader.xml");
			files.Add("XSE PLUGINPRELOADER.XML", xml);
			files.Add(@"Data\F4SE\Plugins\Other.dll", new byte[0]);
			Assert.That(Match(files).ArchiveBaseDirectory, Is.Empty);
		}

		/// <summary>Rejects unsafe or ambiguous Windows archive paths before any signature is accepted.</summary>
		[TestCase(@"..\outside.dll")]
		[TestCase(@"C:\outside.dll")]
		[TestCase(@"Folder\..\outside.dll")]
		[TestCase(@"Folder\\outside.dll")]
		[TestCase(@"Folder.\outside.dll")]
		[TestCase("winhttp.DLL")]
		public void Match_UnsafeOrDuplicateArchivePath_DoesNotMatch(string extraPath)
		{
			Dictionary<string, byte[]> files = CreateArchive();
			files.Add(extraPath, new byte[0]);
			Assert.That(Match(files), Is.Null);
		}

		/// <summary>Uses rule-id ordering rather than configuration ordering when equivalent rules match.</summary>
		[Test]
		public void Match_EquivalentRules_UsesStableIdOrder()
		{
			IGameMode gameMode = CreateGameMode(new[] { CreateRule("z-rule"), CreateRule("a-rule") });
			Assert.That(Match(CreateArchive(), gameMode).Rule.Id, Is.EqualTo("a-rule"));
		}

		/// <summary>Disposes XML streams and prevents oversized documents from becoming recognition evidence.</summary>
		[Test]
		public void Match_XmlReads_AreDisposedAndBounded()
		{
			Dictionary<string, byte[]> files = CreateArchive();
			var stream = new MemoryStream(files["xSE PluginPreloader.xml"]);
			Assert.That(GameRootPackageMatcher.Match(CreateGameMode(), files.Keys, path => stream), Is.Not.Null);
			Assert.That(stream.CanRead, Is.False);
			Assert.That(Match(CreateArchive(xml: "<xSE><PluginPreloader>" + new string('x', 262144) + "</PluginPreloader></xSE>")), Is.Null);
		}

		/// <summary>Does not downgrade unavailable archive evidence into an ordinary-mod match.</summary>
		[Test]
		public void Match_ArchiveReadFailure_Propagates()
		{
			Assert.Throws<IOException>(() => GameRootPackageMatcher.Match(CreateGameMode(), CreateArchive().Keys,
				path => throw new IOException("Archive unavailable.")));
		}

		/// <summary>Honors cancellation before inspecting archive content.</summary>
		[Test]
		public void Match_Canceled_DoesNotReadArchiveContent()
		{
			Assert.Throws<OperationCanceledException>(() => GameRootPackageMatcher.Match(CreateGameMode(), CreateArchive().Keys,
				path => throw new InvalidOperationException("No content read should be needed."), new CancellationToken(true)));
		}

		/// <summary>Keeps legacy GameModes, disabled root support and empty rule lists outside recognition.</summary>
		[Test]
		public void Match_AbsentCapabilityOrRules_DoesNotInspectMod()
		{
			IMod archive = InterfaceStub<IMod>.Create((method, args) => throw new InvalidOperationException("Archive must not be read."));
			IGameMode legacy = InterfaceStub<IGameMode>.Create((method, args) => method.Name == "get_SupportsGameRootModInstall" ? (object)true : null);
			Assert.That(GameRootPackageMatcher.Match(legacy, archive), Is.Null);
			Assert.That(GameRootPackageMatcher.Match(CreateGameMode(new GameRootPackageRule[0]), archive), Is.Null);
			ITestGameMode disabled = InterfaceStub<ITestGameMode>.Create((method, args) =>
				method.Name == "get_GameRootPackageRules" ? (object)new[] { CreateRule() } : false);
			Assert.That(GameRootPackageMatcher.Match(disabled, archive), Is.Null);
		}

		/// <summary>Copies caller-owned signature collections so later configuration edits cannot change runtime evidence.</summary>
		[Test]
		public void Rule_CallerMutation_DoesNotChangeSignature()
		{
			string[] elements = { "xSE", "PluginPreloader" };
			string[] files = { "WinHTTP.dll", "xSE PluginPreloader.xml" };
			var check = new GameRootPackageXmlCheck(files[1], elements);
			var checks = new List<GameRootPackageXmlCheck> { check };
			var rule = new GameRootPackageRule("preloader", files, checks);
			files[0] = "Changed.dll";
			elements[0] = "Changed";
			checks.Clear();
			Assert.That(rule.RequiredFiles[0], Is.EqualTo("WinHTTP.dll"));
			Assert.That(rule.XmlChecks.Single().ElementPath[0], Is.EqualTo("xSE"));
		}

		/// <summary>Accepts optional rules and preserves existing schema-v2 definitions with no recognition configuration.</summary>
		[Test]
		public void Validator_ValidAndOmittedRules_AreAccepted()
		{
			GameModeDefinition definition = CreateDefinition();
			Assert.That(new GameModeDefinitionValidator().Validate(definition), Is.Empty);
			definition.ModInstall.GameRootPackageRules = null;
			definition.ModInstall.SupportsGameRootInstall = false;
			Assert.That(new GameModeDefinitionValidator().Validate(definition), Is.Empty);
			definition.ModInstall.GameRootPackageRules = new List<GameModeGameRootPackageRuleDefinition>();
			Assert.That(new GameModeDefinitionValidator().Validate(definition), Is.Empty);
		}

		/// <summary>Reports invalid rule configuration at the package-rule property rather than accepting broad patterns.</summary>
		[TestCase("duplicate-id")]
		[TestCase("duplicate-file")]
		[TestCase("duplicate-xml")]
		[TestCase("invalid-id")]
		[TestCase("wildcard")]
		[TestCase("path")]
		[TestCase("empty-files")]
		[TestCase("xml-file")]
		[TestCase("xml-path")]
		[TestCase("null-rule")]
		[TestCase("null-check")]
		[TestCase("unsupported-root")]
		public void Validator_InvalidRule_IsReported(string scenario)
		{
			GameModeDefinition definition = CreateDefinition();
			GameModeGameRootPackageRuleDefinition rule = definition.ModInstall.GameRootPackageRules[0];
			switch (scenario)
			{
				case "duplicate-id":
					definition.ModInstall.GameRootPackageRules.Add(new GameModeGameRootPackageRuleDefinition { Id = rule.Id.ToUpperInvariant(), RequiredFiles = rule.RequiredFiles }); break;
				case "duplicate-file": rule.RequiredFiles = new[] { "WinHTTP.dll", "winhttp.DLL" }; break;
				case "duplicate-xml": rule.XmlChecks.Add(new GameModeGameRootPackageXmlCheckDefinition { File = "XSE PLUGINPRELOADER.XML", ElementPath = rule.XmlChecks[0].ElementPath }); break;
				case "invalid-id": rule.Id = "preloader\n"; break;
				case "wildcard": rule.RequiredFiles = new[] { "*.dll" }; break;
				case "path": rule.RequiredFiles = new[] { @"Data\WinHTTP.dll" }; break;
				case "empty-files": rule.RequiredFiles = new string[0]; break;
				case "xml-file": rule.XmlChecks[0].File = "Other.xml"; break;
				case "xml-path": rule.XmlChecks[0].ElementPath = new[] { "xSE", "//PluginPreloader" }; break;
				case "null-rule": definition.ModInstall.GameRootPackageRules.Add(null); break;
				case "null-check": rule.XmlChecks.Add(null); break;
				case "unsupported-root": definition.ModInstall.SupportsGameRootInstall = false; break;
			}
			Assert.That(new GameModeDefinitionValidator().Validate(definition).Any(issue =>
				issue.Severity == GameModeDefinitionIssueSeverity.Error && issue.PropertyPath.StartsWith("modInstall.gameRootPackageRules", StringComparison.Ordinal)), Is.True);
		}

		/// <summary>Loads valid optional rules while isolating another game's invalid configuration.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void Loader_InvalidRuleOrUnknownField_DoesNotPreventOtherGameLoading(bool unknownField)
		{
			string directory = Path.Combine(Path.GetTempPath(), "NmmRootRules_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				GameModeDefinition valid = CreateDefinition();
				JObject invalid = JObject.Parse(SerializeDefinition(valid));
				invalid["modeId"] = "InvalidGame";
				JObject rule = (JObject)invalid["modInstall"]["gameRootPackageRules"][0];
				if (unknownField) rule["unexpected"] = true;
				else rule["requiredFiles"] = new JArray("*.dll");
				Directory.CreateDirectory(Path.Combine(directory, "Valid"));
				Directory.CreateDirectory(Path.Combine(directory, "Invalid"));
				File.WriteAllText(Path.Combine(directory, "Valid", "game.json"), SerializeDefinition(valid));
				File.WriteAllText(Path.Combine(directory, "Invalid", "game.json"), invalid.ToString());
				GameModeDefinitionLoadResult loaded = new GameModeDefinitionLoader().LoadFromDirectory(directory);
				Assert.That(loaded.HasErrors, Is.True);
				Assert.That(loaded.Definitions.Single().ModeId, Is.EqualTo(valid.ModeId));
				Assert.That(loaded.Definitions.Single().ModInstall.GameRootPackageRules.Single().RequiredFiles,
					Is.EquivalentTo(new[] { "WinHTTP.dll", "xSE PluginPreloader.xml" }));
			}
			finally
			{
				Directory.Delete(directory, true);
			}
		}

		/// <summary>Loads a definition without the optional section through the existing schema-v2 loader.</summary>
		[Test]
		public void Loader_OmittedRules_PreservesExistingDefinition()
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmLegacyRootRules_" + Guid.NewGuid().ToString("N") + ".json");
			try
			{
				GameModeDefinition definition = CreateDefinition();
				definition.ModInstall.GameRootPackageRules = null;
				File.WriteAllText(path, SerializeDefinition(definition));
				GameModeDefinitionLoadResult loaded = new GameModeDefinitionLoader().LoadFromPath(path);
				Assert.That(loaded.HasErrors, Is.False);
				Assert.That(loaded.Definitions.Single().ModInstall.GameRootPackageRules, Is.Null);
			}
			finally
			{
				if (File.Exists(path)) File.Delete(path);
			}
		}

		/// <summary>Creates an optional-capability GameMode without an installed game or persisted configuration.</summary>
		private static IGameMode CreateGameMode(GameRootPackageRule[] rules = null)
		{
			GameRootPackageRule[] configured = rules ?? new[] { CreateRule() };
			return InterfaceStub<ITestGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_SupportsGameRootModInstall": return true;
					case "get_GameRootPackageRules": return configured;
					case "get_StopFolders": return new[] { "F4SE", "Tools", "textures" };
					default: return null;
				}
			});
		}

		/// <summary>Provides the optional rule capability for the read-only GameMode stub.</summary>
		private interface ITestGameMode : IGameMode, IGameRootPackageRuleProvider { }

		/// <summary>Creates the characterized preloader rule independently of archive display names or Nexus ids.</summary>
		private static GameRootPackageRule CreateRule(string id = "xse-plugin-preloader", bool allowWrapper = true)
		{
			return new GameRootPackageRule(id, new[] { "WinHTTP.dll", "xSE PluginPreloader.xml" },
				new[] { new GameRootPackageXmlCheck("xSE PluginPreloader.xml", new[] { "xSE", "PluginPreloader" }) }, allowWrapper);
		}

		/// <summary>Creates in-memory archive evidence without touching managed folders.</summary>
		private static Dictionary<string, byte[]> CreateArchive(string prefix = "", string xml = "<xSE><PluginPreloader/></xSE>")
		{
			return new Dictionary<string, byte[]>(StringComparer.Ordinal)
			{
				{ prefix + "WinHTTP.dll", new byte[0] },
				{ prefix + "xSE PluginPreloader.xml", Encoding.UTF8.GetBytes(xml) }
			};
		}

		/// <summary>Matches immutable test evidence through the production shared matcher.</summary>
		private static GameRootPackageMatch Match(Dictionary<string, byte[]> files, IGameMode gameMode = null)
		{
			return GameRootPackageMatcher.Match(gameMode ?? CreateGameMode(), files.Keys, path => new MemoryStream(files[path], false));
		}

		/// <summary>Creates a minimal valid schema-v2 definition for loader and validator coverage.</summary>
		private static GameModeDefinition CreateDefinition()
		{
			return new GameModeDefinition
			{
				SchemaVersion = 2, ModeId = "RootRulesTest", Name = "Package rules test", BehaviorProfile = "generic",
				GameExecutables = new[] { "Game.exe" },
				ModInstall = new GameModeModInstallDefinition
				{
					PathAdjustmentProfile = "none", SupportsGameRootInstall = true,
					GameRootPackageRules = new List<GameModeGameRootPackageRuleDefinition>
					{
						new GameModeGameRootPackageRuleDefinition
						{
							Id = "xse-plugin-preloader", RequiredFiles = new[] { "WinHTTP.dll", "xSE PluginPreloader.xml" },
							AllowSingleWrapperFolder = true,
							XmlChecks = new List<GameModeGameRootPackageXmlCheckDefinition>
							{
								new GameModeGameRootPackageXmlCheckDefinition { File = "xSE PluginPreloader.xml", ElementPath = new[] { "xSE", "PluginPreloader" } }
							}
						}
					}
				}
			};
		}

		/// <summary>Serializes only persisted definition properties using their documented JSON casing.</summary>
		private static string SerializeDefinition(GameModeDefinition definition)
		{
			return JsonConvert.SerializeObject(definition, new JsonSerializerSettings
			{
				ContractResolver = new CamelCasePropertyNamesContractResolver(), NullValueHandling = NullValueHandling.Ignore
			});
		}
	}
}
