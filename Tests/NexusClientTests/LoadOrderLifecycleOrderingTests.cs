using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Transactions;
using ChinhDo.Transactions;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.ModManagement.LoadOrder.UI;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Isolated lifecycle ordering regression source; never uses a helper, game profile or live ownership database.</summary>
	[TestFixture]
	public sealed class LoadOrderLifecycleOrderingTests
	{
		private const string First = "11111111-1111-1111-1111-111111111111";
		private const string Second = "22222222-2222-2222-2222-222222222222";
		private const string Third = "33333333-3333-3333-3333-333333333333";
		private const string Campaign = "44444444-4444-4444-4444-444444444444";

		/// <summary>Newly registered entries append; stale manual or curator policies cannot move surviving entries.</summary>
		[TestCase("Manual")]
		[TestCase("Curator")]
		public void InstallPreservesExistingOrder(string source)
		{
			using (var fixture = new Fixture(Third, First))
			{
				fixture.Log.Seed(fixture.PolicyKey, fixture.Log.Foreign, Policy(source, First, Third).Encode());
				fixture.Commit(() => { LarianOrderGameValueInstaller installer = fixture.Installer(); Assert.That(installer.EditGameSpecificValue(fixture.ModuleKey(Second), Record(Second)), Is.True); installer.FinalizeInstall(); });
				CollectionAssert.AreEqual(new[] { Third, First, Second }, fixture.Order());
			}
		}

		/// <summary>The last committed install keeps exact native images and reports whether surviving positions moved.</summary>
		[Test]
		public void InstallRetainsExactBeforeAfterAudit()
		{
			using (var fixture = new Fixture(Third, First))
			{
				fixture.CaptureAudit = true; byte[] before = File.ReadAllBytes(fixture.Manifest);
				fixture.Commit(() => { LarianOrderGameValueInstaller installer = fixture.Installer(); installer.EditGameSpecificValue(fixture.ModuleKey(Second), Record(Second)); installer.FinalizeInstall(); });
				Newtonsoft.Json.Linq.JObject audit = LoadOrderMutationAudit.Read(fixture.AuditDirectory, fixture.Manifest);
				CollectionAssert.AreEqual(before, Convert.FromBase64String((string)audit["BeforeLsxBase64"]));
				CollectionAssert.AreEqual(File.ReadAllBytes(fixture.Manifest), Convert.FromBase64String((string)audit["AfterLsxBase64"]));
				Assert.That((bool)audit["ExistingRelativeOrderChanged"], Is.False);
				Assert.That((bool)audit["OrderExplicitlySupplied"], Is.False);
			}
		}

		/// <summary>Rolled-back installs must not leave a last-change record claiming the mutation committed.</summary>
		[Test]
		public void RolledBackInstallDoesNotPublishAudit()
		{
			using (var fixture = new Fixture(First))
			{
				fixture.CaptureAudit = true; byte[] before = File.ReadAllBytes(fixture.Manifest);
				using (var transaction = new TransactionScope())
				{
					LarianOrderGameValueInstaller installer = fixture.Installer(); installer.EditGameSpecificValue(fixture.ModuleKey(Second), Record(Second)); installer.FinalizeInstall();
				}
				Assert.That(LoadOrderMutationAudit.Read(fixture.AuditDirectory, fixture.Manifest), Is.Null);
				CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.Manifest));
			}
		}

		/// <summary>A per-module operation may not remove, reorder or alter another module's metadata.</summary>
		[TestCase(true)]
		[TestCase(false)]
		public void ScopedChangeGuardRejectsUnrelatedMutation(bool reorder)
		{
			using (var fixture = new Fixture(Third, First, Second))
			{
				LarianLoadOrderDocument before = LarianLoadOrderDocument.Read(fixture.Manifest);
				if (reorder) fixture.Write(false, First, Third, Second);
				if (!reorder)
				{
					var xml = new System.Xml.XmlDocument(); xml.Load(fixture.Manifest);
					xml.SelectSingleNode("//node[@id='ModuleShortDesc'][attribute[@id='UUID']/@value='" + Third + "']").Attributes.Append(xml.CreateAttribute("unexpected")).Value = "change";
					xml.Save(fixture.Manifest);
				}
				Assert.Throws<InvalidOperationException>(() => LoadOrderMutationAudit.RequireScopedChange(before, LarianLoadOrderDocument.Read(fixture.Manifest), new[] { Second }));
			}
		}

		/// <summary>Removing one module does not reapply a historical order or another module's historical exclusion.</summary>
		[Test]
		public void UninstallChangesOnlyItsOwnEntry()
		{
			using (var fixture = new Fixture(Third, First, Second))
			{
				LarianOrderPreference policy = Policy("Manual", First, Third); policy.Excluded = new[] { Third };
				policy.Order = new[] { First };
				fixture.Log.Seed(fixture.PolicyKey, fixture.Log.Foreign, policy.Encode());
				fixture.Log.Seed(fixture.ModuleKey(Second), fixture.Log.Target, Record(Second));
				fixture.Commit(() => { LarianOrderGameValueInstaller installer = fixture.Installer(); installer.UnEditGameSpecificValue(fixture.ModuleKey(Second)); installer.FinalizeInstall(); });
				CollectionAssert.AreEqual(new[] { Third, First }, fixture.Order());
			}
		}

		/// <summary>A reinstalled module's explicit exclusion is honored without replaying positions for the other modules.</summary>
		[Test]
		public void InstallHonorsOnlyTheAffectedModulesSavedExclusion()
		{
			using (var fixture = new Fixture(Third, First))
			{
				LarianOrderPreference policy = Policy("Manual", First, Third); policy.Excluded = new[] { Second };
				fixture.Log.Seed(fixture.PolicyKey, fixture.Log.Foreign, policy.Encode());
				fixture.Commit(() => { LarianOrderGameValueInstaller installer = fixture.Installer(); Assert.That(installer.EditGameSpecificValue(fixture.ModuleKey(Second), Record(Second)), Is.True); installer.FinalizeInstall(); });
				CollectionAssert.AreEqual(new[] { Third, First }, fixture.Order());
			}
		}

		/// <summary>A same-UUID upgrade updates its metadata in place rather than replaying the saved policy's positions.</summary>
		[Test]
		public void UpgradeKeepsCurrentModulePosition()
		{
			using (var fixture = new Fixture(Third, Second, First))
			{
				fixture.Log.Seed(fixture.PolicyKey, fixture.Log.Foreign, Policy("Manual", First, Third, Second).Encode());
				fixture.Log.Seed(fixture.ModuleKey(Second), fixture.Log.Target, Record(Second));
				fixture.Commit(() => { LarianOrderGameValueInstaller installer = fixture.Installer(true); Assert.That(installer.EditGameSpecificValue(fixture.ModuleKey(Second), Record(Second, "2")), Is.True); installer.FinalizeInstall(); });
				CollectionAssert.AreEqual(new[] { Third, Second, First }, fixture.Order());
				Assert.That(LarianLoadOrderDocument.Field(LarianLoadOrderDocument.Read(fixture.Manifest).Records[Second], "Version64"), Is.EqualTo("2"));
			}
		}

		/// <summary>Scoped and older whole-aggregate removal preserve the sequence of surviving entries, including legacy ModOrder.</summary>
		[TestCase(false, false)]
		[TestCase(true, false)]
		[TestCase(false, true)]
		[TestCase(true, true)]
		public void CollectionRemovalPreservesCurrentSurvivorOrder(bool scoped, bool legacy)
		{
			using (var fixture = new Fixture(Third, First, Second))
			{
				fixture.Write(legacy, Third, First, Second);
				byte[] current = fixture.Aggregate();
				fixture.Write(legacy, Second, First, Third); byte[] previous = fixture.Aggregate();
				fixture.Write(legacy, Third, First, Second);
				fixture.Log.Seed(fixture.AggregateKey, fixture.Log.Target, current, previous);
				if (scoped) { LarianOrderPreference policy = Policy("Curator", First); policy.Managed = new[] { First }; fixture.Log.Seed(fixture.PolicyKey, fixture.Log.Target, policy.Encode()); }
				fixture.Commit(() =>
				{
					LarianOrderGameValueInstaller installer = fixture.Installer(); installer.UnEditGameSpecificValue(fixture.AggregateKey);
					if (scoped) installer.UnEditGameSpecificValue(fixture.PolicyKey);
					installer.FinalizeInstall();
				});
				CollectionAssert.AreEqual(new[] { Third, First, Second }, fixture.Order());
			}
		}

		/// <summary>Installing a newly supplied curator order remains an explicit ordering action and wins over current positions.</summary>
		[TestCase("Curator")]
		[TestCase("Manual")]
		public void ExplicitOrderRecipeStillAppliesItsOrder(string source)
		{
			using (var fixture = new Fixture(Third, First, Second))
			{
				LarianOrderPreference policy = Policy(source, Second, First); policy.Managed = new[] { First, Second };
				byte[] desired = Encoding.UTF8.GetBytes("<bg3-order><node id='Mods'><children>" + Encoding.UTF8.GetString(Record(Campaign)) + Encoding.UTF8.GetString(Record(Second)) + Encoding.UTF8.GetString(Record(First)) + "</children></node></bg3-order>");
				fixture.Commit(() =>
				{
					LarianOrderGameValueInstaller installer = fixture.Installer();
					Assert.That(installer.EditGameSpecificValue(fixture.PolicyKey, policy.Encode()), Is.True);
					Assert.That(installer.EditGameSpecificValue(fixture.AggregateKey, desired), Is.True); installer.FinalizeInstall();
				});
				CollectionAssert.AreEqual(new[] { Second, First, Third }, fixture.Order());
			}
		}

		/// <summary>The opt-in trigger excludes profiles, Collections, rollback, failure and non-durable reports.</summary>
		[TestCase(ModOperationOrigin.Manual, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, true)]
		[TestCase(ModOperationOrigin.Collection, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, false)]
		[TestCase(ModOperationOrigin.Profile, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, false)]
		[TestCase(ModOperationOrigin.LocalRestore, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, false)]
		[TestCase(ModOperationOrigin.Recovery, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, false)]
		[TestCase(ModOperationOrigin.Manual, ModOperationReportedStatus.Failed, ModOperationDurability.VerifiedRolledBack, false)]
		[TestCase(ModOperationOrigin.Manual, ModOperationReportedStatus.Succeeded, ModOperationDurability.Unknown, false)]
		public void AutomaticFixRequiresSuccessfulCommittedManualOperation(ModOperationOrigin origin, ModOperationReportedStatus status, ModOperationDurability durability, bool expected)
		{
			var identity = ModOperationIdentity.CreateNew(origin, new ModOperationFingerprint("fixture-target", new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "fixture-recipe"));
			var result = new ModOperationResult(identity, status, durability, null);
			Assert.That(LoadOrderControl.IsAutomaticFixTrigger(true, result), Is.EqualTo(expected));
			Assert.That(LoadOrderControl.IsAutomaticFixTrigger(false, result), Is.False);
			Assert.That(LoadOrderControl.IsAutomaticFixTrigger(true, null), Is.False);
		}

		/// <summary>Creates a valid explicit policy without synthesizing membership for unrelated modules.</summary>
		private static LarianOrderPreference Policy(string source, params string[] ids) => new LarianOrderPreference { Source = source, Order = ids, Excluded = new string[0] };
		/// <summary>Builds the established module payload with a mandatory protected campaign.</summary>
		private static byte[] Record(string id, string version = "1") => Encoding.UTF8.GetBytes("<node id='ModuleShortDesc'><attribute id='UUID' value='" + id + "'/><attribute id='Folder' value='" + (id == Campaign ? "GustavX" : "Module" + id[0]) + "'/><attribute id='Name' value='Fixture'/><attribute id='Version64' value='" + version + "'/></node>");
		/// <summary>Provides transaction-aware native reader seams without a disk InstallLog.</summary>
		private interface IOrderingLog : IInstallLog, ICurrentGameSpecificValueReader, IOwnedGameSpecificValueReader { }

		/// <summary>Minimal native owner history with correct out-argument handling for production preference readers.</summary>
		private sealed class LogProxy : RealProxy
		{
			private readonly Dictionary<string, List<Tuple<IMod, byte[]>>> _values = new Dictionary<string, List<Tuple<IMod, byte[]>>>(StringComparer.Ordinal);
			internal IMod Target { get; } = InterfaceStub<IMod>.Create((method, args) => null);
			internal IMod Foreign { get; } = InterfaceStub<IMod>.Create((method, args) => null);
			private readonly IMod _original = InterfaceStub<IMod>.Create((method, args) => null);
			internal IOrderingLog Log => (IOrderingLog)GetTransparentProxy();
			/// <summary>Restricts the fake to the existing native interface and optional value readers.</summary>
			internal LogProxy() : base(typeof(IOrderingLog)) { }
			/// <summary>Seeds exact original and installed values for one independently owned key.</summary>
			internal void Seed(string key, IMod owner, byte[] value, byte[] original = null) { _values[key] = new List<Tuple<IMod, byte[]>> { Tuple.Create(_original, original), Tuple.Create(owner, value) }; }
			/// <summary>Maps fixture identities without relying on archive filenames.</summary>
			private string Key(IMod mod) => ReferenceEquals(mod, Target) ? "target" : ReferenceEquals(mod, Foreign) ? "foreign" : "original";
			/// <summary>Dispatches only the native ownership operations exercised by this fixture.</summary>
			public override IMessage Invoke(IMessage message)
			{
				var call = (IMethodCallMessage)message; object[] args = call.Args; object result = null;
				try
				{
					string name = call.MethodName;
					string key = args.Length == 0 ? null : args[0] as string;
					List<Tuple<IMod, byte[]>> history = key != null && _values.ContainsKey(key) ? _values[key] : null;
					switch (name)
					{
						case "get_OriginalValuesKey": result = "original"; break;
						case "GetModKey": result = Key((IMod)args[0]); break;
						case "GetInstalledGameSpecificValueEdits": result = _values.Where(pair => pair.Value.Any(item => ReferenceEquals(item.Item1, args[0]))).Select(pair => pair.Key).ToList(); break;
						case "GetCurrentGameSpecificValueEditOwnerKey": result = history?.LastOrDefault() == null ? null : Key(history.Last().Item1); break;
						case "GetCurrentGameSpecificValueEditOwner": result = history?.LastOrDefault()?.Item1; break;
						case "GetPreviousGameSpecificValue": result = history != null && history.Count > 1 ? history[history.Count - 2].Item2 : null; break;
						case "GetGameSpecificValueEditInstallers": result = history?.Select(item => item.Item1).ToList() ?? new List<IMod>(); break;
						case "TryReadCurrentGameSpecificValue": args[1] = history?.LastOrDefault()?.Item2; result = history?.Count > 0; break;
						case "TryReadOwnedGameSpecificValue":
							Tuple<IMod, byte[]> owned = _values.ContainsKey((string)args[1]) ? _values[(string)args[1]].LastOrDefault(item => ReferenceEquals(item.Item1, args[0])) : null;
							args[2] = owned?.Item2; result = owned != null; break;
						case "LogOriginalGameSpecificValue": _values[key] = new List<Tuple<IMod, byte[]>> { Tuple.Create(_original, (byte[])args[1]) }; break;
						case "AddGameSpecificValueEdit": _values[(string)args[1]].Add(Tuple.Create((IMod)args[0], (byte[])args[2])); break;
						case "ReplaceGameSpecificValueEdit":
							List<Tuple<IMod, byte[]>> values = _values[(string)args[1]]; int index = values.FindLastIndex(item => ReferenceEquals(item.Item1, args[0])); values[index] = Tuple.Create((IMod)args[0], (byte[])args[2]); break;
						case "RemoveGameSpecificValueEdit": _values[(string)args[1]].RemoveAll(item => ReferenceEquals(item.Item1, args[0])); break;
						default: throw new NotSupportedException(name);
					}
					return new ReturnMessage(result, args, args.Length, call.LogicalCallContext, call);
				}
				catch (Exception error) { return new ReturnMessage(error, call); }
			}
		}

		/// <summary>Owns an isolated profile and native file transaction; never resolves LocalAppData.</summary>
		private sealed class Fixture : IDisposable
		{
			private readonly string _root = Path.Combine(Path.GetTempPath(), "nmm-order-lifecycle-" + Guid.NewGuid().ToString("N"));
			internal string Manifest { get; }
			internal string AuditDirectory => _root;
			internal bool CaptureAudit { get; set; }
			internal LogProxy Log { get; } = new LogProxy();
			internal StructuredXmlRecordRule Rule { get; }
			internal BaldursGate3CollectionRecordStore Store { get; }
			internal string PolicyKey => LarianOrderPreference.Key("Public");
			internal string AggregateKey => BaldursGate3CollectionRecordStore.Prefix + "Public|" + BaldursGate3CollectionRecordStore.Identity;
			/// <summary>Creates one valid modern profile with the requested current sequence.</summary>
			internal Fixture(params string[] ids)
			{
				Directory.CreateDirectory(Path.Combine(_root, "Public")); Manifest = Path.Combine(_root, "Public", "modsettings.lsx");
				Rule = new StructuredXmlRecordRule(1, StructuredXmlRecordRule.ProviderId, 1, "info.json", "Mods", "Utf8", @"Fixture\Profiles", "modsettings.lsx", new[] { "default" }, "fixture-modules-v1|", "Mods", "ModuleShortDesc", "UUID", new[] { new StructuredXmlRecordField("UUID", "guid", new[] { "UUID" }, null, null) });
				Store = new BaldursGate3CollectionRecordStore(Rule, _root); Write(false, ids);
			}
			/// <summary>Uses the production lifecycle installer against this explicit fixture store.</summary>
			internal LarianOrderGameValueInstaller Installer(bool upgrade = false) => new LarianOrderGameValueInstaller(Log.Target, Log.Log, new TxFileManager(), null, Rule, upgrade, Store, CaptureAudit ? AuditDirectory : null);
			/// <summary>Uses the same durable namespace as ordinary module registration.</summary>
			internal string ModuleKey(string id) => Rule.KeyPrefix + "Public|" + id;
			/// <summary>Completes the caller-owned native file transaction before assertions or cleanup.</summary>
			internal void Commit(Action action) { using (var transaction = new TransactionScope()) { action(); transaction.Complete(); } }
			/// <summary>Returns native positions from the actual fixture file.</summary>
			internal string[] Order() => LarianLoadOrderDocument.Read(Manifest).Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray();
			/// <summary>Captures native aggregate bytes through the real record store.</summary>
			internal byte[] Aggregate() { Store.TryReadValue(Manifest, BaldursGate3CollectionRecordStore.Identity, out byte[] value); return value; }
			/// <summary>Creates either supported order format with consistent game and module records.</summary>
			internal void Write(bool legacy, params string[] ids)
			{
				string modules = Encoding.UTF8.GetString(Record(Campaign)) + String.Concat(ids.Select(id => Encoding.UTF8.GetString(Record(id))));
				string order = legacy ? "<node id='ModOrder'><children>" + String.Concat(new[] { Campaign }.Concat(ids).Select(id => "<node id='Module'><attribute id='UUID' value='" + id + "'/></node>")) + "</children></node>" : String.Empty;
				File.WriteAllText(Manifest, "<save><version major='4' minor='" + (legacy ? "6" : "8") + "'/><region id='ModuleSettings'><node id='root'><children><node id='Mods'><children>" + modules + "</children></node>" + order + "</children></node></region></save>");
			}
			/// <summary>Deletes only fixed fixture files and empty directories.</summary>
			public void Dispose() { File.Delete(LoadOrderMutationAudit.FilePath(AuditDirectory, Manifest)); File.Delete(Manifest); Directory.Delete(Path.GetDirectoryName(Manifest), false); Directory.Delete(_root, false); }
		}
	}
}
