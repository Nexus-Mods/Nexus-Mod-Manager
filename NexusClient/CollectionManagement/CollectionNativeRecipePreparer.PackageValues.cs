using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	public sealed partial class CollectionNativeRecipePreparer
	{
		/// <summary>Regenerates package values and curator policies only while preparing a new exact effect preview.</summary>
		private ModInstallationRecipeInput ApplyFinalGameSettings(ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
			ModInstallationRecipeInput translated, IGameMode mode, IMod mod, CollectionVerifiedArchive archive, CancellationToken token)
		{
			CollectionGamePreparation game = GetRetainedPreparationManifest(plan, token).GameData;
			// Curator-supplied metadata is authoritative. Never infer additional inclusion from an archive in that case.
			if (!game.HasOwnedMetadata && (mode as IDeterministicPackageGameValuePlanProvider)?.RequiresPackageGameValuePlan(mod) == true)
			{
				translated = ApplySelectedPackageValues(translated, mode, mod, token);
				ValidateManagedModArchive(mod, archive, token);
			}
			return game.ApplySettings(plan, member, translated, mode);
		}

		/// <summary>Uses the final selected destinations after installer choices, file overrides and patches, never the whole archive.</summary>
		internal static ModInstallationRecipeInput ApplySelectedPackageValues(ModInstallationRecipeInput translated, IGameMode mode, IMod mod, CancellationToken token)
		{
			var provider = mode as IDeterministicPackageGameValuePlanProvider;
			if (provider?.RequiresPackageGameValuePlan(mod) != true) return translated;
			var files = new List<BasicInstallPlanFile>();
			foreach (InstallModFileOperation file in translated.NativeOperations.OfType<InstallModFileOperation>())
			{
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(mode, mod, file.DestinationPath, translated.InstallContext.InstallRoot);
				files.Add(new BasicInstallPlanFile(file.SourcePath, file.DestinationPath, target.RelativePath, null, target));
			}
			if (translated.NativeOperations.OfType<GenerateDataFileOperation>().Any(file => String.Equals(Path.GetExtension(file.DestinationPath), ".pak", StringComparison.OrdinalIgnoreCase) &&
				ModDeploymentTargetResolver.Resolve(mode, mod, file.DestinationPath, translated.InstallContext.InstallRoot).Root == ModDeploymentRoot.Data))
			{
				StructuredXmlRecordRule rule = (mode as IStructuredXmlRecordProvider)?.StructuredRecords;
				if (rule == null || !translated.NativeOperations.OfType<EditGameSpecificValueOperation>().Any(value => new StructuredXmlRecordStore(rule).IsSupportedKey(value.Key)))
					throw new NotSupportedException("Generated or patched packages require explicit module metadata before Collection registration.");
			}
			IReadOnlyList<BasicInstallGameSpecificValue> values = provider.GetPackageGameValues(mod, translated.InstallContext, files.AsReadOnly(), token);
			var operations = translated.NativeOperations.ToList();
			var keys = new HashSet<string>(operations.OfType<EditGameSpecificValueOperation>().Select(operation => operation.Key), StringComparer.Ordinal);
			foreach (BasicInstallGameSpecificValue value in values)
			{
				// An explicit installer effect already owns its reviewed metadata; inference must not replace that contract.
				if (!keys.Add(value.Key)) continue;
				operations.Add(new EditGameSpecificValueOperation(value.Key, value.Value, true));
			}
			return values.Count == 0 ? translated : translated.WithTransformedNativePlan(operations);
		}
	}
}
