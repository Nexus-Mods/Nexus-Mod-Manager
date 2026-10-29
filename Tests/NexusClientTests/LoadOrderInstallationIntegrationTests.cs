using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Regression source for archive-only registration and pre-review legacy curator recipe regeneration.</summary>
	[TestFixture]
	public sealed class LoadOrderInstallationIntegrationTests
	{
		private const string First = "11111111-1111-1111-1111-111111111111";
		private const string Second = "22222222-2222-2222-2222-222222222222";
		private const string Third = "33333333-3333-3333-3333-333333333333";
		private const string Game = "44444444-4444-4444-4444-444444444444";

		/// <summary>The common basic planner inspects only the final selected package, for either native installation method.</summary>
		[TestCase(ModInstallMethod.Direct)]
		[TestCase(ModInstallMethod.Virtual)]
		public void BasicPlanRegistersOnlySelectedPakWithoutSidecar(ModInstallMethod method)
		{
			using (var fixture = new Fixture())
			{
				byte[] before = File.ReadAllBytes(fixture.Manifest);
				BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(fixture.Mod, fixture.Mode, new ModInstallContext(method, ModInstallRoot.Data), false,
					new[] { new KeyValuePair<string, string>("First.pak", "Renamed.pak") });
				Assert.That(result.IsSupported, Is.True, result.Message);
				Assert.That(result.Plan.GameSpecificValues.Select(value => value.Key), Is.EqualTo(new[] { "fixture-modules-v1|Public|" + First }));
				StringAssert.Contains("Packed First", Encoding.UTF8.GetString(result.Plan.GameSpecificValues.Single().Value));
				Assert.That(fixture.Inspected.Select(Path.GetFileName), Is.EqualTo(new[] { "Renamed.pak" }));
				Assert.That(fixture.Inspected.All(path => !File.Exists(path)), Is.True, "Only temporary inspection copies are cleaned up.");
				CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Manifest), "Preparation must not publish or claim ownership.");
			}
		}

		/// <summary>A human display version in info.json cannot replace the packed native version obtained from selected PAK metadata.</summary>
		[Test]
		public void PakRegistrationUsesNativeVersionEvenWithSidecar()
		{
			using (var fixture = new Fixture())
			{
				fixture.ArchiveFiles = new[] { "First.pak", "info.json" };
				fixture.WriteArchive("info.json", "{\"Mods\":[{\"UUID\":\"" + First + "\",\"Version\":\"2.63\"}]}");
				BasicInstallGameSpecificValue value = fixture.Plan("First.pak").Single();
				var xml = new XmlDocument(); xml.LoadXml(Encoding.UTF8.GetString(value.Value));
				Assert.That(xml.SelectSingleNode("node/attribute[@id='Version64']").Attributes["value"].Value, Is.EqualTo(Module(First).Metadata.Version));
				Assert.That(fixture.Inspected.Count, Is.EqualTo(1));
			}
		}

		/// <summary>A descriptively named LSX-only archive produces reversible native order effects and no deployable files.</summary>
		[Test]
		public void LsxOnlyArchivePlansExplicitOrderWithoutDeployingIntoMods()
		{
			using (var fixture = new Fixture())
			{
				const string source = "Curator complete load order.lsx";
				fixture.ArchiveFiles = new[] { source };
				var imported = new XmlDocument(); imported.LoadXml(NativeXml());
				XmlNode modules = imported.SelectSingleNode("//node[@id='Mods']/children");
				modules.AppendChild(NativeLoadOrderDraft.CreateRecord(imported, Module(Third).Metadata));
				modules.AppendChild(NativeLoadOrderDraft.CreateRecord(imported, Module(First).Metadata));
				fixture.WriteArchive(source, imported.OuterXml);
				byte[] before = File.ReadAllBytes(fixture.Manifest);
				BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(fixture.Mod, fixture.Mode, Fixture.Context, false, null);
				Assert.That(result.IsSupported, Is.True, result.Message);
				Assert.That(result.Plan.Files, Is.Empty);
				Assert.That(result.Plan.GameSpecificValues.Count, Is.EqualTo(2));
				LarianOrderPreference preference = LarianOrderPreference.Decode(result.Plan.GameSpecificValues.First().Value);
				CollectionAssert.AreEqual(new[] { Third, First }, preference.Order);
				CollectionAssert.AreEqual(new[] { Third, First }, preference.Managed);
				Assert.That(fixture.Inspected, Is.Empty, "Importing an explicit order must not restart package inspection.");
				CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Manifest));
			}
		}

		/// <summary>Reinstall planning repairs only the selected module's invalid version and retains foreign fields.</summary>
		[Test]
		public void RegistrationCorrectsInvalidNativeVersionInPlace()
		{
			using (var fixture = new Fixture())
			{
				var xml = new XmlDocument(); xml.Load(fixture.Manifest);
				XmlElement record = NativeLoadOrderDraft.CreateRecord(xml, Module(First).Metadata);
				record.SetAttribute("foreign", "retain me"); record.SelectSingleNode("attribute[@id='Version64']").Attributes["value"].Value = "2.63";
				xml.SelectSingleNode("//node[@id='Mods']/children").AppendChild(record); xml.Save(fixture.Manifest);
				byte[] before = File.ReadAllBytes(fixture.Manifest);
				string payload = Encoding.UTF8.GetString(fixture.Plan("First.pak").Single().Value);
				StringAssert.DoesNotContain("2.63", payload); StringAssert.Contains("foreign=\"retain me\"", payload);
				CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Manifest), "The reviewed plan has not been installed yet.");
			}
		}

		/// <summary>Existing foreign fields remain exact; archive metadata cannot silently replace a pre-existing native record.</summary>
		[Test]
		public void RegistrationRetainsExistingRecordAndUnknownFields()
		{
			using (var fixture = new Fixture())
			{
				var document = new XmlDocument(); document.Load(fixture.Manifest);
				XmlElement existing = NativeLoadOrderDraft.CreateRecord(document, Module(First).Metadata);
				existing.SetAttribute("foreign", "retain me"); document.SelectSingleNode("//node[@id='Mods']/children").AppendChild(existing); document.Save(fixture.Manifest);
				byte[] result = fixture.Plan("First.pak").Single().Value;
				StringAssert.Contains("foreign=\"retain me\"", Encoding.UTF8.GetString(result));
			}
		}

		/// <summary>Physical overrides and campaign modules are not independently included as add-ons.</summary>
		[TestCase(true)]
		[TestCase(false)]
		public void NonAddonPackagesDoNotProduceModuleEffects(bool physicalOverride)
		{
			using (var fixture = new Fixture())
			{
				fixture.Inspect = file => physicalOverride
					? new LoadOrderPackageInspection(LoadOrderInspectionStatus.Complete, LoadOrderPackageKind.Override, null, LoadOrderContentFeatures.GameFileOverrides, new[] { file.FileName })
					: Complete(file.FileName, new LoadOrderModuleInfo(Module(First).Metadata, LoadOrderModuleKind.Campaign, LoadOrderContentFeatures.None, null, null, null));
				Assert.That(fixture.Plan("First.pak"), Is.Empty);
			}
		}

		/// <summary>Partial observations cannot create native module ownership or change a game manifest.</summary>
		[Test]
		public void PartialInspectionBlocksRegistrationAndCleansCopies()
		{
			using (var fixture = new Fixture())
			{
				byte[] before = File.ReadAllBytes(fixture.Manifest);
				fixture.Inspect = file => new LoadOrderPackageInspection(LoadOrderInspectionStatus.Partial, LoadOrderPackageKind.Modules, new[] { Module(First) }, LoadOrderContentFeatures.None, new[] { file.FileName });
				Assert.Throws<InvalidDataException>(() => fixture.Plan("First.pak"));
				Assert.That(fixture.Inspected.All(path => !File.Exists(path)), Is.True);
				CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Manifest));
			}
		}

		/// <summary>Two selected packages declaring one UUID cannot acquire an arbitrary registration winner.</summary>
		[Test]
		public void DuplicatePackageModuleIdentityBlocksRegistration()
		{
			using (var fixture = new Fixture()) Assert.Throws<InvalidDataException>(() => fixture.Plan("First.pak", "Second.pak"));
		}

		/// <summary>The helper sees the complete selected split set, while a continuation produces no extra module.</summary>
		[Test]
		public void SplitPackagePartsRegisterOneModule()
		{
			using (var fixture = new Fixture())
			{
				fixture.Inspect = file => file.FileName == "First.pak" ? Complete(file.FileName, Module(First)) :
					new LoadOrderPackageInspection(LoadOrderInspectionStatus.Complete, LoadOrderPackageKind.Continuation, null, LoadOrderContentFeatures.None, new[] { "First.pak", "Second.pak" }, "First.pak");
				Assert.That(fixture.Plan("First.pak", "Second.pak").Count, Is.EqualTo(1));
			}
		}

		/// <summary>No newly inferred modern entry may silently migrate an older game-profile manifest.</summary>
		[Test]
		public void LegacyProfileRejectsNewModernModuleRecords()
		{
			using (var fixture = new Fixture())
			{
				File.WriteAllText(fixture.Manifest, NativeXml(true));
				Assert.Throws<InvalidDataException>(() => fixture.Plan("First.pak"));
			}
		}

		/// <summary>Omitted packages, secondary roots and cancelled plans never request archive payloads.</summary>
		[Test]
		public void EmptySecondaryAndCancelledSelectionsDoNotExtract()
		{
			using (var fixture = new Fixture())
			{
				Assert.That(fixture.Planner.Build(fixture.Mod, Fixture.Context, new BasicInstallPlanFile[0], CancellationToken.None), Is.Empty);
				var secondary = new BasicInstallPlanFile("First.pak", "Data\\First.pak", "Data\\First.pak", null,
					ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Secondary, "First.pak"));
				Assert.That(fixture.Planner.Build(fixture.Mod, Fixture.Context, new[] { secondary }, CancellationToken.None), Is.Empty);
				Assert.Throws<OperationCanceledException>(() => fixture.Planner.Build(fixture.Mod, Fixture.Context, new BasicInstallPlanFile[0], new CancellationToken(true)));
				Assert.That(fixture.Extracted, Is.EqualTo(0));
			}
		}

		/// <summary>Collection deferral avoids inspecting archive variants before its final file overrides have been applied.</summary>
		[Test]
		public void DeferredPackagePlanningDoesNotOpenArchivePayloads()
		{
			using (var fixture = new Fixture())
			{
				BasicInstallPlanResult result = new BasicInstallPlanBuilder().Build(fixture.Mod, fixture.Mode, Fixture.Context, false,
					null, null, true, null, true, false, CancellationToken.None);
				Assert.That(result.IsSupported, Is.True); Assert.That(result.Plan.GameSpecificValues, Is.Empty); Assert.That(fixture.Extracted, Is.EqualTo(0));
			}
		}

		/// <summary>Final Collection operations determine inspection, and explicit installer metadata is preserved.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void CollectionInfersFromFinalSelectedOperations(bool explicitMetadata)
		{
			using (var fixture = new Fixture())
			{
				var recipe = new ModInstallationSimpleFileRecipe(new[] { new ModInstallationSimpleFileMapping("First.pak", "Renamed.pak") });
				var operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, new ModOperationFingerprint("target-sha256:" + new string('a', 64), Fixture.Context, "recipe:package-fixture"));
				var validation = new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId, ModInstallationSimpleFileRecipeAdapter.AdapterVersion,
					Fixture.Context, new ModInstallationRecipeExpectedContent(new string('b', 64), 4096),
					new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) },
					new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, "First.pak"), new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, "Renamed.pak") });
				ModInstallationRecipeInput input = new ModInstallationSimpleFileRecipeAdapter().Translate(new ModInstallationRecipeInput(operation, validation), recipe);
				XmlElement record = NativeLoadOrderDraft.CreateRecord(new XmlDocument(), Module(First).Metadata); record.SetAttribute("installer", "preserve");
				byte[] declared = Encoding.UTF8.GetBytes(record.OuterXml);
				if (explicitMetadata) input = input.WithTransformedNativePlan(input.NativeOperations.Concat(new[] { new EditGameSpecificValueOperation("fixture-modules-v1|Public|" + First, declared, true) }));
				ModInstallationRecipeInput result = CollectionNativeRecipePreparer.ApplySelectedPackageValues(input, fixture.Mode, fixture.Mod, CancellationToken.None);
				EditGameSpecificValueOperation value = result.NativeOperations.OfType<EditGameSpecificValueOperation>().Single();
				Assert.That(value.Key, Is.EqualTo("fixture-modules-v1|Public|" + First));
				Assert.That(fixture.Inspected.Select(Path.GetFileName), Is.EqualTo(new[] { "Renamed.pak" }));
				if (explicitMetadata) CollectionAssert.AreEqual(declared, value.Value);
			}
		}

		/// <summary>Missing release tooling is reported before extraction and cannot masquerade as successful empty module metadata.</summary>
		[Test]
		public void MissingInspectorBlocksBeforeExtraction()
		{
			using (var fixture = new Fixture())
			{
				fixture.Available = false; Assert.Throws<InvalidDataException>(() => fixture.Plan("First.pak")); Assert.That(fixture.Extracted, Is.EqualTo(0));
			}
		}

		/// <summary>Optional unselected packages are outside the curator's inclusion/exclusion scope.</summary>
		[Test]
		public void CuratorPolicyUsesOnlySelectedPackageMembership()
		{
			JObject disabled = Entry(Second, "Second"); disabled["enabled"] = false;
			var order = new JArray(Entry(First, "First"), disabled, Entry(Third, "Optional"));
			LarianOrderPreference policy = BaldursGate3CollectionPreparation.BuildScopedPreference(order, new HashSet<string>(new[] { "First.pak", "Second.pak" }, StringComparer.OrdinalIgnoreCase));
			CollectionAssert.AreEqual(new[] { First }, policy.Order); CollectionAssert.AreEqual(new[] { Second }, policy.Excluded);
			CollectionAssert.AreEqual(new[] { First, Second }, policy.Managed);
		}

		/// <summary>Regeneration replaces retained native aggregate effects exactly once, preserves other operations and is idempotent.</summary>
		[Test]
		public void LegacyRecipesRegenerateBeforeReviewWithoutChangingRetainedInput()
		{
			StructuredXmlRecordRule rule = Rule(); string key = BaldursGate3CollectionRecordStore.Prefix + "Public|" + BaldursGate3CollectionRecordStore.Identity;
			var file = new InstallModFileOperation("First.pak", "First.pak");
			var unrelated = new EditGameSpecificValueOperation("another-provider|value", new byte[] { 1 }, true);
			var retained = new ScriptedInstallOperation[] { file, new EditGameSpecificValueOperation(key, new byte[] { 2 }, true), unrelated };
			var values = new[] { new BasicInstallGameSpecificValue(key, new byte[] { 3 }) }; var order = new JArray(Entry(First, "First"));
			IReadOnlyList<ScriptedInstallOperation> first = BaldursGate3CollectionPreparation.RegenerateOrderValues(retained, rule, true, values, order, null);
			IReadOnlyList<ScriptedInstallOperation> second = BaldursGate3CollectionPreparation.RegenerateOrderValues(first, rule, true, values, order, null);
			Assert.That(first.Count, Is.EqualTo(4)); Assert.That(first[0], Is.SameAs(file)); Assert.That(first[1], Is.SameAs(unrelated));
			EditGameSpecificValueOperation[] edits = first.OfType<EditGameSpecificValueOperation>().ToArray();
			Assert.That(edits[1].Key, Is.EqualTo(LarianOrderPreference.Key("Public"))); Assert.That(edits[2].Key, Is.EqualTo(key));
			CollectionAssert.AreEqual(new byte[] { 2 }, ((EditGameSpecificValueOperation)retained[1]).Value);
			CollectionAssert.AreEqual(first.OfType<EditGameSpecificValueOperation>().Select(edit => edit.Key), second.OfType<EditGameSpecificValueOperation>().Select(edit => edit.Key));
			CollectionAssert.AreEqual(edits[1].Value, second.OfType<EditGameSpecificValueOperation>().Single(edit => edit.Key == LarianOrderPreference.Key("Public")).Value);
		}

		/// <summary>Non-owning members lose stale aggregate effects rather than publishing a second curator policy.</summary>
		[Test]
		public void RegenerationRemovesStalePolicyFromNonOwningMember()
		{
			string aggregate = BaldursGate3CollectionRecordStore.Prefix + "OldProfile|" + BaldursGate3CollectionRecordStore.Identity;
			var retained = new ScriptedInstallOperation[] { new EditGameSpecificValueOperation(aggregate, new byte[] { 1 }, true), new EditGameSpecificValueOperation(LarianOrderPreference.Key("OldProfile"), new byte[] { 2 }, true) };
			Assert.That(BaldursGate3CollectionPreparation.RegenerateOrderValues(retained, Rule(), true, new BasicInstallGameSpecificValue[0], new JArray(Entry(First, "First")), null), Is.Empty);
			Assert.That(BaldursGate3CollectionPreparation.IsRebuiltValue(aggregate, Rule(), false), Is.False, "Capability-disabled legacy replay retains its existing aggregate contract.");
		}

		/// <summary>Scoped regeneration preserves an independently installed optional module and ignores it during scoped verification.</summary>
		[Test]
		public void ScopedAggregatePreservesUnselectedModulesAndStillDetectsCuratorChanges()
		{
			using (var fixture = new Fixture())
			{
				var document = new XmlDocument(); document.Load(fixture.Manifest);
				document.SelectSingleNode("//node[@id='Mods']/children").AppendChild(NativeLoadOrderDraft.CreateRecord(document, Module(Second).Metadata)); document.Save(fixture.Manifest);
				var store = new BaldursGate3CollectionRecordStore(fixture.Capability.Profiles, fixture.Profiles);
				var order = new JArray(Entry(First, "First"), Entry(Second, "Second"));
				BasicInstallGameSpecificValue scoped = store.BuildPlan(order, new HashSet<string>(new[] { "First.pak" }, StringComparer.OrdinalIgnoreCase), true).Single();
				StringAssert.Contains(Second, Encoding.UTF8.GetString(scoped.Value));
				store.TryReadValue(fixture.Manifest, BaldursGate3CollectionRecordStore.Identity, out byte[] original);
				var policy = BaldursGate3CollectionPreparation.BuildScopedPreference(order, new HashSet<string>(new[] { "First.pak" }, StringComparer.OrdinalIgnoreCase));
				var effects = new[] { new CollectionPlannedGameValueEffect(LarianOrderPreference.Key("Public"), policy.Encode()) };
				// The optional package remains unrelated even if its native display metadata changes.
				var changed = new XmlDocument(); changed.LoadXml(Encoding.UTF8.GetString(scoped.Value));
				changed.SelectSingleNode("//node[@id='ModuleShortDesc'][attribute[@id='UUID' and @value='" + Second + "']]/attribute[@id='Name']").Attributes["value"].Value = "Independent change";
				byte[] unrelatedChange = Encoding.UTF8.GetBytes(changed.OuterXml);
				Assert.That(CollectionNativeOrderEffectComparer.Matches(scoped.Key, scoped.Value, unrelatedChange, effects), Is.True);
				Assert.That(CollectionNativeOrderEffectComparer.Matches(scoped.Key, scoped.Value, original, effects), Is.False);
				Assert.That(CollectionNativeOrderEffectComparer.Matches(scoped.Key, scoped.Value, unrelatedChange, new CollectionPlannedGameValueEffect[0]), Is.False);
			}
		}

		/// <summary>Builds valid immutable module metadata for the fake package inspector.</summary>
		private static LoadOrderModuleInfo Module(string id) => new LoadOrderModuleInfo(new LoadOrderModuleMetadata(id, "Packed First", "PackedFirst", "1"), LoadOrderModuleKind.AddOn, LoadOrderContentFeatures.None, null, null, null);
		/// <summary>Produces one complete fake package result without running an executable.</summary>
		private static LoadOrderPackageInspection Complete(string file, LoadOrderModuleInfo module) => new LoadOrderPackageInspection(LoadOrderInspectionStatus.Complete, LoadOrderPackageKind.Modules, new[] { module }, module.Features, new[] { file });
		/// <summary>Provides curator metadata in the already-normalized export shape.</summary>
		private static JObject Entry(string id, string name) => new JObject { ["id"] = name + ".pak", ["enabled"] = true, ["data"] = new JObject { ["uuid"] = id, ["name"] = name, ["folder"] = name, ["version"] = "1", ["isListed"] = false } };
		/// <summary>Uses a synthetic durable namespace to prove the implementation has no BG3 mode-ID dependency.</summary>
		private static StructuredXmlRecordRule Rule() => new StructuredXmlRecordRule(1, StructuredXmlRecordRule.ProviderId, 1, "info.json", "Mods", "Utf8", @"Fixture\Profiles", "modsettings.lsx",
			new[] { "default" }, "fixture-modules-v1|", "Mods", "ModuleShortDesc", "UUID", new[] { new StructuredXmlRecordField("UUID", "guid", new[] { "UUID" }, null, null) });
		/// <summary>Creates an existing native profile with its mandatory game-owned record.</summary>
		private static string NativeXml(bool legacy = false) => "<save><version major='4' minor='" + (legacy ? "6" : "8") + "'/><region id='ModuleSettings'><node id='root'><children><node id='Mods'><children><node id='ModuleShortDesc'><attribute id='UUID' value='" + Game + "'/><attribute id='Name' value='GustavX'/><attribute id='Folder' value='GustavX'/></node></children></node>" + (legacy ? "<node id='ModOrder'><children><node id='Module'><attribute id='UUID' value='" + Game + "'/></node></children></node>" : String.Empty) + "</children></node></region></save>";
		/// <summary>Combines the unchanged native mode interface with the additive selected-package capability.</summary>
		private interface IPackageMode : IGameMode, IDeterministicPackageGameValuePlanProvider { }

		/// <summary>Owns synthetic archive bytes and a temporary profile tree; no real helper or game is used.</summary>
		private sealed class Fixture : IDisposable, ILoadOrderPackageInspector
		{
			internal static readonly ModInstallContext Context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
			private readonly string _root = Path.Combine(Path.GetTempPath(), "nmm-package-plan-fixture-" + Guid.NewGuid().ToString("N"));
			internal string Profiles { get; }
			internal string Manifest { get; }
			internal IMod Mod { get; }
			internal IGameMode Mode { get; }
			internal LoadOrderCapability Capability { get; }
			internal LarianPackageInstallPlanner Planner { get; }
			internal List<string> Inspected { get; } = new List<string>();
			internal int Extracted { get; private set; }
			internal string[] ArchiveFiles { get; set; } = new[] { "First.pak", "Second.pak" };
			/// <summary>Supplies one metadata file in the isolated fake archive.</summary>
			internal void WriteArchive(string name, string text) => File.WriteAllText(Path.Combine(_root, name), text);
			internal Func<LoadOrderPackageFile, LoadOrderPackageInspection> Inspect { get; set; } = file => Complete(file.FileName, Module(First));
			internal bool Available { get; set; } = true;
			public bool IsAvailable => Available;

			/// <summary>Supplies bounded fixture streams through the same IMod method as production archive extraction.</summary>
			internal Fixture()
			{
				Profiles = Path.Combine(_root, "Profiles"); Directory.CreateDirectory(Path.Combine(Profiles, "Public"));
				Manifest = Path.Combine(Profiles, "Public", "modsettings.lsx"); File.WriteAllText(Manifest, NativeXml());
				File.WriteAllText(Path.Combine(_root, "First.pak"), "fake package"); File.WriteAllText(Path.Combine(_root, "Second.pak"), "fake package");
				Capability = new LoadOrderCapability(LoadOrderCapability.LarianProviderId, 1, new LoadOrderDirectoryPolicy(new[] { ".pak" }, false), Rule());
				Planner = new LarianPackageInstallPlanner(Capability, this, new StructuredXmlRecordStore(Capability.Profiles, Profiles));
				Mod = InterfaceStub<IMod>.Create((method, args) =>
				{
					if (method.Name == "GetFileList") return ArchiveFiles.ToList();
					if (method.Name == "GetFileStream") { Extracted++; return File.OpenRead(Path.Combine(_root, (string)args[0])); }
					return null;
				});
				Mode = InterfaceStub<IPackageMode>.Create((method, args) =>
				{
					if (method.Name == "RequiresPackageGameValuePlan") return true;
					if (method.Name == "GetPackageGameValues") return LarianLoadOrderArchivePlanner.Source(Mod) == null ? Planner.Build((IMod)args[0], (ModInstallContext)args[1], (IReadOnlyList<BasicInstallPlanFile>)args[2], (CancellationToken)args[3]) : LarianLoadOrderArchivePlanner.Build(Mod, Capability.Profiles, Profiles);
					if (method.Name == "GetModFormatAdjustedPath") return LarianLoadOrderArchivePlanner.Source(Mod) == null ? args[1] : String.Empty;
					if (method.Name == "get_PluginDirectory") return _root;
					return null;
				});
			}

			/// <summary>Creates the exact selected native destinations without installing files.</summary>
			internal IReadOnlyList<BasicInstallGameSpecificValue> Plan(params string[] names) => Planner.Build(Mod, Context, names.Select(name => new BasicInstallPlanFile(name, name, name, null,
				ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, name))).ToArray(), CancellationToken.None);
			/// <summary>Records the staged input path and supplies configured immutable metadata.</summary>
			LoadOrderPackageInspection ILoadOrderPackageInspector.Inspect(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> siblings, CancellationToken token)
			{ Inspected.Add(file.Path); return Inspect(file); }
			/// <summary>Removes only this fixture's fixed, explicitly created files and empty directories.</summary>
			public void Dispose()
			{
				foreach (string source in ArchiveFiles) File.Delete(Path.Combine(_root, source));
				File.Delete(Manifest); File.Delete(Path.Combine(_root, "First.pak")); File.Delete(Path.Combine(_root, "Second.pak"));
				Directory.Delete(Path.GetDirectoryName(Manifest), false); Directory.Delete(Profiles, false); Directory.Delete(_root, false);
			}
		}
	}
}
