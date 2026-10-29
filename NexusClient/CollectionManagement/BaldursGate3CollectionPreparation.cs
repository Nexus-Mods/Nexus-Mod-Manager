using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Interprets BG3 package inventory, module settings and secondary deployment paths.</summary>
	internal sealed class BaldursGate3CollectionPreparation : CollectionGamePreparation
	{
		private readonly JObject _root;
		private readonly JArray _order;
		private CollectionPlanIdentity _baldursGate3PackagePlan;
		private HashSet<string> _baldursGate3PackageFiles;

		/// <summary>Reads retained BG3 metadata once per retained manifest.</summary>
		internal BaldursGate3CollectionPreparation(JObject root)
		{
			_root = root;
			_order = root == null ? null : CollectionBaldursGate3Data.ReadLoadOrder(root);
		}

		internal override bool HasOwnedMetadata => _order != null && _order.Count > 0;

		/// <summary>Uses the selected archives' package inventory, including selected members already installed.</summary>
		internal override bool ConfigurePackageFiles(ResolvedCollectionPlan plan, IEnumerable<string> files)
		{
			JArray order = _order;
			if (order == null || order.Count == 0) return false;
			_baldursGate3PackageFiles = new HashSet<string>(files.Where(path => String.Equals(Path.GetExtension(path), ".pak", StringComparison.OrdinalIgnoreCase))
				.Select(path => Path.GetFileName(path.Replace('/', '\\'))), StringComparer.OrdinalIgnoreCase);
			_baldursGate3PackagePlan = plan.Identity;
			return true;
		}

		/// <summary>Retains curator module ordering as one native-owned value instead of per-archive metadata appends.</summary>
		internal override ModInstallationRecipeInput ApplySettings(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput translated, IGameMode gameMode)
		{
			JArray order = _order;
			if (order == null || order.Count == 0) return translated;
			StructuredXmlRecordRule rule = (gameMode as IStructuredXmlRecordProvider)?.StructuredRecords;
			if (rule == null) throw new NotSupportedException("BG3 Collection load order requires the configured native module-settings provider.");
			bool scoped = (gameMode as ILoadOrderCapabilityProvider)?.LoadOrder != null;
			ISet<string> packages = plan.Identity.Equals(_baldursGate3PackagePlan) ? _baldursGate3PackageFiles : null;
			IEnumerable<BasicInstallGameSpecificValue> values = new BasicInstallGameSpecificValue[0];
			if (member.MemberKey.Equals(plan.SelectedMembers.OrderBy(item => item.SourceOrdinal).First().MemberKey))
			{
				if (packages == null && plan.SelectedMembers.Count != plan.CapabilityReport.Manifest.Members.Count)
					throw new NotSupportedException("The selected BG3 package inventory is not available for this operation.");
				values = new BaldursGate3CollectionRecordStore(rule).BuildPlan(order, packages, scoped);
			}
			return translated.WithTransformedNativePlan(RegenerateOrderValues(translated.NativeOperations, rule, scoped, values, order, packages));
		}

		/// <summary>Creates a new reviewed recipe; retained operations and legacy recovery artifacts are never rewritten in place.</summary>
		internal static IReadOnlyList<ScriptedInstallOperation> RegenerateOrderValues(IEnumerable<ScriptedInstallOperation> retained, StructuredXmlRecordRule rule,
			bool scoped, IEnumerable<BasicInstallGameSpecificValue> values, JArray order, ISet<string> packages)
		{
			List<ScriptedInstallOperation> operations = retained.Where(operation => !(operation is EditGameSpecificValueOperation) ||
				!IsRebuiltValue(((EditGameSpecificValueOperation)operation).Key, rule, scoped)).ToList();
			var records = new BaldursGate3CollectionRecordStore(rule);
			foreach (BasicInstallGameSpecificValue value in values)
			{
				records.ParseKey(value.Key, out string profile, out string identity);
				if (scoped)
				{
					LarianOrderPreference preference = BuildScopedPreference(order, packages);
					operations.Add(new EditGameSpecificValueOperation(LarianOrderPreference.Key(profile), preference.Encode(), true));
				}
				operations.Add(new EditGameSpecificValueOperation(value.Key, value.Value, true));
			}
			return operations.AsReadOnly();
		}

		/// <summary>Replaces retained aggregate effects only from the verified curator source, before new review/fingerprinting.</summary>
		internal static bool IsRebuiltValue(string key, StructuredXmlRecordRule rule, bool scoped)
		{
			if (key.StartsWith(rule.KeyPrefix, StringComparison.Ordinal)) return true;
			if (!scoped) return false;
			if (LarianOrderPreference.TryProfile(key, out string profile)) return true;
			if (!key.StartsWith(BaldursGate3CollectionRecordStore.Prefix, StringComparison.Ordinal)) return false;
			new BaldursGate3CollectionRecordStore(rule).ParseKey(key, out profile, out string identity);
			return true;
		}

		/// <summary>Scopes enabled and explicitly disabled modules to selected archives, preserving independently installed optional modules.</summary>
		internal static LarianOrderPreference BuildScopedPreference(JArray order, ISet<string> packages)
		{
			JObject[] entries = order.OfType<JObject>().Where(entry => (bool?)entry["data"]?["isListed"] != true &&
				(packages == null || packages.Contains(Path.GetFileName(((string)entry["id"] ?? String.Empty).Replace('/', '\\'))))).ToArray();
			var preference = new LarianOrderPreference
			{
				Source = "Curator", Order = entries.Where(entry => (bool)entry["enabled"]).Select(entry => (string)entry["data"]["uuid"]).ToArray(),
				Excluded = entries.Where(entry => !(bool)entry["enabled"]).Select(entry => (string)entry["data"]["uuid"]).ToArray(),
				Managed = entries.Select(entry => (string)entry["data"]["uuid"]).ToArray()
			};
			preference.Encode(); return preference;
		}

		/// <summary>Applies the same secondary roots to retained C# installer file operations.</summary>
		internal override IReadOnlyList<ScriptedInstallOperation> MapCaptured(ResolvedCollectionMemberPlan member,
			IReadOnlyList<ScriptedInstallOperation> captured, IMod mod)
		{
			JObject root = _root;
			if (!CollectionBaldursGate3Data.IsManifest(root)) return captured;
			string type = (string)root["mods"]?[member.SourceOrdinal]?["details"]?["type"];
			bool extender = CollectionBaldursGate3Data.UsesExtenderInstaller(type, mod.GetFileList());
			var result = new List<ScriptedInstallOperation>();
			foreach (ScriptedInstallOperation operation in captured)
			{
				InstallModFileOperation file = operation as InstallModFileOperation;
				GenerateDataFileOperation generated = operation as GenerateDataFileOperation;
				if (file == null && generated == null) { result.Add(operation); continue; }
				string path = CollectionBaldursGate3Data.MapDestination(file == null ? generated.DestinationPath : file.DestinationPath, type, extender);
				if (path == null) continue;
				result.Add(file == null ? (ScriptedInstallOperation)new GenerateDataFileOperation(path, generated.Data, generated.PreparationSourcePath) : new InstallModFileOperation(file.SourcePath, path));
			}
			return result.AsReadOnly();
		}
		/// <inheritdoc />
		internal override IEnumerable<KeyValuePair<string, string>> GetBasicInstallFiles(ResolvedCollectionMemberPlan member, IMod mod)
		{
			string type = (string)_root["mods"]?[member.SourceOrdinal]?["details"]?["type"];
			return CollectionBaldursGate3Data.GetBasicInstallFiles(mod, type, member.ArtifactChoice.SelectedArtifact.StableId);
		}

		/// <inheritdoc />
		internal override ModInstallationSimpleFileRecipe MapFiles(ResolvedCollectionMemberPlan member, ModInstallationSimpleFileRecipe recipe, IMod mod) =>
			CollectionBaldursGate3Data.MapFiles(recipe, (string)_root["mods"]?[member.SourceOrdinal]?["details"]?["type"], mod.GetFileList());

		/// <inheritdoc />
		internal override string ComparablePath(string path)
		{
			int separator = path.IndexOf('\\');
			return separator > 0 && (path.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("bin\\", StringComparison.OrdinalIgnoreCase))
				? path.Substring(separator + 1) : path;
		}

	}
}
