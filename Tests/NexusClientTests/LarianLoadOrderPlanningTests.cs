using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.ModManagement.ManagedContent;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	/// <summary>Pure diagnosis, curator precedence and lifecycle regression source; never compiled or executed by the agent.</summary>
	[TestFixture]
	public sealed class LarianLoadOrderPlanningTests
	{
		private const string First = "11111111-1111-1111-1111-111111111111";
		private const string Second = "22222222-2222-2222-2222-222222222222";
		private const string Third = "33333333-3333-3333-3333-333333333333";
		private const string Campaign = "44444444-4444-4444-4444-444444444444";

		/// <summary>Dependency constraints supersede old positions, with stable tie-breaking between available nodes.</summary>
		[Test]
		public void SortsDependenciesStably()
		{
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First, Second, Third), Inventory(Module(First, Third), Module(Second), Module(Third)));
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { Second, Third, First }));
			Assert.That(result.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.DependencyOrder), Is.True);
			Assert.That(result.Repairs.Any(step => step.Kind == LoadOrderRepairKind.Sort), Is.True);
		}

		/// <summary>A complete installed dependency closure can be enabled without enabling unrelated packages.</summary>
		[Test]
		public void IncludesOnlyRequiredAvailableDependencies()
		{
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First), Inventory(Module(First, Second), Module(Second), Module(Third)));
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { Second, First }));
			Assert.That(result.Repairs.Where(step => step.Kind == LoadOrderRepairKind.IncludeDependency).Select(step => step.ModuleIdentity), Is.EqualTo(new[] { Second }));
		}

		/// <summary>Saved exclusions are deliberate choices, rather than permission for auto-fix to reactivate a dependency.</summary>
		[Test]
		public void DoesNotOverrideExplicitDependencyExclusion()
		{
			var policy = new LarianOrderPreference { Source = "Manual", Order = new[] { First }, Excluded = new[] { Second } };
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First), Inventory(Module(First, Second), Module(Second)), policy);
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { First }));
			Assert.That(result.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.ExcludedDependency && !issue.AutomaticallyRepairable), Is.True);
		}

		/// <summary>Vanilla dependencies come from game configuration and are never mistaken for missing downloadable user packages.</summary>
		[Test]
		public void ConfiguredBuiltInDependencyDoesNotBlockAvailableClosure()
		{
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First), Inventory(Module(First, Second)), null, new[] { Second });
			Assert.That(result.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.MissingDependency), Is.False);
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { First }));
		}

		/// <summary>Partial/ambiguous inventory cannot become permission to remove or reorder a native record.</summary>
		[Test]
		public void DuplicateIdentityBlocksRepairEvenWithoutProviderDiagnostics()
		{
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First), Inventory(Module(First), Module(First)));
			Assert.That(result.CanPrepareRepair, Is.False);
			Assert.That(result.Repairs, Is.Empty);
		}

		/// <summary>Cycles remain visible and are not hidden by an arbitrary deterministic but invalid order.</summary>
		[Test]
		public void DependencyCycleRetainsOriginalOrder()
		{
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First, Second), Inventory(Module(First, Second), Module(Second, First)));
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { First, Second }));
			Assert.That(result.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.DependencyCycle), Is.True);
			Assert.That(result.Repairs.Any(step => step.Kind == LoadOrderRepairKind.Sort), Is.False);
		}

		/// <summary>Auto-inclusion does not activate an installed dependency whose packed version is below its declared requirement.</summary>
		[Test]
		public void VersionGapRequiresAnUpdateInsteadOfAutoInclusion()
		{
			LoadOrderModuleInfo first = Module(First, Second, requiredVersion: "2");
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First), Inventory(first, Module(Second)));
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { First }));
			Assert.That(result.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.DependencyVersion), Is.True);
			Assert.That(result.Issues.Single(issue => issue.Kind == LoadOrderIssueKind.DependencyVersion).Severity, Is.EqualTo(LoadOrderDiagnosticSeverity.Warning));
		}

		/// <summary>Declared incompatibilities cannot be resolved by enabling the missing member of a pair.</summary>
		[Test]
		public void AutoInclusionDoesNotIntroduceDeclaredConflict()
		{
			LoadOrderModuleInfo second = new LoadOrderModuleInfo(Metadata(Second), LoadOrderModuleKind.AddOn, LoadOrderContentFeatures.None, null, null, new[] { new LoadOrderModuleReference(First, null, null) });
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First), Inventory(Module(First, Second), second));
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { First }));
		}

		/// <summary>Not matching an installed package never authorizes deleting a saved module, even after a complete scan.</summary>
		[Test]
		public void UnmatchedSavedEntriesAreNeverAutomaticallyRemoved()
		{
			LoadOrderInventory complete = Inventory(Module(First));
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First, Second), complete);
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { First, Second }));
			Assert.That(result.Repairs.Any(step => step.Kind == LoadOrderRepairKind.RemoveMissingRecord), Is.False);
			Assert.That(result.Issues.Single(issue => issue.Kind == LoadOrderIssueKind.MissingPackage).Severity, Is.EqualTo(LoadOrderDiagnosticSeverity.Warning));
			Assert.That(result.HasAutomaticRepairs, Is.False);
			var partial = new LoadOrderInventory(complete.PackageRoot, complete.Packages, false, new LoadOrderDiagnostic[0]);
			LoadOrderAnalysis unverified = LarianLoadOrderAnalyzer.Analyze(Document(First, Second), partial);
			Assert.That(unverified.Repairs, Is.Empty);
			Assert.That(unverified.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.MissingPackage), Is.False);
			Assert.That(unverified.Issues.Single(issue => issue.Kind == LoadOrderIssueKind.UnverifiedModule).Severity, Is.EqualTo(LoadOrderDiagnosticSeverity.Warning));
		}

		/// <summary>An unresolved package cannot authorize hypothetical curator reordering, exclusion or metadata changes.</summary>
		[TestCase(LoadOrderInspectionStatus.Failed)]
		[TestCase(LoadOrderInspectionStatus.Partial)]
		[TestCase(LoadOrderInspectionStatus.Unavailable)]
		public void ReadFailureRetainsExactOrderAndDisablesAutomaticChanges(LoadOrderInspectionStatus status)
		{
			LarianLoadOrderDocument document = Document(Third, First, Second);
			LoadOrderInventory known = Inventory(Module(First), Module(Second));
			var failed = new LoadOrderPackageObservation(new LoadOrderPackageFile(Path.Combine(known.PackageRoot, "Unreadable.pak"), 1, 0),
				new LoadOrderPackageInspection(status, LoadOrderPackageKind.Unknown, null, LoadOrderContentFeatures.None, null));
			var inventory = new LoadOrderInventory(known.PackageRoot, known.Packages.Concat(new[] { failed }), false,
				new[] { new LoadOrderDiagnostic(LoadOrderDiagnosticKind.PackageInspectionFailed, LoadOrderDiagnosticSeverity.Error, failed.File.Path) });
			var policy = new LarianOrderPreference { Source = "Curator", Managed = new[] { First, Second, Third }, Order = new[] { Second, First }, Excluded = new[] { Third } };
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(document, inventory, policy);
			CollectionAssert.AreEqual(new[] { Third, First, Second }, result.SuggestedOrder);
			Assert.That(result.CanPrepareRepair, Is.False); Assert.That(result.Repairs, Is.Empty);
			Assert.That(result.Issues.All(issue => !issue.AutomaticallyRepairable), Is.True);
			var draft = new NativeLoadOrderDraft(document, inventory);
			CollectionAssert.AreEqual(document.Bytes, draft.Prepare().After.Bytes);
		}

		/// <summary>A historical policy cannot automatically exclude a saved record with no positively identified package.</summary>
		[Test]
		public void SavedExclusionDoesNotRemoveUnverifiedEntry()
		{
			var policy = new LarianOrderPreference { Source = "Manual", Order = new[] { First }, Excluded = new[] { Second } };
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First, Second), Inventory(Module(First)), policy);
			CollectionAssert.AreEqual(new[] { First, Second }, result.SuggestedOrder);
			Assert.That(result.Repairs.Any(step => step.Kind == LoadOrderRepairKind.RemoveExcludedRecord || step.Kind == LoadOrderRepairKind.RemoveMissingRecord), Is.False);
		}

		/// <summary>A reader failure cannot establish that an unresolved dependency is missing or authorize repairs.</summary>
		[Test]
		public void IncompleteInventoryReportsUnverifiedDependenciesWithTechnicalEvidence()
		{
			LoadOrderInventory complete = Inventory(Module(First, Second));
			var diagnostic = new LoadOrderDiagnostic(LoadOrderDiagnosticKind.PackageInspectionIncomplete, LoadOrderDiagnosticSeverity.Error,
				Path.Combine(complete.PackageRoot, "Unknown.pak"), technicalDetails: "Solid archive inspection is unsupported.");
			var partial = new LoadOrderInventory(complete.PackageRoot, complete.Packages, false, new[] { diagnostic });
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First), partial);
			Assert.That(result.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.MissingDependency), Is.False);
			Assert.That(result.Issues.Single(issue => issue.Kind == LoadOrderIssueKind.UnverifiedDependency).Severity, Is.EqualTo(LoadOrderDiagnosticSeverity.Warning));
			LoadOrderIssue inspection = result.Issues.Single(issue => issue.Kind == LoadOrderIssueKind.Inventory);
			Assert.That(inspection.Severity, Is.EqualTo(LoadOrderDiagnosticSeverity.Warning));
			StringAssert.Contains(diagnostic.TechnicalDetails, inspection.TechnicalDetails);
			Assert.That(result.CanPrepareRepair, Is.False); Assert.That(result.Repairs, Is.Empty);
		}

		/// <summary>The curator's supplied order wins over both the existing order and a contradictory dependency declaration.</summary>
		[Test]
		public void CuratorOrderWinsAndContradictionIsReported()
		{
			var policy = new LarianOrderPreference { Source = "Curator", Managed = new[] { First, Second }, Order = new[] { Second, First }, Excluded = new string[0] };
			LoadOrderAnalysis result = LarianLoadOrderAnalyzer.Analyze(Document(First, Second), Inventory(Module(First), Module(Second, First)), policy);
			Assert.That(result.SuggestedOrder, Is.EqualTo(new[] { Second, First }));
			Assert.That(result.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.CuratorDependencyConflict), Is.True);
		}

		/// <summary>Metadata refresh corrects a package version without discarding foreign native fields.</summary>
		[Test]
		public void MetadataRefreshPreservesForeignFields()
		{
			LarianLoadOrderDocument document = Document(First);
			LoadOrderInventory inventory = Inventory(new LoadOrderModuleInfo(Metadata(First, "2"), LoadOrderModuleKind.AddOn, LoadOrderContentFeatures.None, null, null, null));
			LoadOrderAnalysis analysis = LarianLoadOrderAnalyzer.Analyze(document, inventory);
			Assert.That(analysis.Repairs.Any(step => step.Kind == LoadOrderRepairKind.RefreshMetadata), Is.True);
			LoadOrderIssue difference = analysis.Issues.Single(issue => issue.Kind == LoadOrderIssueKind.NativeMetadata);
			Assert.That(difference.Severity, Is.EqualTo(LoadOrderDiagnosticSeverity.Warning));
			StringAssert.Contains("Version64: saved = 1; package = 2", difference.TechnicalDetails);
			var draft = new NativeLoadOrderDraft(document, inventory); draft.RefreshMetadata(First);
			LarianLoadOrderDocument result = draft.Prepare().After;
			Assert.That(LarianLoadOrderDocument.Field(result.Records[First], "Version64"), Is.EqualTo("2"));
			Assert.That(LarianLoadOrderDocument.Field(result.Records[First], "ForeignField"), Is.EqualTo("keep"));
		}

		/// <summary>An explicitly applied curator policy restores its requested positions while keeping unrelated additions stable.</summary>
		[Test]
		public void ExplicitCuratorPreferenceWinsOverPresentOrder()
		{
			LarianLoadOrderDocument document = Document(Third, First, Second);
			var policy = new LarianOrderPreference { Source = "Curator", Managed = new[] { First, Second }, Order = new[] { Second, First }, Excluded = new string[0] };
			var result = new LarianLoadOrderDocument(document.FilePath, LarianOrderPreference.Apply(document.Bytes, document.FilePath, policy, true));
			Assert.That(result.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity), Is.EqualTo(new[] { Second, First, Third }));
		}

		/// <summary>Having a saved curator policy alone never authorizes replaying its positions or unrelated exclusions.</summary>
		[Test]
		public void DefaultPreferenceApplicationPreservesExactCurrentOrder()
		{
			LarianLoadOrderDocument document = Document(Third, First, Second);
			var policy = new LarianOrderPreference { Source = "Curator", Managed = new[] { First, Second }, Order = new[] { Second, First }, Excluded = new[] { Third } };
			CollectionAssert.AreEqual(document.Bytes, LarianOrderPreference.Apply(document.Bytes, document.FilePath, policy));
		}

		/// <summary>Collection replay takes foreign records from the current file, rather than reintroducing stale retained records.</summary>
		[Test]
		public void ScopedReplayPreservesCurrentForeignModules()
		{
			byte[] merged = BaldursGate3CollectionRecordStore.MergeManaged(Aggregate(Document(First, Third)), Aggregate(Document(First, Second)), new[] { First });
			string xml = Encoding.UTF8.GetString(merged);
			StringAssert.Contains(Third, xml); StringAssert.DoesNotContain(Second, xml); StringAssert.Contains(Campaign, xml);
		}

		/// <summary>New curator-scoped verification permits unrelated modules, while legacy recipes still require exact values.</summary>
		[Test]
		public void ScopedVerificationRequiresEveryCuratorRecord()
		{
			string key = BaldursGate3CollectionRecordStore.Prefix + "Public|" + BaldursGate3CollectionRecordStore.Identity;
			var preference = new LarianOrderPreference { Source = "Curator", Managed = new[] { First }, Order = new[] { First }, Excluded = new string[0] };
			var effects = new[] { new CollectionPlannedGameValueEffect(LarianOrderPreference.Key("Public"), preference.Encode()) };
			Assert.That(CollectionNativeOrderEffectComparer.Matches(key, Aggregate(Document(First)), Aggregate(Document(First, Third)), effects), Is.True);
			Assert.That(CollectionNativeOrderEffectComparer.Matches(key, Aggregate(Document(First)), Aggregate(Document(Third)), effects), Is.False);
			Assert.That(CollectionNativeOrderEffectComparer.Matches(key, Aggregate(Document(First)), Aggregate(Document(First, Third)), new CollectionPlannedGameValueEffect[0]), Is.False);
		}

		/// <summary>Profile companions are target-bound and preserve lossless packed versions and missing legacy companions.</summary>
		[Test]
		public void ProfileCompanionRejectsDifferentGameAndFutureSchema()
		{
			Assert.That(NativeOrderProfileState.Decode(null, "fixture"), Is.Null);
			Assert.Throws<InvalidDataException>(() => NativeOrderProfileState.Decode(Encoding.UTF8.GetBytes("{\"Version\":2,\"ModeId\":\"fixture\",\"Profiles\":{}}"), "fixture"));
			Assert.Throws<InvalidDataException>(() => NativeOrderProfileState.Decode(Encoding.UTF8.GetBytes("{\"Version\":1,\"ModeId\":\"other\",\"Profiles\":{}}"), "fixture"));
		}

		/// <summary>Constructs pure native XML observations without touching a real profile.</summary>
		private static LarianLoadOrderDocument Document(params string[] ids)
		{
			string records = Record(Campaign, "GustavX") + String.Concat(ids.Select(id => Record(id, Name(id))));
			return new LarianLoadOrderDocument(Path.Combine(Path.GetTempPath(), "nmm-order-fixture", "modsettings.lsx"), Encoding.UTF8.GetBytes("<save><version major=\"4\" minor=\"8\"/><region id=\"ModuleSettings\"><node id=\"root\"><children><node id=\"Mods\"><children>" + records + "</children></node></children></node></region></save>"));
		}
		/// <summary>Builds consistent native fields plus foreign XML to exercise preservation.</summary>
		private static string Record(string id, string name) => "<node id=\"ModuleShortDesc\"><attribute id=\"UUID\" value=\"" + id + "\"/><attribute id=\"Folder\" value=\"" + name + "\"/><attribute id=\"Name\" value=\"" + name + "\"/><attribute id=\"Version64\" value=\"1\"/><attribute id=\"ForeignField\" value=\"keep\"/></node>";
		/// <summary>Uses deterministic display names in matching package and native observations.</summary>
		private static string Name(string id) => "Module" + id[0];
		/// <summary>Creates lossless package metadata.</summary>
		private static LoadOrderModuleMetadata Metadata(string id, string version = "1") => new LoadOrderModuleMetadata(id, Name(id), Name(id), version);
		/// <summary>Creates a module with an optional declared dependency.</summary>
		private static LoadOrderModuleInfo Module(string id, string dependency = null, string requiredVersion = "0") => new LoadOrderModuleInfo(Metadata(id), LoadOrderModuleKind.AddOn, LoadOrderContentFeatures.None, null,
			dependency == null ? null : new[] { new LoadOrderModuleReference(dependency, null, requiredVersion) }, null);
		/// <summary>Supplies detached complete observations; no helper process is started.</summary>
		private static LoadOrderInventory Inventory(params LoadOrderModuleInfo[] modules)
		{
			string root = Path.Combine(Path.GetTempPath(), "nmm-order-fixture");
			return new LoadOrderInventory(root, modules.Select((module, index) => new LoadOrderPackageObservation(new LoadOrderPackageFile(Path.Combine(root, index + ".pak"), 1, 0),
				new LoadOrderPackageInspection(LoadOrderInspectionStatus.Complete, LoadOrderPackageKind.Modules, new[] { module }, module.Features, new[] { index + ".pak" }))), true, new LoadOrderDiagnostic[0]);
		}
		/// <summary>Uses the retained native Collection payload format.</summary>
		private static byte[] Aggregate(LarianLoadOrderDocument document) => Encoding.UTF8.GetBytes("<bg3-order>" + document.Mods.OuterXml + "</bg3-order>");
	}
}
