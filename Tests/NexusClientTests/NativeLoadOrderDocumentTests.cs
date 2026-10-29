using System;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement.LoadOrder;
using NUnit.Framework;

namespace Nexus.Client.Tests
{
	/// <summary>Isolated native LSX read/draft regression source; compiling and running is reserved for the maintainer.</summary>
	[TestFixture]
	public sealed class NativeLoadOrderDocumentTests
	{
		private const string First = "11111111-1111-1111-1111-111111111111";
		private const string Second = "22222222-2222-2222-2222-222222222222";
		private const string Campaign = "33333333-3333-3333-3333-333333333333";

		/// <summary>Current order is taken from Mods, and reordering preserves game records and unknown fields.</summary>
		[Test]
		public void CurrentDraftReordersWithoutDiscardingForeignXml()
		{
			using (var fixture = new Fixture(false))
			{
				LarianLoadOrderDocument observed = LarianLoadOrderDocument.Read(fixture.Manifest);
				var draft = new NativeLoadOrderDraft(observed, fixture.Inventory());
				draft.Move(Second, 0);
				NativeLoadOrderPreview preview = draft.Prepare();
				Assert.That(preview.After.Entries.Select(entry => entry.Identity), Is.EqualTo(new[] { Campaign, Second, First }));
				StringAssert.Contains("ForeignField", preview.After.Xml.OuterXml);
				StringAssert.Contains("UnrelatedSettings", preview.After.Xml.OuterXml);
				Assert.That(File.ReadAllBytes(fixture.Manifest), Is.EqualTo(observed.Bytes));
			}
		}

		/// <summary>A no-op review retains exact original bytes rather than reserializing the file.</summary>
		[Test]
		public void NoOpDraftRetainsOriginalBytes()
		{
			using (var fixture = new Fixture(false))
			{
				var draft = new NativeLoadOrderDraft(LarianLoadOrderDocument.Read(fixture.Manifest), fixture.Inventory());
				Assert.That(draft.Prepare().IsChanged, Is.False);
			}
		}

		/// <summary>Legacy effective order is read from ModOrder and both native lists are updated consistently.</summary>
		[Test]
		public void LegacyDraftUpdatesBothNativeLists()
		{
			using (var fixture = new Fixture(true))
			{
				var draft = new NativeLoadOrderDraft(LarianLoadOrderDocument.Read(fixture.Manifest), fixture.Inventory());
				draft.Move(Second, 0);
				LarianLoadOrderDocument result = draft.Prepare().After;
				Assert.That(result.UsesLegacyModOrder, Is.True);
				Assert.That(result.Entries.Select(entry => entry.Identity), Is.EqualTo(new[] { Campaign, Second, First }));
			}
		}

		/// <summary>Removing a module from native inclusion is a draft edit, not a file-disable operation.</summary>
		[Test]
		public void InclusionEditsPreserveCampaignAndOriginalFile()
		{
			using (var fixture = new Fixture(false))
			{
				var draft = new NativeLoadOrderDraft(LarianLoadOrderDocument.Read(fixture.Manifest), fixture.Inventory());
				Assert.Throws<InvalidOperationException>(() => draft.SetIncluded(Campaign, false));
				draft.SetIncluded(First, false);
				Assert.That(draft.Prepare().After.Entries.Any(entry => entry.Identity == First), Is.False);
				Assert.That(LarianLoadOrderDocument.Read(fixture.Manifest).Entries.Any(entry => entry.Identity == First), Is.True);
			}
		}

		/// <summary>Duplicate identities and future formats are rejected instead of silently normalized.</summary>
		[Test]
		public void RejectsDuplicateRecordsAndUnsupportedVersion()
		{
			using (var fixture = new Fixture(false))
			{
				string original = File.ReadAllText(fixture.Manifest);
				File.WriteAllText(fixture.Manifest, original.Replace(Second, First));
				Assert.Throws<InvalidDataException>(() => LarianLoadOrderDocument.Read(fixture.Manifest));
				File.WriteAllText(fixture.Manifest, original.Replace("minor=\"8\"", "minor=\"9\""));
				Assert.Throws<InvalidDataException>(() => LarianLoadOrderDocument.Read(fixture.Manifest));
			}
		}

		/// <summary>Pending publication evidence stops the existing native record-store mutation paths.</summary>
		[Test]
		public void PendingPublicationRequiresRecovery()
		{
			using (var fixture = new Fixture(false))
			{
				File.WriteAllText(fixture.Manifest + NativeLoadOrderService.PendingSuffix, "interrupted");
				Assert.Throws<InvalidDataException>(() => NativeLoadOrderService.RequireNoPendingManifest(fixture.Manifest));
				Assert.That(File.ReadAllText(fixture.Manifest + NativeLoadOrderService.PendingSuffix), Is.EqualTo("interrupted"));
			}
		}

		/// <summary>Owns one temporary manifest only; no real package or game profile is read.</summary>
		private sealed class Fixture : IDisposable
		{
			private readonly string _root = Path.Combine(Path.GetTempPath(), "nmm-native-order-" + Guid.NewGuid().ToString("N"));
			internal string Manifest => Path.Combine(_root, "modsettings.lsx");
			/// <summary>Creates a representative complete manifest with foreign XML retained for review.</summary>
			internal Fixture(bool legacy)
			{
				Directory.CreateDirectory(_root);
				string order = legacy ? "<node id=\"ModOrder\"><children>" + Ordered(Campaign) + Ordered(First) + Ordered(Second) + "</children></node>" : String.Empty;
				File.WriteAllText(Manifest, "<save><version major=\"4\" minor=\"" + (legacy ? "6" : "8") + "\"/><region id=\"ModuleSettings\"><node id=\"root\"><children><node id=\"Mods\"><children>" +
					Module(Campaign, "GustavX") + Module(First, "First") + Module(Second, "Second") + "</children></node>" + order + "<node id=\"UnrelatedSettings\"/></children></node></region></save>");
			}
			/// <summary>Supplies complete empty package evidence so these tests isolate native existing-record edits.</summary>
			internal LoadOrderInventory Inventory() => new LoadOrderInventory(_root, new LoadOrderPackageObservation[0], true, new LoadOrderDiagnostic[0]);
			/// <summary>Produces native module metadata with an unknown field that must survive edits.</summary>
			private static string Module(string id, string folder) => "<node id=\"ModuleShortDesc\"><attribute id=\"UUID\" value=\"" + id + "\"/><attribute id=\"Folder\" value=\"" + folder + "\"/><attribute id=\"Version64\" value=\"1\"/><attribute id=\"ForeignField\" value=\"keep\"/></node>";
			/// <summary>Produces one legacy order node.</summary>
			private static string Ordered(string id) => "<node id=\"Module\"><attribute id=\"UUID\" value=\"" + id + "\"/></node>";
			/// <summary>Removes only this fixture's temporary directory.</summary>
			public void Dispose() { Directory.Delete(_root, true); }
		}
	}
}
