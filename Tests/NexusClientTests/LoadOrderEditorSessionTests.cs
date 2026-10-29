using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.ModManagement.LoadOrder.UI;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	/// <summary>Detached UI-state regression source; no controls, helper processes or real game files are created.</summary>
	[TestFixture]
	public sealed class LoadOrderEditorSessionTests
	{
		private const string First = "11111111-1111-1111-1111-111111111111";
		private const string Second = "22222222-2222-2222-2222-222222222222";
		private const string Third = "33333333-3333-3333-3333-333333333333";
		private const string Campaign = "44444444-4444-4444-4444-444444444444";

		/// <summary>One list distinguishes fixed game records, included/excluded modules and independent physical overrides.</summary>
		[Test]
		public void OneListSeparatesModuleInclusionFromPhysicalOverrides()
		{
			LoadOrderPackageObservation overrides = Package("Override.pak", new LoadOrderPackageInspection(LoadOrderInspectionStatus.Complete, LoadOrderPackageKind.Override, null, LoadOrderContentFeatures.None, new[] { "Override.pak" }));
			LoadOrderInventory inventory = Inventory(new[] { Module(First), Module(Second) }, overrides);
			var session = new LoadOrderEditorSession(LarianLoadOrderAnalyzer.Analyze(Document(false, First), inventory));
			LoadOrderModuleRow[] rows = session.Rows().ToArray();
			Assert.That(rows.Take(3).Select(row => row.Identity), Is.EqualTo(new[] { Campaign, First, Second }));
			Assert.That(rows[0].CanToggle, Is.False); Assert.That(rows[0].Position, Is.Null);
			Assert.That(rows[1].Included, Is.True); Assert.That(rows[1].Position, Is.EqualTo(1));
			Assert.That(rows[2].Included, Is.False); Assert.That(rows[2].CanToggle, Is.True);
			Assert.That(rows.Last().Included, Is.Null); Assert.That(rows.Last().CanToggle, Is.False);
			Assert.That(rows.Last().Badges.Split(','), Does.Contain("Override"));
		}

		/// <summary>Returning inclusion/order to the original observation removes the dirty state without writing anything.</summary>
		[Test]
		public void DirtyStateTracksRealInclusionAndOrderChanges()
		{
			var session = Session(false, new[] { First }, Module(First), Module(Second));
			byte[] original = (byte[])session.Analysis.Document.Bytes.Clone();
			session.Include(Row(session, Second), true); Assert.That(session.IsDirty, Is.True);
			session.Move(Row(session, Second), -1); Assert.That(session.Included, Is.EqualTo(new[] { Second, First }));
			session.Move(Row(session, Second), 1); session.Include(Row(session, Second), false);
			Assert.That(session.IsDirty, Is.False); Assert.That(session.Analysis.Document.Bytes, Is.EqualTo(original));
		}

		/// <summary>Drag positioning accounts for removal of the source when moving down or up across several rows.</summary>
		[Test]
		public void RelativeMovesRespectBeforeAndAfterPositions()
		{
			var session = Session(false, new[] { First, Second, Third }, Module(First), Module(Second), Module(Third));
			session.MoveRelative(Row(session, First), Row(session, Third), true);
			Assert.That(session.Included, Is.EqualTo(new[] { Second, Third, First }));
			session.MoveRelative(Row(session, First), Row(session, Second), false);
			Assert.That(session.Included, Is.EqualTo(new[] { First, Second, Third }));
			session.MoveRelative(Row(session, Second), Row(session, Second), true);
			Assert.That(session.IsDirty, Is.False);
		}

		/// <summary>Fixed game records cannot be removed or used as a drag destination.</summary>
		[Test]
		public void FixedGameRecordsRejectEditingAndDrops()
		{
			var session = Session(false, new[] { First }, Module(First));
			Assert.Throws<InvalidOperationException>(() => session.Include(Row(session, Campaign), false));
			Assert.Throws<InvalidOperationException>(() => session.MoveRelative(Row(session, First), Row(session, Campaign), false));
			Assert.That(session.IsDirty, Is.False);
		}

		/// <summary>Incomplete inspection retains positive script evidence while preventing misleading editing permissions.</summary>
		[Test]
		public void PartialInspectionShowsEvidenceWithoutEnablingEdits()
		{
			LoadOrderModuleInfo module = Module(First, LoadOrderContentFeatures.LuaScripts);
			var inventory = new LoadOrderInventory(Root, new[] { Package("Partial.pak", new LoadOrderPackageInspection(LoadOrderInspectionStatus.Partial, LoadOrderPackageKind.Modules,
				new[] { module }, LoadOrderContentFeatures.LuaScripts, new[] { "Partial.pak" })) }, false, new LoadOrderDiagnostic[0]);
			var session = new LoadOrderEditorSession(LarianLoadOrderAnalyzer.Analyze(Document(false, First), inventory));
			Assert.That(session.Draft, Is.Null); Assert.That(Row(session, First).CanToggle, Is.False);
			Assert.That(Row(session, First).Badges.Split(','), Does.Contain("Lua"));
			Assert.That(Row(session, First).Badges.Split(','), Does.Contain("Unverified"));
		}

		/// <summary>Duplicate UUIDs remain one ambiguous module row with every source path and no arbitrary winning metadata.</summary>
		[Test]
		public void DuplicateSourcesDoNotChooseEditableMetadata()
		{
			var session = Session(false, new[] { First }, Module(First), Module(First));
			LoadOrderModuleRow row = Row(session, First);
			Assert.That(row.Module, Is.Null); Assert.That(row.Packages.Length, Is.EqualTo(2)); Assert.That(row.CanToggle, Is.False);
		}

		/// <summary>The UI does not offer a modern metadata addition to a legacy manifest that the native draft would reject.</summary>
		[Test]
		public void LegacyAvailableModulesCannotBeNewlyIncluded()
		{
			var session = Session(true, new[] { First }, Module(First), Module(Second));
			Assert.That(Row(session, First).CanToggle, Is.True); Assert.That(Row(session, Second).CanToggle, Is.False);
		}

		/// <summary>A declared extender version is visible as a requirement badge even without a redundant content flag.</summary>
		[Test]
		public void RequiredExtenderVersionAppearsInDetailsAndBadges()
		{
			LoadOrderModuleInfo module = new LoadOrderModuleInfo(Metadata(First), LoadOrderModuleKind.AddOn, LoadOrderContentFeatures.None, 29, null, null);
			var session = Session(false, new[] { First }, module);
			Assert.That(Row(session, First).Badges.Split(','), Does.Contain("Extender"));
			StringAssert.Contains("29", Row(session, First).Details());
		}

		/// <summary>Missing dependency findings have readable author-provided subjects rather than only opaque UUIDs.</summary>
		[Test]
		public void DependencyNamesAreAvailableForReadableDirections()
		{
			LoadOrderModuleInfo module = new LoadOrderModuleInfo(Metadata(First), LoadOrderModuleKind.AddOn, LoadOrderContentFeatures.None, null,
				new[] { new LoadOrderModuleReference(Third, "Required framework", "1") }, null);
			var session = Session(false, new[] { First }, module);
			Assert.That(session.NameFor(Third), Is.EqualTo("Required framework"));
		}

		/// <summary>Editing one detached profile never changes another profile's inclusion or dirty state.</summary>
		[Test]
		public void ProfilesHaveIndependentDrafts()
		{
			var first = Session(false, new[] { First }, Module(First), Module(Second));
			var second = Session(false, new[] { Second }, Module(First), Module(Second));
			first.Include(Row(first, Second), true);
			Assert.That(second.IsDirty, Is.False); Assert.That(second.Included, Is.EqualTo(new[] { Second }));
		}

		/// <summary>Saved positions are visible without package inspection, inferred repairs or an editable draft.</summary>
		[Test]
		public void PreviewShowsSavedOrderWithoutInventingMissingPackageProblems()
		{
			LarianLoadOrderDocument document = Document(false, Second, First);
			byte[] original = (byte[])document.Bytes.Clone();
			LoadOrderEditorSession preview = LoadOrderEditorSession.Preview(document);
			Assert.That(preview.Included, Is.EqualTo(new[] { Second, First }));
			Assert.That(preview.Rows().Select(row => row.Identity), Is.EqualTo(new[] { Campaign, Second, First }));
			Assert.That(preview.IsInspecting, Is.True); Assert.That(preview.Draft, Is.Null);
			Assert.That(preview.Rows().All(row => !row.CanToggle), Is.True);
			Assert.That(preview.Analysis.Issues, Is.Empty); Assert.That(preview.Analysis.HasAutomaticRepairs, Is.False);
			Assert.That(preview.IsDirty, Is.False); Assert.That(document.Bytes, Is.EqualTo(original));
		}

		/// <summary>Partial evidence can add badges and available modules while saved positions stay fixed and all edits stay locked.</summary>
		[Test]
		public void PreviewEnrichmentKeepsSavedPositionsAndRemainsReadOnly()
		{
			LarianLoadOrderDocument document = Document(false, Second, First);
			LoadOrderInventory inventory = Inventory(new[] { Module(First, LoadOrderContentFeatures.LuaScripts), Module(Third) });
			LoadOrderEditorSession preview = LoadOrderEditorSession.Preview(document, inventory.Packages);
			Assert.That(preview.Included, Is.EqualTo(new[] { Second, First }));
			Assert.That(Row(preview, Second).Position, Is.EqualTo(1)); Assert.That(Row(preview, First).Position, Is.EqualTo(2));
			Assert.That(Row(preview, First).Badges.Split(','), Does.Contain("Lua"));
			Assert.That(Row(preview, Third).Included, Is.False); Assert.That(preview.Analysis.Issues, Is.Empty);
			Assert.Throws<InvalidOperationException>(() => preview.Include(Row(preview, Third), true));
			Assert.Throws<InvalidOperationException>(() => preview.Move(Row(preview, First), -1));
		}

		private static string Root => Path.Combine(Path.GetTempPath(), "nmm-order-ui-fixture");
		/// <summary>Builds one complete in-memory editor observation.</summary>
		private static LoadOrderEditorSession Session(bool legacy, string[] ids, params LoadOrderModuleInfo[] modules) => new LoadOrderEditorSession(LarianLoadOrderAnalyzer.Analyze(Document(legacy, ids), Inventory(modules)));
		/// <summary>Finds one logical module rather than relying on a grid handle or translated text.</summary>
		private static LoadOrderModuleRow Row(LoadOrderEditorSession session, string id) => session.Rows().Single(row => row.Identity == id);
		/// <summary>Produces consistent package/native display metadata.</summary>
		private static LoadOrderModuleMetadata Metadata(string id) => new LoadOrderModuleMetadata(id, "Mod " + id[0], "Folder" + id[0], "1");
		/// <summary>Supplies an independently scoped module observation.</summary>
		private static LoadOrderModuleInfo Module(string id, LoadOrderContentFeatures features = LoadOrderContentFeatures.None) => new LoadOrderModuleInfo(Metadata(id), LoadOrderModuleKind.AddOn, features, null, null, null);
		/// <summary>Creates complete synthetic package evidence with no filesystem reads.</summary>
		private static LoadOrderInventory Inventory(LoadOrderModuleInfo[] modules, params LoadOrderPackageObservation[] extra) => new LoadOrderInventory(Root,
			modules.Select((module, index) => Package(index + ".pak", new LoadOrderPackageInspection(LoadOrderInspectionStatus.Complete, LoadOrderPackageKind.Modules, new[] { module }, module.Features, new[] { index + ".pak" }))).Concat(extra), true, new LoadOrderDiagnostic[0]);
		/// <summary>Associates a metadata result with an absolute but uncreated fixture path.</summary>
		private static LoadOrderPackageObservation Package(string name, LoadOrderPackageInspection inspection) => new LoadOrderPackageObservation(new LoadOrderPackageFile(Path.Combine(Root, name), 1, 0), inspection);
		/// <summary>Creates native bytes in memory, retaining the mandatory existing campaign and optional legacy order.</summary>
		private static LarianLoadOrderDocument Document(bool legacy, params string[] ids)
		{
			string records = Record(Campaign, "GustavX", "GustavX") + String.Concat(ids.Select(id => Record(id, Metadata(id).Folder, Metadata(id).Name)));
			string order = legacy ? "<node id=\"ModOrder\"><children>" + String.Concat(new[] { Campaign }.Concat(ids).Select(id => "<node id=\"Module\"><attribute id=\"UUID\" value=\"" + id + "\"/></node>")) + "</children></node>" : String.Empty;
			return new LarianLoadOrderDocument(Path.Combine(Root, "modsettings.lsx"), Encoding.UTF8.GetBytes("<save><version major=\"4\" minor=\"" + (legacy ? "6" : "8") + "\"/><region id=\"ModuleSettings\"><node id=\"root\"><children><node id=\"Mods\"><children>" + records + "</children></node>" + order + "</children></node></region></save>"));
		}
		/// <summary>Builds one native module record using fixture constants only.</summary>
		private static string Record(string id, string folder, string name) => "<node id=\"ModuleShortDesc\"><attribute id=\"UUID\" value=\"" + id + "\"/><attribute id=\"Folder\" value=\"" + folder + "\"/><attribute id=\"Name\" value=\"" + name + "\"/><attribute id=\"Version64\" value=\"1\"/></node>";
	}
}
