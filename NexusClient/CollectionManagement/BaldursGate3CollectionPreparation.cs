using System;
using System.Collections.Generic;
using System.Diagnostics;
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
		private JArray _preparedOrder;
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
			_preparedOrder = null;
			JArray order = _order;
			if (order == null || order.Count == 0) return false;
			_baldursGate3PackageFiles = new HashSet<string>(files.Where(path => String.Equals(Path.GetExtension(path), ".pak", StringComparison.OrdinalIgnoreCase))
				.Select(path => Path.GetFileName(path.Replace('/', '\\'))), StringComparer.OrdinalIgnoreCase);
			_baldursGate3PackagePlan = plan.Identity;
			return true;
		}

		/// <summary>Corrects stale listed flags only when an exact selected archive confirms a loadable add-on.</summary>
		internal override void ConfirmPackageMetadata(ResolvedCollectionPlan plan, Func<ResolvedCollectionMemberPlan, IMod> source, CancellationToken token)
		{
			JArray prepared = (JArray)_order.DeepClone();
			var inspector = new LarianPackageInspectorClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "LoadOrder", "LarianPackageInspector.exe"));
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				token.ThrowIfCancellationRequested();
				string fileId = _root["mods"]?[member.SourceOrdinal]?["source"]?["fileId"]?.ToString();
				JObject[] listed = prepared.OfType<JObject>().Where(entry => (bool?)entry["data"]?["isListed"] == true &&
					fileId != null && entry["fileId"]?.ToString() == fileId && _baldursGate3PackageFiles.Contains((string)entry["id"])).ToArray();
				if (listed.Length == 0) continue;
				if (!inspector.IsAvailable)
				{
					Trace.TraceWarning("Listed BG3 packages retain their collection exclusions because the package inspection helper is unavailable.");
					break;
				}
				IMod mod = source(member);
				if (mod == null) throw new InvalidDataException("The exact archive for a listed BG3 package is unavailable.");
				InspectListedPackages(listed, mod, inspector, token);
			}
			_preparedOrder = prepared;
		}

		/// <summary>Stages only selected sibling packages and removes only temporary files created by this inspection.</summary>
		private void InspectListedPackages(JObject[] listed, IMod mod, ILoadOrderPackageInspector inspector, CancellationToken token)
		{
			string[] paths = mod.GetFileList().Where(path => Path.GetExtension(path).Equals(".pak", StringComparison.OrdinalIgnoreCase) &&
				_baldursGate3PackageFiles.Contains(Path.GetFileName(path.Replace('/', '\\')))).Take(1025).ToArray();
			if (paths.Length > 1024 || paths.Select(path => Path.GetFileName(path.Replace('/', '\\'))).Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
				throw new InvalidDataException("The selected listed BG3 package set is oversized or ambiguous.");
			string staging = Path.Combine(Path.GetTempPath(), "NMM-CollectionListed-" + Guid.NewGuid().ToString("N"));
			var created = new List<string>();
			Directory.CreateDirectory(staging);
			try
			{
				long total = 0;
				foreach (string path in paths)
				{
					token.ThrowIfCancellationRequested();
					string name = Path.GetFileName(path.Replace('/', '\\'));
					LoadOrderInventoryValues.FileName(name);
					string destination = Path.Combine(staging, name);
					using (FileStream input = mod.GetFileStream(path))
					{
						if (input == null || input.Length < 1 || input.Length > 32L * 1024 * 1024 * 1024 || total > 64L * 1024 * 1024 * 1024 - input.Length)
							throw new InvalidDataException("Listed BG3 packages exceed the bounded inspection size.");
						long length = input.Length; input.Position = 0;
						using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
						{
							created.Add(destination); LarianPackageInstallPlanner.CopyPackage(input, output, length, token);
						}
						total += length;
					}
				}
				LoadOrderPackageFile[] files = created.Select(LoadOrderPackageDirectoryScanner.Observe).ToArray();
				foreach (JObject entry in listed)
				{
					token.ThrowIfCancellationRequested();
					LoadOrderPackageFile file = files.SingleOrDefault(item => item.FileName.Equals((string)entry["id"], StringComparison.OrdinalIgnoreCase));
					if (file == null) throw new InvalidDataException("A listed BG3 package is missing from its selected archive.");
					LoadOrderPackageInspection inspection = inspector.Inspect(file, files, token);
					if (inspection.Status == LoadOrderInspectionStatus.Failed || inspection.Status == LoadOrderInspectionStatus.Unavailable)
						throw new InvalidDataException("A listed BG3 package could not be inspected: " + file.FileName + ". " + inspection.TechnicalDetails);
					ConfirmListedModule(entry, inspection);
					if (inspection.Status != LoadOrderInspectionStatus.Complete)
						Trace.TraceWarning("Listed BG3 package {0} retains its collection exclusion after incomplete inspection: {1}", file.FileName, inspection.TechnicalDetails);
				}
			}
			finally
			{
				foreach (string path in created)
					try { File.Delete(path); }
					catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Trace.TraceWarning("Listed package inspection cleanup: {0}", error.Message); }
				try { Directory.Delete(staging, false); }
				catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Trace.TraceWarning("Listed package inspection staging retained at {0}: {1}", staging, error.Message); }
			}
		}

		/// <summary>Retains override, campaign and incomplete exclusions; confirmed metadata replaces only the prepared copy.</summary>
		internal static void ConfirmListedModule(JObject entry, LoadOrderPackageInspection inspection)
		{
			if ((bool?)entry["data"]?["isListed"] != true || inspection.Status != LoadOrderInspectionStatus.Complete) return;
			LoadOrderModuleInfo module = inspection.Modules.SingleOrDefault(item => item.Kind == LoadOrderModuleKind.AddOn &&
				String.Equals(item.Metadata.Identity, (string)entry["data"]?["uuid"], StringComparison.OrdinalIgnoreCase));
			if (module == null || new[] { "Gustav", "GustavDev", "GustavX", "Shared", "SharedDev" }.Contains(module.Metadata.Folder, StringComparer.OrdinalIgnoreCase)) return;
			JObject data = (JObject)entry["data"];
			data["folder"] = module.Metadata.Folder; data["name"] = module.Metadata.Name;
			data["version"] = module.Metadata.Version; data["md5"] = module.Metadata.Md5 ?? String.Empty;
			data["isListed"] = false;
		}

		/// <summary>Retains curator module ordering as one native-owned value instead of per-archive metadata appends.</summary>
		internal override ModInstallationRecipeInput ApplySettings(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput translated, IGameMode gameMode)
		{
			bool configured = plan.Identity.Equals(_baldursGate3PackagePlan);
			JArray order = configured ? _preparedOrder ?? _order : _order;
			if (order == null || order.Count == 0) return translated;
			StructuredXmlRecordRule rule = (gameMode as IStructuredXmlRecordProvider)?.StructuredRecords;
			if (rule == null) throw new NotSupportedException("BG3 Collection load order requires the configured native module-settings provider.");
			bool scoped = (gameMode as ILoadOrderCapabilityProvider)?.LoadOrder != null;
			ISet<string> packages = configured ? _baldursGate3PackageFiles : null;
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
