using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.ModManagement.LoadOrder;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	/// <summary>Isolated capability, folder and protocol regression cases; never touches installed game data.</summary>
	[TestFixture]
	public sealed class LoadOrderInfrastructureTests
	{
		/// <summary>Legacy definitions and explicitly disabled declarations do not acquire an inventory capability.</summary>
		[Test]
		public void AbsentAndDisabledCapabilitiesKeepLegacyBehavior()
		{
			GameModeDefinition definition = Definition();
			GameModeLoadOrderDefinition order = definition.LoadOrder;
			definition.LoadOrder = null;
			Assert.That(DataDrivenLoadOrder.Create(definition), Is.Null);
			definition.LoadOrder = order;
			order.Enabled = false;
			definition.StructuredEffects = null;
			Assert.That(DataDrivenLoadOrder.Create(definition), Is.Null);
		}

		/// <summary>A non-BG3 mode can declare the provider, and later JSON mutation cannot change captured rules.</summary>
		[Test]
		public void GenericDefinitionFreezesCapabilityWithoutGameIdentityChecks()
		{
			GameModeDefinition definition = Definition();
			LoadOrderCapability capability = DataDrivenLoadOrder.Create(definition);
			definition.LoadOrder.DirectoryPolicy.AllowedExtensions[0] = ".txt";
			definition.StructuredEffects.ProfileRoot = @"Different\Profiles";
			Assert.That(capability.Provider, Is.EqualTo(LoadOrderCapability.LarianProviderId));
			Assert.That(capability.DirectoryPolicy.AllowedExtensions, Is.EqualTo(new[] { ".pak" }));
			Assert.That(capability.Profiles.ProfileRoot, Is.EqualTo(@"Fixture\Profiles"));
		}

		/// <summary>Built-in identities are optional, canonicalized and copied; duplicate or invalid values stay isolated as definition errors.</summary>
		[Test]
		public void BuiltInModuleIdentitiesAreValidatedAndDetached()
		{
			GameModeDefinition definition = Definition();
			definition.LoadOrder.BuiltInModuleIdentities = new[] { "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA" };
			LoadOrderCapability capability = DataDrivenLoadOrder.Create(definition);
			definition.LoadOrder.BuiltInModuleIdentities[0] = "invalid";
			Assert.That(capability.BuiltInModuleIdentities, Is.EqualTo(new[] { "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }));
			Assert.That(new GameModeDefinitionValidator().Validate(definition).Any(issue => issue.PropertyPath == "loadOrder"), Is.True);
			definition.LoadOrder.BuiltInModuleIdentities = new[] { "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA" };
			Assert.Throws<ArgumentException>(() => DataDrivenLoadOrder.Create(definition));
		}

		/// <summary>Unsupported revisions cannot accidentally enable a partly understood reader.</summary>
		[TestCase(0)]
		[TestCase(2)]
		public void UnsupportedRevisionIsReportedAtLoadOrder(int revision)
		{
			GameModeDefinition definition = Definition();
			definition.LoadOrder.Revision = revision;
			Assert.That(new GameModeDefinitionValidator().Validate(definition).Any(issue => issue.PropertyPath == "loadOrder"), Is.True);
		}

		/// <summary>Configuration cannot reinterpret module records or allow a folder layout unsupported by this provider.</summary>
		[Test]
		public void InvalidProfileShapeAndDuplicateExtensionsAreRejected()
		{
			GameModeDefinition definition = Definition();
			definition.StructuredEffects.ContainerId = "OtherRecords";
			Assert.Throws<ArgumentException>(() => DataDrivenLoadOrder.Create(definition));
			definition = Definition();
			definition.LoadOrder.DirectoryPolicy.AllowedExtensions = new[] { ".pak", ".PAK" };
			Assert.Throws<ArgumentException>(() => DataDrivenLoadOrder.Create(definition));
			definition = Definition();
			definition.LoadOrder.DirectoryPolicy.AllowSubdirectories = true;
			Assert.Throws<ArgumentException>(() => DataDrivenLoadOrder.Create(definition));
		}

		/// <summary>The scanner includes hidden/non-package contents and never treats nested packages as deployed top-level entries.</summary>
		[Test]
		public void FolderScanReportsUnexpectedContentsWithoutChangingThem()
		{
			using (var directory = new FixtureDirectory())
			{
				string package = directory.File("Valid.PAK");
				string readme = directory.File("readme.txt");
				File.SetAttributes(readme, FileAttributes.Hidden);
				string nested = Path.Combine(directory.Root, "Nested");
				Directory.CreateDirectory(nested);
				File.WriteAllText(Path.Combine(nested, "Nested.pak"), "nested");
				LoadOrderPackageDirectoryObservation result = LoadOrderPackageDirectoryScanner.Scan(directory.Root, Policy(), CancellationToken.None);
				Assert.That(result.IsComplete, Is.True);
				Assert.That(result.Files.Select(file => file.Path), Is.EqualTo(new[] { package }));
				Assert.That(result.Diagnostics.Any(issue => issue.Kind == LoadOrderDiagnosticKind.UnexpectedFile && issue.Path == readme), Is.True);
				Assert.That(result.Diagnostics.Any(issue => issue.Kind == LoadOrderDiagnosticKind.UnexpectedDirectory && issue.Path == nested), Is.True);
				Assert.That(File.ReadAllText(readme), Is.EqualTo("fixture"));
				Assert.That(File.Exists(Path.Combine(nested, "Nested.pak")), Is.True);
			}
		}

		/// <summary>An absent root cannot be confused with an empty successful scan or be created by inspection.</summary>
		[Test]
		public void MissingRootAndBoundedScanRemainIncomplete()
		{
			using (var directory = new FixtureDirectory())
			{
				string missing = Path.Combine(directory.Root, "Missing");
				LoadOrderPackageDirectoryObservation absent = LoadOrderPackageDirectoryScanner.Scan(missing, Policy(), CancellationToken.None);
				Assert.That(absent.IsComplete, Is.False);
				Assert.That(absent.Diagnostics.Single().Kind, Is.EqualTo(LoadOrderDiagnosticKind.RootUnavailable));
				Assert.That(Directory.Exists(missing), Is.False);
				directory.File("One.pak"); directory.File("Two.pak");
				LoadOrderPackageDirectoryObservation limited = LoadOrderPackageDirectoryScanner.Scan(directory.Root, Policy(), CancellationToken.None, 1);
				Assert.That(limited.IsComplete, Is.False);
				Assert.That(limited.Diagnostics.Any(issue => issue.Kind == LoadOrderDiagnosticKind.ScanLimitReached), Is.True);
			}
		}

		/// <summary>Cancellation is propagated instead of being recorded as a corrupt package.</summary>
		[Test]
		public void CancelledScanPropagatesCancellation()
		{
			using (var directory = new FixtureDirectory())
			using (var cancellation = new CancellationTokenSource())
			{
				cancellation.Cancel();
				Assert.Throws<OperationCanceledException>(() => LoadOrderPackageDirectoryScanner.Scan(directory.Root, Policy(), cancellation.Token));
			}
		}

		/// <summary>A broken package does not suppress good module evidence from the same directory.</summary>
		[Test]
		public void InventoryRetainsSuccessfulPackagesWhenAnotherReaderFails()
		{
			using (var directory = new FixtureDirectory())
			{
				directory.File("Good.pak"); directory.File("Bad.pak");
				var inspector = new FixtureInspector(file =>
				{
					if (file.FileName == "Bad.pak") throw new InvalidDataException("broken package");
					return Inspection(file.FileName);
				});
				LoadOrderInventory inventory = Provider(directory, inspector).ReadInventory(CancellationToken.None);
				Assert.That(inventory.IsComplete, Is.False);
				Assert.That(inventory.Packages.Count, Is.EqualTo(2));
				Assert.That(inventory.Packages.Single(package => package.File.FileName == "Good.pak").Inspection.Modules.Count, Is.EqualTo(1));
				Assert.That(inventory.Diagnostics.Any(issue => issue.Kind == LoadOrderDiagnosticKind.PackageInspectionFailed), Is.True);
			}
		}

		/// <summary>Progress precedes each reader call and retains completed failures as observations rather than silently skipping them.</summary>
		[Test]
		public void PackageProgressReportsCurrentFileAndEveryCompletedObservation()
		{
			using (var directory = new FixtureDirectory())
			{
				directory.File("Good.pak"); directory.File("Bad.pak");
				var progress = new FixtureProgress();
				var inspector = new FixtureInspector(file =>
				{
					Assert.That(progress.Values.Last().FileName, Is.EqualTo(file.FileName));
					Assert.That(progress.Values.Last().Package, Is.Null);
					if (file.FileName == "Bad.pak") throw new InvalidDataException("broken package");
					return Inspection(file.FileName);
				});
				var provider = new PackageLoadOrderInventoryProvider(DataDrivenLoadOrder.Create(Definition()), () => directory.Root, inspector, progress);
				LoadOrderInventory inventory = provider.ReadInventory(CancellationToken.None);
				Assert.That(progress.Values.Select(value => value.Completed), Is.EqualTo(new[] { 0, 1, 1, 2 }));
				Assert.That(progress.Values.All(value => value.Total == 2), Is.True);
				Assert.That(progress.Values.Where(value => value.Package != null).Select(value => value.Package), Is.EqualTo(inventory.Packages));
				Assert.That(progress.Values.Single(value => value.Package?.File.FileName == "Bad.pak").Package.Inspection.Status, Is.EqualTo(LoadOrderInspectionStatus.Failed));
			}
		}

		/// <summary>Duplicate UUIDs remain ambiguous; the infrastructure does not choose the newest package.</summary>
		[Test]
		public void DuplicateModulesAreReportedForBothPackages()
		{
			using (var directory = new FixtureDirectory())
			{
				directory.File("One.pak"); directory.File("Two.pak");
				LoadOrderInventory inventory = Provider(directory, new FixtureInspector(file => Inspection(file.FileName))).ReadInventory(CancellationToken.None);
				Assert.That(inventory.IsComplete, Is.True);
				Assert.That(inventory.Diagnostics.Count(issue => issue.Kind == LoadOrderDiagnosticKind.DuplicateModuleIdentity), Is.EqualTo(2));
			}
		}

		/// <summary>A file changed by another actor cannot retain a successful metadata result from the old inventory.</summary>
		[Test]
		public void ChangedPackageIsReportedSeparatelyFromCorruption()
		{
			using (var directory = new FixtureDirectory())
			{
				directory.File("One.pak");
				var inspector = new FixtureInspector(file =>
				{
					File.AppendAllText(file.Path, "changed");
					return Inspection(file.FileName);
				});
				LoadOrderInventory inventory = Provider(directory, inspector).ReadInventory(CancellationToken.None);
				Assert.That(inventory.IsComplete, Is.False);
				Assert.That(inventory.Diagnostics.Single().Kind, Is.EqualTo(LoadOrderDiagnosticKind.PackageChanged));
			}
		}

		/// <summary>Large helper error output remains a per-package problem instead of escaping the inventory boundary.</summary>
		[Test]
		public void OversizedReaderErrorDoesNotAbortInventory()
		{
			using (var directory = new FixtureDirectory())
			{
				directory.File("One.pak");
				var inspector = new FixtureInspector(file => { throw new IOException(new string('x', 10000)); });
				LoadOrderInventory inventory = Provider(directory, inspector).ReadInventory(CancellationToken.None);
				Assert.That(inventory.IsComplete, Is.False);
				Assert.That(inventory.Packages.Single().Inspection.TechnicalDetails.Length, Is.EqualTo(8192));
			}
		}

		/// <summary>A missing helper yields inventory evidence and one availability problem, without fake module details.</summary>
		[Test]
		public void MissingHelperDoesNotInventSuccessfulInspections()
		{
			using (var directory = new FixtureDirectory())
			{
				directory.File("One.pak"); directory.File("Two.pak");
				var reader = new LarianPackageInspectorClient(Path.Combine(directory.Root, "AbsentReader.exe"));
				LoadOrderInventory inventory = Provider(directory, reader).ReadInventory(CancellationToken.None);
				Assert.That(inventory.IsComplete, Is.False);
				Assert.That(inventory.Packages.All(package => package.Inspection.Status == LoadOrderInspectionStatus.Unavailable && package.Inspection.Modules.Count == 0), Is.True);
				Assert.That(inventory.Diagnostics.Count(issue => issue.Kind == LoadOrderDiagnosticKind.InspectorUnavailable), Is.EqualTo(1));
			}
		}

		/// <summary>Large publishing integers and versions survive the boundary without floating-point conversion.</summary>
		[Test]
		public void ProtocolPreservesMetadataAndScriptIndicators()
		{
			LoadOrderPackageInspection inspection = LarianPackageInspectionProtocol.ReadResponse(Response("One.pak").ToString(), "One.pak");
			LoadOrderModuleInfo module = inspection.Modules.Single();
			Assert.That(module.Metadata.Version, Is.EqualTo("18446744073709551615"));
			Assert.That(module.Metadata.PublishHandle, Is.EqualTo("18446744073709551615"));
			Assert.That(module.Features.HasFlag(LoadOrderContentFeatures.LuaScripts), Is.True);
			Assert.That(module.RequiredScriptExtenderVersion, Is.EqualTo(29));
		}

		/// <summary>Traversal and duplicate JSON properties cannot reach downstream package or publication code.</summary>
		[Test]
		public void ProtocolRejectsUntrustedResponseShapes()
		{
			JObject response = Response("One.pak");
			response["readFiles"] = new JArray(@"..\Other.pak");
			Assert.Throws<InvalidDataException>(() => LarianPackageInspectionProtocol.ReadResponse(response.ToString(), "One.pak"));
			response = Response("One.pak");
			Assert.Throws<InvalidDataException>(() => LarianPackageInspectionProtocol.ReadResponse(response.ToString(), "Other.pak"));
			response.Remove("modules");
			Assert.Throws<InvalidDataException>(() => LarianPackageInspectionProtocol.ReadResponse(response.ToString(), "One.pak"));
			Assert.Throws<InvalidDataException>(() => LarianPackageInspectionProtocol.ReadResponse("{\"schemaVersion\":1,\"schemaVersion\":2}", "One.pak"));
		}

		/// <summary>Unchanged observations are reused, while new packages, changed inputs and changed helper binaries are inspected.</summary>
		[Test]
		public void DisplayCacheInspectsOnlyNewOrChangedPackagesAndInvalidatesChangedHelper()
		{
			using (var directory = new FixtureDirectory())
			using (var helper = new FixtureDirectory())
			{
				string executable = helper.File("Inspector.exe");
				string first = directory.File("First.pak"); directory.File("Second.pak");
				int calls = 0;
				var reader = new FixtureInspector(file => { calls++; return Inspection(file.FileName); });
				var cache = new LoadOrderPackageInspectionCache();
				Func<LoadOrderInventory> read = () => new PackageLoadOrderInventoryProvider(DataDrivenLoadOrder.Create(Definition()), () => directory.Root,
					cache.Wrap(reader, executable)).ReadInventory(CancellationToken.None);
				read(); Assert.That(calls, Is.EqualTo(2));
				directory.File("Third.pak"); read(); Assert.That(calls, Is.EqualTo(3), "Adding one PAK must not reinspect unchanged packages.");
				File.AppendAllText(first, "changed"); read(); Assert.That(calls, Is.EqualTo(4));
				File.AppendAllText(executable, "changed helper"); read(); Assert.That(calls, Is.EqualTo(7));
				cache.Clear(); read(); Assert.That(calls, Is.EqualTo(10), "Explicit Refresh must permit a full reinspection.");
			}
		}

		/// <summary>Changing any part invalidates both the primary and continuation observations.</summary>
		[Test]
		public void DisplayCacheInvalidatesAllResultsForChangedSplitPackageInput()
		{
			using (var directory = new FixtureDirectory())
			using (var helper = new FixtureDirectory())
			{
				string executable = helper.File("Inspector.exe");
				directory.File("Split.pak"); string part = directory.File("Split_1.pak");
				int calls = 0;
				var reader = new FixtureInspector(file =>
				{
					calls++;
					return file.FileName == "Split.pak" ? new LoadOrderPackageInspection(LoadOrderInspectionStatus.Complete, LoadOrderPackageKind.Modules,
						Inspection(file.FileName).Modules, LoadOrderContentFeatures.None, new[] { "Split.pak", "Split_1.pak" })
						: new LoadOrderPackageInspection(LoadOrderInspectionStatus.Complete, LoadOrderPackageKind.Continuation, null,
							LoadOrderContentFeatures.None, new[] { "Split.pak", "Split_1.pak" }, "Split.pak");
				});
				var cache = new LoadOrderPackageInspectionCache();
				Func<LoadOrderInventory> read = () => new PackageLoadOrderInventoryProvider(DataDrivenLoadOrder.Create(Definition()), () => directory.Root,
					cache.Wrap(reader, executable)).ReadInventory(CancellationToken.None);
				read(); read(); Assert.That(calls, Is.EqualTo(2));
				File.AppendAllText(part, "changed"); read(); Assert.That(calls, Is.EqualTo(4));
			}
		}

		/// <summary>The provider closes one batch even when cancellation interrupts package inspection, including through the cache wrapper.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void ProviderDisposesInspectionBatchOnCancellation(bool cached)
		{
			using (var directory = new FixtureDirectory())
			using (var helper = new FixtureDirectory())
			using (var cancellation = new CancellationTokenSource())
			{
				directory.File("One.pak"); directory.File("Two.pak"); string executable = helper.File("Inspector.exe");
				var reader = new BatchInspector(cancellation);
				ILoadOrderPackageInspector inspector = cached ? new LoadOrderPackageInspectionCache().Wrap(reader, executable) : reader;
				Assert.Throws<OperationCanceledException>(() => Provider(directory, inspector).ReadInventory(cancellation.Token));
				Assert.That(reader.Opened, Is.EqualTo(1)); Assert.That(reader.Closed, Is.EqualTo(1));
			}
		}

		/// <summary>Transient reader failures are not frozen in the display cache.</summary>
		[Test]
		public void DisplayCacheRetriesFailedPackageInspection()
		{
			using (var directory = new FixtureDirectory())
			using (var helper = new FixtureDirectory())
			{
				string executable = helper.File("Inspector.exe"); directory.File("One.pak");
				int calls = 0;
				var reader = new FixtureInspector(file => ++calls == 1 ? new LoadOrderPackageInspection(LoadOrderInspectionStatus.Failed,
					LoadOrderPackageKind.Unknown, null, LoadOrderContentFeatures.None, null, technicalDetails: "Transient failure") : Inspection(file.FileName));
				var cache = new LoadOrderPackageInspectionCache();
				for (int scan = 0; scan < 3; scan++)
					new PackageLoadOrderInventoryProvider(DataDrivenLoadOrder.Create(Definition()), () => directory.Root, cache.Wrap(reader, executable)).ReadInventory(CancellationToken.None);
				Assert.That(calls, Is.EqualTo(2));
			}
		}

		/// <summary>Reports retain incomplete inspection evidence, exact integer strings and configured game dependencies without touching game files.</summary>
		[Test]
		public void DiagnosticReportPreservesFailuresRawVersionsAndDependencyResolution()
		{
			const string identity = "11111111-1111-1111-1111-111111111111";
			const string gameDependency = "22222222-2222-2222-2222-222222222222";
			GameModeDefinition definition = Definition(); definition.LoadOrder.BuiltInModuleIdentities = new[] { gameDependency };
			LoadOrderCapability capability = DataDrivenLoadOrder.Create(definition);
			string root = Path.Combine(Path.GetTempPath(), "nmm-report-fixture");
			var document = new LarianLoadOrderDocument(Path.Combine(root, "modsettings.lsx"), System.Text.Encoding.UTF8.GetBytes(
				"<save><version major=\"4\" minor=\"8\"/><region id=\"ModuleSettings\"><node id=\"root\"><children><node id=\"Mods\"><children>"
				+ "<node id=\"ModuleShortDesc\"><attribute id=\"UUID\" value=\"33333333-3333-3333-3333-333333333333\"/><attribute id=\"Folder\" value=\"GustavX\"/></node>"
				+ "<node id=\"ModuleShortDesc\"><attribute id=\"UUID\" value=\"" + identity + "\"/><attribute id=\"Name\" value=\"Example\"/><attribute id=\"Folder\" value=\"Example\"/><attribute id=\"Version64\" value=\"1\"/></node>"
				+ "</children></node></children></node></region></save>"));
			JObject response = Response("Good.pak");
			response["modules"][0]["dependencies"] = new JArray(new JObject { ["identity"] = gameDependency, ["name"] = "Game content", ["version"] = "18446744073709551615" });
			var packages = new[]
			{
				new LoadOrderPackageObservation(new LoadOrderPackageFile(Path.Combine(root, "Good.pak"), 1, 0), LarianPackageInspectionProtocol.ReadResponse(response.ToString(), "Good.pak")),
				new LoadOrderPackageObservation(new LoadOrderPackageFile(Path.Combine(root, "Unknown.pak"), 1, 0), new LoadOrderPackageInspection(LoadOrderInspectionStatus.Partial,
					LoadOrderPackageKind.Unknown, null, LoadOrderContentFeatures.None, new[] { "Unknown.pak" }, technicalDetails: "Solid archive inspection is unsupported."))
			};
			var inventory = new LoadOrderInventory(root, packages, false, new[] { new LoadOrderDiagnostic(LoadOrderDiagnosticKind.PackageInspectionIncomplete,
				LoadOrderDiagnosticSeverity.Error, packages[1].File.Path, technicalDetails: packages[1].Inspection.TechnicalDetails) });
			LoadOrderAnalysis analysis = LarianLoadOrderAnalyzer.Analyze(document, inventory, null, capability.BuiltInModuleIdentities);
			JObject report = JObject.Parse(LoadOrderDiagnosticReport.Create(analysis, capability, "fixture", "Public", new[] { identity }, true, false,
				new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), "Inspection observation is stale."));
			Assert.That((int)report["SchemaVersion"], Is.EqualTo(1)); Assert.That((bool)report["ObservationCurrent"], Is.False);
			Assert.That((string)report["Packages"][1]["TechnicalDetails"], Is.EqualTo(packages[1].Inspection.TechnicalDetails));
			Assert.That((string)report["Packages"][0]["Modules"][0]["Metadata"]["Version"], Is.EqualTo("18446744073709551615"));
			Assert.That((string)report["Packages"][0]["Modules"][0]["Dependencies"][0]["Resolution"], Is.EqualTo("GameProvidedByConfiguration"));
			Assert.That((string)report["Manifest"]["Sha256"], Is.EqualTo(document.Sha256));
			Assert.That((bool)report["Summary"]["CanPrepareRepair"], Is.False);
		}

		/// <summary>Builds a synthetic compatible provider without using BG3 ModeId or any actual game profile.</summary>
		private static GameModeDefinition Definition() => new GameModeDefinition
		{
			SchemaVersion = 2, ModeId = "SyntheticLarian", Name = "Synthetic modules", BehaviorProfile = "generic", GameExecutables = new[] { "Game.exe" },
			ModInstall = new GameModeModInstallDefinition { PathAdjustmentProfile = "none", SupportsGameRootInstall = false },
			LoadOrder = new GameModeLoadOrderDefinition
			{
				SchemaVersion = 1, Enabled = true, Provider = LoadOrderCapability.LarianProviderId, Revision = 1,
				PackageRoot = "ManagedInstallation", ProfileSource = "structuredEffects",
				DirectoryPolicy = new GameModeLoadOrderDirectoryDefinition { AllowedExtensions = new[] { ".pak" }, AllowSubdirectories = false }
			},
			StructuredEffects = new GameModeStructuredEffectsDefinition
			{
				SchemaVersion = 1, Provider = "profiled-attribute-records-v1", Revision = 1, MetadataFile = "info.json", MetadataArray = "Mods",
				MetadataEncoding = "Utf8", ProfileRoot = @"Fixture\Profiles", ManifestFile = "modsettings.lsx", ExcludedProfiles = new[] { "default" },
				KeyPrefix = "fixture-modules-v1|", ContainerId = "Mods", RecordId = "ModuleShortDesc", IdentityField = "UUID",
				Fields = new List<GameModeStructuredRecordFieldDefinition>
				{
					new GameModeStructuredRecordFieldDefinition { Id = "Folder", Type = "LSString", Sources = new[] { "Folder" } },
					new GameModeStructuredRecordFieldDefinition { Id = "Name", Type = "LSString", Sources = new[] { "Name" } },
					new GameModeStructuredRecordFieldDefinition { Id = "UUID", Type = "guid", Sources = new[] { "UUID" } },
					new GameModeStructuredRecordFieldDefinition { Id = "Version64", Type = "int64", Sources = new[] { "Version" } }
				}
			}
		};

		/// <summary>Creates the same flat package policy used by the declared provider.</summary>
		private static LoadOrderDirectoryPolicy Policy() => new LoadOrderDirectoryPolicy(new[] { ".pak" }, false);

		/// <summary>Binds the production orchestrator to an isolated fixture directory.</summary>
		private static PackageLoadOrderInventoryProvider Provider(FixtureDirectory directory, ILoadOrderPackageInspector inspector) =>
			new PackageLoadOrderInventoryProvider(DataDrivenLoadOrder.Create(Definition()), () => directory.Root, inspector);

		/// <summary>Creates a complete result through the same strict protocol consumed by production.</summary>
		private static LoadOrderPackageInspection Inspection(string fileName) => LarianPackageInspectionProtocol.ReadResponse(Response(fileName).ToString(), fileName);

		/// <summary>Provides a representative protocol envelope with exact integer strings.</summary>
		private static JObject Response(string fileName) => JObject.Parse(@"{
			'schemaVersion':1,'provider':'larian-module-settings-v1','fileName':'" + fileName + @"',
			'status':'Complete','kind':'Modules','features':'LuaScripts, ScriptExtender','readFiles':['" + fileName + @"'],
			'modules':[{'identity':'11111111-1111-1111-1111-111111111111','name':'Example','folder':'Example',
			'version':'18446744073709551615','publishHandle':'18446744073709551615','kind':'AddOn',
			'features':'LuaScripts, ScriptExtender','requiredScriptExtenderVersion':29,'dependencies':[],'conflicts':[]}]
		}");

		/// <summary>Injects package-specific outcomes without launching executables or reading actual packages.</summary>
		private sealed class FixtureInspector : ILoadOrderPackageInspector
		{
			private readonly Func<LoadOrderPackageFile, LoadOrderPackageInspection> _inspect;
			/// <summary>Captures one deterministic fixture reader.</summary>
			internal FixtureInspector(Func<LoadOrderPackageFile, LoadOrderPackageInspection> inspect) { _inspect = inspect; }
			public bool IsAvailable => true;
			/// <summary>Returns the configured evidence for one fixture file.</summary>
			public LoadOrderPackageInspection Inspect(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> availableFiles, CancellationToken cancellationToken) => _inspect(file);
		}

		/// <summary>Tracks provider-owned lifetimes without starting a real inspection helper.</summary>
		private sealed class BatchInspector : ILoadOrderPackageInspector, ILoadOrderPackageInspectionBatch, IDisposable
		{
			private readonly CancellationTokenSource _cancellation;
			internal int Opened, Closed;
			/// <summary>Supplies cancellation at the first actual read.</summary>
			internal BatchInspector(CancellationTokenSource cancellation) { _cancellation = cancellation; }
			public bool IsAvailable => true;
			/// <summary>Records entry into the scan lifetime.</summary>
			public IDisposable BeginBatch(IReadOnlyList<LoadOrderPackageFile> files) { Opened++; return this; }
			/// <summary>Interrupts the scan before it can return package metadata.</summary>
			public LoadOrderPackageInspection Inspect(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> files, CancellationToken token)
			{ _cancellation.Cancel(); token.ThrowIfCancellationRequested(); return null; }
			/// <summary>Records release of the provider-owned lifetime.</summary>
			public void Dispose() { Closed++; }
		}

		/// <summary>Collects progress synchronously so fixtures can inspect event ordering without a UI message loop.</summary>
		private sealed class FixtureProgress : IProgress<LoadOrderInspectionProgress>
		{
			internal readonly List<LoadOrderInspectionProgress> Values = new List<LoadOrderInspectionProgress>();
			/// <summary>Retains the immutable progress observation.</summary>
			public void Report(LoadOrderInspectionProgress value) { Values.Add(value); }
		}

		/// <summary>Owns only a uniquely named temporary directory used by these regression cases.</summary>
		private sealed class FixtureDirectory : IDisposable
		{
			public string Root { get; } = Path.Combine(Path.GetTempPath(), "nmm-load-order-" + Guid.NewGuid().ToString("N"));
			/// <summary>Creates the isolated fixture root.</summary>
			internal FixtureDirectory() { Directory.CreateDirectory(Root); }
			/// <summary>Creates one top-level fixture file.</summary>
			internal string File(string name) { string path = Path.Combine(Root, name); System.IO.File.WriteAllText(path, "fixture"); return path; }
			/// <summary>Removes only this fixture's own temporary files.</summary>
			public void Dispose()
			{
				foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) System.IO.File.SetAttributes(file, FileAttributes.Normal);
				Directory.Delete(Root, true);
			}
		}
	}
}
