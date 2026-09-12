using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Nexus.Client.Mods;
using Nexus.Transactions;

namespace Nexus.Client.ModManagement
{
	public partial class ProfileManager
	{
		private const string DEPLOYMENT_FILE = "deployment.xml";
		private static readonly Version DEPLOYMENT_VERSION = new Version("1.0.0.0");

		/// <summary>
		/// Loads method-neutral deployment state for a profile, or synthesizes Virtual-only state for a legacy profile.
		/// </summary>
		public ProfileDeploymentManifest LoadDeploymentManifest(IModProfile p_impProfile, IList<IVirtualModLink> p_lstLegacyLinks)
		{
			if (p_impProfile == null)
				throw new ArgumentNullException(nameof(p_impProfile));

			string path = GetProfileDeploymentPath(p_impProfile);
			if (!File.Exists(path))
				return CreateLegacyDeploymentManifest(p_impProfile, p_lstLegacyLinks);

			return ReadDeploymentManifest(path);
		}

		/// <summary>
		/// Builds the mod-level install/uninstall work needed to make the target profile active.
		/// </summary>
		public ProfileDeploymentPlan CreateDeploymentPlan(IModProfile p_impProfile, ProfileDeploymentManifest p_pdmManifest, IEnumerable<string> p_enmForcedReinstallFileNames)
		{
			if (p_impProfile == null)
				throw new ArgumentNullException(nameof(p_impProfile));
			if (p_pdmManifest == null)
				throw new ArgumentNullException(nameof(p_pdmManifest));

			var plan = new ProfileDeploymentPlan();
			var forcedReinstall = new HashSet<string>(p_enmForcedReinstallFileNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			var matchedProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (IMod activeMod in ModManager.InstallationLog.ActiveMods.ToList())
			{
				ProfileDeploymentMod desired = FindProfileMod(p_pdmManifest.Mods, activeMod);
				if (desired == null)
				{
					plan.ModsToDeactivate.Add(activeMod);
					continue;
				}

				matchedProfileIds.Add(desired.ProfileModId);
				ModInstallMethod currentMethod = ModManager.InstallationLog.GetModInstallMethod(activeMod);
				ModInstallRoot currentRoot = ModManager.InstallationLog.GetModInstallRoot(activeMod);
				bool forceReinstall = forcedReinstall.Contains(Path.GetFileName(activeMod.Filename));
				if (!forceReinstall && currentMethod == desired.Method && NormalizeInstallRoot(currentRoot) == NormalizeInstallRoot(desired.InstallRoot))
					continue;

				plan.ModsToDeactivate.Add(activeMod);
				IMod installMod = ResolveManagedProfileMod(desired, activeMod);
				ValidateProfileRestoreSource(p_impProfile, desired, installMod);
				plan.ModsToInstall.Add(new ProfileDeploymentInstallRequest(installMod,
					new ModInstallContext(desired.Method, NormalizeInstallRoot(desired.InstallRoot))));
			}

			foreach (ProfileDeploymentMod desired in p_pdmManifest.Mods)
			{
				if (matchedProfileIds.Contains(desired.ProfileModId))
					continue;

				IMod installMod = ResolveManagedProfileMod(desired, null);
				ValidateProfileRestoreSource(p_impProfile, desired, installMod);
				plan.ModsToInstall.Add(new ProfileDeploymentInstallRequest(installMod,
					new ModInstallContext(desired.Method, NormalizeInstallRoot(desired.InstallRoot))));
			}

			return plan;
		}

		/// <summary>
		/// Persists the current method-neutral profile state after a deployment-level change.
		/// </summary>
		public void UpdateCurrentDeploymentManifest()
		{
			if (CurrentProfile != null && !VirtualModActivator.DisableLinkCreation)
				SaveDeploymentManifest(CurrentProfile);
		}

		/// <summary>
		/// Restores the persisted owner order for promoted targets after profile links and mods are in place.
		/// </summary>
		public void RestoreDeploymentOwnership(ProfileDeploymentManifest p_pdmManifest)
		{
			if (p_pdmManifest == null)
				throw new ArgumentNullException(nameof(p_pdmManifest));
			if (p_pdmManifest.IsLegacy || p_pdmManifest.Targets.Count == 0)
				return;

			var activeByProfileId = new Dictionary<string, IMod>(StringComparer.OrdinalIgnoreCase);
			foreach (ProfileDeploymentMod profileMod in p_pdmManifest.Mods)
			{
				IMod activeMod = FindProfileMod(ModManager.InstallationLog.ActiveMods, profileMod);
				if (activeMod == null)
					throw new InvalidOperationException(String.Format("Profile deployment mod '{0}' is not installed.", profileMod.FileName));

				activeByProfileId.Add(profileMod.ProfileModId, activeMod);
			}

			using (var transaction = new TransactionScope())
			{
				foreach (ProfileDeploymentTargetState targetState in p_pdmManifest.Targets)
				{
					var desiredOwnerKeys = new List<string>(targetState.Owners.Count);
					foreach (ProfileDeploymentOwner owner in targetState.Owners)
					{
						if (owner.IsOriginal)
						{
							desiredOwnerKeys.Add(ModManager.InstallationLog.OriginalValuesKey);
							continue;
						}

						IMod ownerMod;
						if (String.IsNullOrWhiteSpace(owner.ProfileModId) || !activeByProfileId.TryGetValue(owner.ProfileModId, out ownerMod))
							throw new InvalidDataException("Profile deployment ownership refers to an unknown mod identity.");

						string ownerKey = ModManager.InstallationLog.GetModKey(ownerMod);
						if (String.IsNullOrWhiteSpace(ownerKey))
							throw new InvalidOperationException(String.Format("Profile deployment mod '{0}' has no active InstallLog key.", ownerMod.ModName));
						desiredOwnerKeys.Add(ownerKey);
					}

					ModManager.DeploymentManager.RestorePromotedOwnerStack(targetState.Target, desiredOwnerKeys);
				}

				transaction.Complete();
			}
		}

		/// <summary>
		/// Writes a snapshot of the current InstallLog and promoted ownership state into the profile.
		/// </summary>
		private void SaveDeploymentManifest(IModProfile p_impProfile)
		{
			SaveDeploymentManifest(p_impProfile, m_strProfileManagerPath);
		}

		/// <summary>
		/// Writes a method-neutral deployment snapshot to an alternate profile root, such as a self-contained backup staging folder.
		/// </summary>
		public void SaveDeploymentManifest(IModProfile p_impProfile, string p_strProfileManagerPath)
		{
			if (p_impProfile == null || ModManager == null || ModManager.InstallationLog == null)
				return;
			if (String.IsNullOrWhiteSpace(p_strProfileManagerPath))
				throw new ArgumentException("A profile manager path is required.", nameof(p_strProfileManagerPath));

			ProfileDeploymentManifest manifest = CaptureCurrentDeploymentManifest(p_impProfile);
			XDocument document = WriteDeploymentManifest(manifest);
			string path = Path.Combine(p_strProfileManagerPath, p_impProfile.Id, DEPLOYMENT_FILE);
			string directory = Path.GetDirectoryName(path);
			if (!Directory.Exists(directory))
				Directory.CreateDirectory(directory);

			WriteProfileFiles(path, Encoding.UTF8.GetBytes(document.ToString()));
		}

		/// <summary>
		/// Gets the method-neutral deployment manifest path for the specified profile.
		/// </summary>
		private string GetProfileDeploymentPath(IModProfile p_impProfile)
		{
			return Path.Combine(m_strProfileManagerPath, p_impProfile.Id, DEPLOYMENT_FILE);
		}

		private ProfileDeploymentManifest CaptureCurrentDeploymentManifest(IModProfile p_impProfile)
		{
			ProfileDeploymentManifest previous = null;
			string path = GetProfileDeploymentPath(p_impProfile);
			if (File.Exists(path))
				previous = ReadDeploymentManifest(path);

			var manifest = new ProfileDeploymentManifest();
			var runtimeToProfileId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (IMod mod in ModManager.InstallationLog.ActiveMods)
			{
				ProfileDeploymentMod oldEntry = previous == null ? null : FindProfileMod(previous.Mods, mod);
				var entry = CreateProfileMod(mod, oldEntry == null ? Guid.NewGuid().ToString("N") : oldEntry.ProfileModId);
				manifest.Mods.Add(entry);

				string runtimeKey = ModManager.InstallationLog.GetModKey(mod);
				if (!String.IsNullOrWhiteSpace(runtimeKey))
					runtimeToProfileId[runtimeKey] = entry.ProfileModId;
			}

			if (ModManager.DeploymentManager != null && ModManager.DeploymentManager.HasPromotedTargets)
			{
				foreach (ModDeploymentTarget target in ModManager.DeploymentManager.GetPromotedTargets()
					.OrderBy(x => x.Root).ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase))
				{
					var targetState = new ProfileDeploymentTargetState { Target = target };
					foreach (string ownerKey in ModManager.DeploymentManager.GetOwnerKeys(target))
					{
						if (ownerKey.Equals(ModManager.InstallationLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
						{
							targetState.Owners.Add(new ProfileDeploymentOwner { IsOriginal = true });
							continue;
						}

						string profileModId;
						if (!runtimeToProfileId.TryGetValue(ownerKey, out profileModId))
							throw new InvalidOperationException(String.Format("Promoted target '{0}' refers to an InstallLog owner not present in the active profile.", target));

						targetState.Owners.Add(new ProfileDeploymentOwner { ProfileModId = profileModId });
					}
					manifest.Targets.Add(targetState);
				}
			}

			return manifest;
		}

		private ProfileDeploymentManifest CreateLegacyDeploymentManifest(IModProfile p_impProfile, IList<IVirtualModLink> p_lstLegacyLinks)
		{
			if (p_impProfile.ModList == null)
				LoadProfileFileList(p_impProfile);

			var manifest = new ProfileDeploymentManifest { IsLegacy = true };
			if (p_impProfile.ModList == null)
				return manifest;

			foreach (IVirtualModInfo modInfo in p_impProfile.ModList.Where(x => x != null && !String.IsNullOrWhiteSpace(x.ModFileName)))
			{
				if (manifest.Mods.Any(x => x.FileName.Equals(modInfo.ModFileName, StringComparison.OrdinalIgnoreCase)))
					continue;

				IVirtualModLink link = p_lstLegacyLinks == null ? null : p_lstLegacyLinks.FirstOrDefault(x =>
					x != null && x.ModInfo != null && x.ModInfo.ModFileName.Equals(modInfo.ModFileName, StringComparison.OrdinalIgnoreCase));
				manifest.Mods.Add(new ProfileDeploymentMod
				{
					ProfileModId = "legacy-" + Guid.NewGuid().ToString("N"),
					FileName = modInfo.ModFileName,
					ModId = modInfo.ModId,
					DownloadId = modInfo.DownloadId,
					Version = modInfo.FileVersion,
					Method = ModInstallMethod.Virtual,
					InstallRoot = link == null ? ModInstallRoot.Data : NormalizeInstallRoot(link.InstallRoot)
				});
			}

			return manifest;
		}

		private ProfileDeploymentMod CreateProfileMod(IMod p_modMod, string p_strProfileModId)
		{
			return new ProfileDeploymentMod
			{
				ProfileModId = p_strProfileModId,
				FileName = Path.GetFileName(p_modMod.Filename),
				ModId = p_modMod.Id,
				DownloadId = p_modMod.DownloadId,
				Version = p_modMod.HumanReadableVersion,
				Method = ModManager.InstallationLog.GetModInstallMethod(p_modMod),
				InstallRoot = NormalizeInstallRoot(ModManager.InstallationLog.GetModInstallRoot(p_modMod))
			};
		}

		private IMod ResolveManagedProfileMod(ProfileDeploymentMod p_pdmMod, IMod p_modFallback)
		{
			IMod match = FindProfileMod(ModManager.ManagedMods, p_pdmMod);
			if (match != null && File.Exists(match.Filename))
				return match;
			if (p_modFallback != null && MatchesProfileMod(p_pdmMod, p_modFallback) && File.Exists(p_modFallback.Filename))
				return p_modFallback;

			throw new FileNotFoundException(String.Format("The archive required to restore profile mod '{0}' is not available.", p_pdmMod.FileName), p_pdmMod.FileName);
		}

		private void ValidateProfileRestoreSource(IModProfile p_impProfile, ProfileDeploymentMod p_pdmMod, IMod p_modMod)
		{
			if (p_pdmMod.Method != ModInstallMethod.Direct || !p_modMod.HasInstallScript)
				return;

			if (String.IsNullOrWhiteSpace(IsScriptedLogPresent(p_modMod.Filename, p_impProfile)))
				throw new InvalidDataException(String.Format("Direct profile restore for scripted mod '{0}' requires its saved scripted-selection replay data.", p_pdmMod.FileName));
		}

		private static ProfileDeploymentMod FindProfileMod(IEnumerable<ProfileDeploymentMod> p_enmEntries, IMod p_modMod)
		{
			if (p_modMod == null || p_enmEntries == null)
				return null;

			string fileName = Path.GetFileName(p_modMod.Filename);
			ProfileDeploymentMod exact = p_enmEntries.FirstOrDefault(x =>
				!String.IsNullOrWhiteSpace(x.FileName) &&
				x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
				VersionMatches(x, p_modMod));
			return exact ?? p_enmEntries.FirstOrDefault(x => MatchesProfileMod(x, p_modMod));
		}

		private static IMod FindProfileMod(IEnumerable<IMod> p_enmMods, ProfileDeploymentMod p_pdmEntry)
		{
			if (p_enmMods == null || p_pdmEntry == null)
				return null;

			IMod exact = p_enmMods.FirstOrDefault(x => x != null &&
				Path.GetFileName(x.Filename).Equals(p_pdmEntry.FileName, StringComparison.OrdinalIgnoreCase) &&
				VersionMatches(p_pdmEntry, x));
			return exact ?? p_enmMods.FirstOrDefault(x => MatchesProfileMod(p_pdmEntry, x));
		}

		private static bool MatchesProfileMod(ProfileDeploymentMod p_pdmEntry, IMod p_modMod)
		{
			if (p_pdmEntry == null || p_modMod == null)
				return false;

			if (!VersionMatches(p_pdmEntry, p_modMod))
				return false;

			if (!String.IsNullOrWhiteSpace(p_pdmEntry.DownloadId) && !String.IsNullOrWhiteSpace(p_modMod.DownloadId) &&
				p_pdmEntry.DownloadId.Equals(p_modMod.DownloadId, StringComparison.OrdinalIgnoreCase))
				return true;

			return !String.IsNullOrWhiteSpace(p_pdmEntry.ModId) && !String.IsNullOrWhiteSpace(p_modMod.Id) &&
				p_pdmEntry.ModId.Equals(p_modMod.Id, StringComparison.OrdinalIgnoreCase);
		}

		private static bool VersionMatches(ProfileDeploymentMod p_pdmEntry, IMod p_modMod)
		{
			if (p_pdmEntry == null || p_modMod == null)
				return false;
			if (String.IsNullOrWhiteSpace(p_pdmEntry.Version))
				return true;

			return String.Equals(p_pdmEntry.Version, p_modMod.HumanReadableVersion ?? String.Empty, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Removes deleted mods from an inactive profile's method-neutral deployment manifest.
		/// </summary>
		private void PurgeDeploymentModsFromProfile(IModProfile p_impProfile, IEnumerable<IMod> p_enmMods)
		{
			if (p_impProfile == null || p_enmMods == null)
				return;

			string path = GetProfileDeploymentPath(p_impProfile);
			if (!File.Exists(path))
				return;

			ProfileDeploymentManifest manifest = ReadDeploymentManifest(path);
			var removedProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (IMod mod in p_enmMods.Where(x => x != null))
			{
				ProfileDeploymentMod entry = FindProfileMod(manifest.Mods, mod);
				if (entry != null)
					removedProfileIds.Add(entry.ProfileModId);
			}

			if (removedProfileIds.Count == 0)
				return;

			manifest.Mods.RemoveAll(x => removedProfileIds.Contains(x.ProfileModId));
			foreach (ProfileDeploymentTargetState target in manifest.Targets)
				target.Owners.RemoveAll(x => !x.IsOriginal && removedProfileIds.Contains(x.ProfileModId));
			manifest.Targets.RemoveAll(x => !x.Owners.Any(owner => !owner.IsOriginal));

			WriteProfileFiles(path, Encoding.UTF8.GetBytes(WriteDeploymentManifest(manifest).ToString()));
		}

		private static ModInstallRoot NormalizeInstallRoot(ModInstallRoot p_mirRoot)
		{
			return p_mirRoot == ModInstallRoot.GameRoot ? ModInstallRoot.GameRoot : ModInstallRoot.Data;
		}

		private static XDocument WriteDeploymentManifest(ProfileDeploymentManifest p_pdmManifest)
		{
			var root = new XElement("deployment", new XAttribute("fileVersion", DEPLOYMENT_VERSION));
			var mods = new XElement("mods");
			foreach (ProfileDeploymentMod mod in p_pdmManifest.Mods)
			{
				mods.Add(new XElement("mod",
					new XAttribute("profileId", mod.ProfileModId),
					new XAttribute("fileName", mod.FileName ?? String.Empty),
					new XAttribute("modId", mod.ModId ?? String.Empty),
					new XAttribute("downloadId", mod.DownloadId ?? String.Empty),
					new XAttribute("version", mod.Version ?? String.Empty),
					new XAttribute("installMethod", mod.Method),
					new XAttribute("installRoot", NormalizeInstallRoot(mod.InstallRoot))));
			}
			root.Add(mods);

			if (p_pdmManifest.Targets.Count > 0)
			{
				var targets = new XElement("targets");
				foreach (ProfileDeploymentTargetState target in p_pdmManifest.Targets)
				{
					var file = new XElement("file", new XAttribute("root", target.Target.Root), new XAttribute("path", target.Target.RelativePath));
					var owners = new XElement("owners");
					foreach (ProfileDeploymentOwner owner in target.Owners)
						owners.Add(owner.IsOriginal ? new XElement("original") : new XElement("mod", new XAttribute("profileId", owner.ProfileModId)));
					file.Add(owners);
					targets.Add(file);
				}
				root.Add(targets);
			}

			return new XDocument(root);
		}

		private static ProfileDeploymentManifest ReadDeploymentManifest(string p_strPath)
		{
			XDocument document = XDocument.Load(p_strPath);
			XElement root = document.Element("deployment");
			if (root == null)
				throw new InvalidDataException("Invalid profile deployment manifest: missing deployment root.");

			XAttribute versionAttribute = root.Attribute("fileVersion");
			Version version;
			if (versionAttribute == null || !Version.TryParse(versionAttribute.Value, out version) || version != DEPLOYMENT_VERSION)
				throw new InvalidDataException("Unsupported profile deployment manifest version.");

			var manifest = new ProfileDeploymentManifest();
			var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			XElement mods = root.Element("mods");
			if (mods != null)
			{
				foreach (XElement element in mods.Elements("mod"))
				{
					string profileId = RequiredAttribute(element, "profileId");
					if (!profileIds.Add(profileId))
						throw new InvalidDataException("Profile deployment manifest contains duplicate mod identities.");

					ModInstallMethod method;
					ModInstallRoot installRoot;
					if (!Enum.TryParse(RequiredAttribute(element, "installMethod"), true, out method) || !Enum.IsDefined(typeof(ModInstallMethod), method))
						throw new InvalidDataException("Profile deployment manifest contains an invalid install method.");
					if (!Enum.TryParse(RequiredAttribute(element, "installRoot"), true, out installRoot) || !Enum.IsDefined(typeof(ModInstallRoot), installRoot))
						throw new InvalidDataException("Profile deployment manifest contains an invalid install root.");

					manifest.Mods.Add(new ProfileDeploymentMod
					{
						ProfileModId = profileId,
						FileName = RequiredAttribute(element, "fileName"),
						ModId = OptionalAttribute(element, "modId"),
						DownloadId = OptionalAttribute(element, "downloadId"),
						Version = OptionalAttribute(element, "version"),
						Method = method,
						InstallRoot = NormalizeInstallRoot(installRoot)
					});
				}
			}

			XElement targets = root.Element("targets");
			if (targets != null)
			{
				var deploymentTargets = new HashSet<ModDeploymentTarget>();
				foreach (XElement element in targets.Elements("file"))
				{
					ModDeploymentRoot deploymentRoot;
					if (!Enum.TryParse(RequiredAttribute(element, "root"), true, out deploymentRoot) || !Enum.IsDefined(typeof(ModDeploymentRoot), deploymentRoot))
						throw new InvalidDataException("Profile deployment manifest contains an invalid target root.");

					var state = new ProfileDeploymentTargetState
					{
						Target = ModDeploymentTargetResolver.FromCanonical(deploymentRoot, RequiredAttribute(element, "path"))
					};
					if (!deploymentTargets.Add(state.Target))
						throw new InvalidDataException("Profile deployment manifest contains duplicate deployment targets.");
					XElement owners = element.Element("owners");
					if (owners == null)
						throw new InvalidDataException("Profile deployment target is missing its owner stack.");

					var targetOwnerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					bool originalSeen = false;
					int managedOwnerCount = 0;
					foreach (XElement owner in owners.Elements())
					{
						if (owner.Name.LocalName == "original")
						{
							if (originalSeen || state.Owners.Count != 0)
								throw new InvalidDataException("The original profile deployment owner must appear once and only as the bottom fallback.");
							originalSeen = true;
							state.Owners.Add(new ProfileDeploymentOwner { IsOriginal = true });
						}
						else if (owner.Name.LocalName == "mod")
						{
							string profileId = RequiredAttribute(owner, "profileId");
							if (!profileIds.Contains(profileId))
								throw new InvalidDataException("Profile deployment target refers to an unknown mod identity.");
							if (!targetOwnerIds.Add(profileId))
								throw new InvalidDataException("Profile deployment target contains a duplicate mod owner.");
							state.Owners.Add(new ProfileDeploymentOwner { ProfileModId = profileId });
							managedOwnerCount++;
						}
						else
							throw new InvalidDataException("Profile deployment target contains an unknown owner type.");
					}

					if (state.Owners.Count == 0)
						throw new InvalidDataException("Profile deployment target contains an empty owner stack.");
					if (managedOwnerCount == 0)
						throw new InvalidDataException("Profile deployment target contains no managed mod owners.");
					manifest.Targets.Add(state);
				}
			}

			return manifest;
		}

		private static string RequiredAttribute(XElement p_xelElement, string p_strName)
		{
			XAttribute attribute = p_xelElement.Attribute(p_strName);
			if (attribute == null || String.IsNullOrWhiteSpace(attribute.Value))
				throw new InvalidDataException(String.Format("Profile deployment manifest is missing required attribute '{0}'.", p_strName));
			return attribute.Value;
		}

		private static string OptionalAttribute(XElement p_xelElement, string p_strName)
		{
			XAttribute attribute = p_xelElement.Attribute(p_strName);
			return attribute == null ? String.Empty : attribute.Value;
		}
	}
}
