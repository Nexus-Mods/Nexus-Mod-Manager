using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Mods;
using System.Transactions;

namespace Nexus.Client.ModManagement.InstallationLog
{
	public partial class InstallLog
	{
		/// <inheritdoc />
		public InstallLogReadSnapshot GetCommittedStateSnapshot()
		{
			if (Transaction.Current != null)
				throw new InvalidOperationException("Committed InstallLog state cannot be captured from inside an ambient transaction.");

			var mods = new List<InstallLogReadMod>();
			var visibleModsByKey = new Dictionary<string, IMod>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<IMod, string> registration in _activeModRegistry.Registrations)
			{
				bool hidden = _activeModRegistry.IsModHidden(registration.Key);
				mods.Add(new InstallLogReadMod(registration.Value, registration.Key.ModArchivePath, registration.Key.Filename,
					registration.Key.Id, registration.Key.DownloadId, registration.Key.HumanReadableVersion,
					registration.Key.MachineVersion == null ? String.Empty : registration.Key.MachineVersion.ToString(),
					registration.Key.HasInstallScript, GetModInstallRootByKey(registration.Value), GetModInstallMethodByKey(registration.Value), hidden));
				if (!hidden)
					visibleModsByKey[registration.Value] = registration.Key;
			}

			var files = new List<InstallLogReadFile>();
			foreach (InstalledItemDictionary<string, object>.ItemInstallers item in _installedFiles)
			{
				List<string> ownerKeys = item.Installers.Select(x => x.InstallerKey).ToList();
				IMod currentMod = null;
				string currentModKey = null;
				for (int index = ownerKeys.Count - 1; index >= 0 && currentMod == null; index--)
				{
					if (visibleModsByKey.TryGetValue(ownerKeys[index], out currentMod))
						currentModKey = ownerKeys[index];
				}

				ModInstallRoot installRoot = currentModKey == null ? ModInstallRoot.Data : GetModInstallRootByKey(currentModKey);
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(GameMode, currentMod, item.Item, installRoot);
				files.Add(new InstallLogReadFile(item.Item, target, ownerKeys));
			}

			var iniEdits = new List<InstallLogReadIniEdit>();
			foreach (InstalledItemDictionary<IniEdit, string>.ItemInstallers item in _installedIniEdits)
			{
				iniEdits.Add(new InstallLogReadIniEdit(item.Item.File, item.Item.Section, item.Item.Key,
					item.Installers.Select(x => new InstallLogReadStringValue(x.InstallerKey, x.Value))));
			}

			var gameValues = new List<InstallLogReadGameValue>();
			foreach (InstalledItemDictionary<string, byte[]>.ItemInstallers item in _gameSpecificValueEdits)
			{
				gameValues.Add(new InstallLogReadGameValue(item.Item,
					item.Installers.Select(x => new InstallLogReadBinaryValue(x.InstallerKey, x.Value))));
			}

			var deploymentTargets = new List<InstallLogReadDeploymentTarget>();
			foreach (KeyValuePair<ModDeploymentTarget, DeploymentEntry> item in _deploymentByTarget)
				deploymentTargets.Add(new InstallLogReadDeploymentTarget(item.Key, item.Value.OwnerKeys));

			return new InstallLogReadSnapshot(OriginalValuesKey, _deploymentCommitSequence,
				mods, files, iniEdits, gameValues, deploymentTargets);
		}
	}
}
