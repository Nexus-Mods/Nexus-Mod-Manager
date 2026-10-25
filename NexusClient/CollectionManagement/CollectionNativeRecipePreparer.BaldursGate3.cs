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
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.CollectionManagement
{
	public sealed partial class CollectionNativeRecipePreparer
	{
		private CollectionPlanIdentity _baldursGate3PackagePlan;
		private HashSet<string> _baldursGate3PackageFiles;

		/// <summary>Uses the selected archives' package inventory, including selected members already installed.</summary>
		internal bool ConfigureBaldursGate3PackageFiles(ResolvedCollectionPlan plan, IEnumerable<string> files, CancellationToken cancellationToken)
		{
			JArray order = GetRetainedPreparationManifest(plan, cancellationToken).BaldursGate3Order;
			if (order == null || order.Count == 0) return false;
			_baldursGate3PackageFiles = new HashSet<string>(files.Where(path => String.Equals(Path.GetExtension(path), ".pak", StringComparison.OrdinalIgnoreCase))
				.Select(path => Path.GetFileName(path.Replace('/', '\\'))), StringComparer.OrdinalIgnoreCase);
			_baldursGate3PackagePlan = plan.Identity;
			return true;
		}

		/// <summary>Retains curator module ordering as one native-owned value instead of per-archive metadata appends.</summary>
		private ModInstallationRecipeInput ApplyBaldursGate3Settings(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput translated, IGameMode gameMode, CancellationToken cancellationToken)
		{
			if (!String.Equals(gameMode.ModeId, "BaldursGate3", StringComparison.OrdinalIgnoreCase)) return translated;
			JArray order = GetRetainedPreparationManifest(plan, cancellationToken).BaldursGate3Order;
			if (order == null || order.Count == 0) return translated;
			StructuredXmlRecordRule rule = (gameMode as IStructuredXmlRecordProvider)?.StructuredRecords;
			if (rule == null) throw new NotSupportedException("BG3 Collection load order requires the configured native module-settings provider.");
			List<ScriptedInstallOperation> operations = translated.NativeOperations.Where(operation =>
				!(operation is EditGameSpecificValueOperation) ||
				!((EditGameSpecificValueOperation)operation).Key.StartsWith(rule.KeyPrefix, StringComparison.Ordinal)).ToList();
			if (member.MemberKey.Equals(plan.SelectedMembers.OrderBy(item => item.SourceOrdinal).First().MemberKey))
			{
				ISet<string> packages = plan.Identity.Equals(_baldursGate3PackagePlan) ? _baldursGate3PackageFiles : null;
				if (packages == null && plan.SelectedMembers.Count != plan.CapabilityReport.Manifest.Members.Count)
					throw new NotSupportedException("The selected BG3 package inventory is not available for this operation.");
				var records = new BaldursGate3CollectionRecordStore(rule);
				foreach (BasicInstallGameSpecificValue value in records.BuildPlan(order, packages))
					operations.Add(new EditGameSpecificValueOperation(value.Key, value.Value, true));
			}
			return translated.WithTransformedNativePlan(operations);
		}

		/// <summary>Applies the same secondary roots to retained C# installer file operations.</summary>
		private IReadOnlyList<ScriptedInstallOperation> MapBaldursGate3CapturedFiles(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			IReadOnlyList<ScriptedInstallOperation> captured, IMod mod, IGameMode gameMode, CancellationToken cancellationToken)
		{
			if (!String.Equals(gameMode.ModeId, "BaldursGate3", StringComparison.OrdinalIgnoreCase)) return captured;
			JObject root = GetRetainedPreparationManifest(plan, cancellationToken).Root;
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
	}
}
