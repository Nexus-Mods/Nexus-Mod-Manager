namespace Nexus.Client.ModManagement.InstallationLog
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Xml.Linq;

	/// <summary>
	/// Contains the sparse, method-neutral deployment ownership registry persisted by the install log.
	/// </summary>
	public partial class InstallLog
	{
		private Dictionary<ModDeploymentTarget, DeploymentEntry> _deploymentByTarget = new Dictionary<ModDeploymentTarget, DeploymentEntry>();
		private Dictionary<string, HashSet<ModDeploymentTarget>> _deploymentTargetsByModKey = new Dictionary<string, HashSet<ModDeploymentTarget>>(StringComparer.OrdinalIgnoreCase);

		private bool HasDeploymentTargetsCore => _deploymentByTarget.Count > 0;

		/// <summary>
		/// Stores the owner sequence for one promoted target from restoration fallback to physical winner.
		/// </summary>
		private sealed class DeploymentEntry
		{
			public DeploymentEntry(IEnumerable<string> ownerKeys)
			{
				OwnerKeys = NormalizeDeploymentOwnerKeys(ownerKeys);
			}

			public string[] OwnerKeys { get; }
		}

		private void LoadDeploymentRegistry(XDocument docLog)
		{
			XElement deploymentFiles = docLog.Descendants("deploymentFiles").FirstOrDefault();
			if (deploymentFiles == null)
				return;

			List<XElement> persistedFiles = deploymentFiles.Elements("file").ToList();
			_deploymentByTarget = new Dictionary<ModDeploymentTarget, DeploymentEntry>(persistedFiles.Count);
			_deploymentTargetsByModKey = new Dictionary<string, HashSet<ModDeploymentTarget>>(persistedFiles.Count, StringComparer.OrdinalIgnoreCase);

			foreach (XElement file in persistedFiles)
			{
				string path = file.Attribute("path")?.Value;
				if (string.IsNullOrWhiteSpace(path))
					throw new InvalidDataException("Install Log deployment target is missing its path.");

				ModDeploymentRoot root = ParseDeploymentRoot(file.Attribute("root")?.Value);
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(root, path);
				string[] ownerKeys = file.Descendants("installingMods").Elements("mod")
					.Select(x => x.Attribute("key")?.Value)
					.ToArray();

				if (ownerKeys.Length > 0)
					SetDeploymentOwnersCore(target, ownerKeys);
			}
		}

		private void AddSerializedDeploymentRegistry(XElement root)
		{
			if (_deploymentByTarget.Count == 0)
				return;

			var deploymentFiles = new XElement("deploymentFiles");
			foreach (KeyValuePair<ModDeploymentTarget, DeploymentEntry> item in _deploymentByTarget
				.OrderBy(x => x.Key.Root)
				.ThenBy(x => x.Key.RelativePath, StringComparer.OrdinalIgnoreCase))
			{
				deploymentFiles.Add(new XElement("file",
					new XAttribute("root", item.Key.Root),
					new XAttribute("path", item.Key.RelativePath),
					new XElement("installingMods",
						item.Value.OwnerKeys.Select(ownerKey => new XElement("mod", new XAttribute("key", ownerKey))))));
			}

			root.Add(deploymentFiles);
		}

		private static ModDeploymentRoot ParseDeploymentRoot(string value)
		{
			ModDeploymentRoot root;
			if (Enum.TryParse(value, true, out root) && Enum.IsDefined(typeof(ModDeploymentRoot), root))
				return root;

			throw new InvalidDataException(string.Format("Invalid deployment root '{0}' in Install Log.", value ?? string.Empty));
		}

		private static string[] NormalizeDeploymentOwnerKeys(IEnumerable<string> ownerKeys)
		{
			if (ownerKeys == null)
				throw new ArgumentNullException(nameof(ownerKeys));

			var owners = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string ownerKey in ownerKeys)
			{
				if (string.IsNullOrWhiteSpace(ownerKey))
					throw new InvalidDataException("A deployment owner key must not be empty.");
				if (!seen.Add(ownerKey))
					throw new InvalidDataException(string.Format("Deployment owner '{0}' appears more than once in the same target stack.", ownerKey));

				owners.Add(ownerKey);
			}

			return owners.ToArray();
		}

		private IReadOnlyList<string> GetDeploymentOwnerKeysCore(ModDeploymentTarget target)
		{
			DeploymentEntry entry;
			return target != null && _deploymentByTarget.TryGetValue(target, out entry)
				? (IReadOnlyList<string>)entry.OwnerKeys.ToArray()
				: new string[0];
		}

		private IReadOnlyCollection<ModDeploymentTarget> GetDeploymentTargetsForModCore(string modKey)
		{
			HashSet<ModDeploymentTarget> targets;
			return !string.IsNullOrWhiteSpace(modKey) && _deploymentTargetsByModKey.TryGetValue(modKey, out targets)
				? (IReadOnlyCollection<ModDeploymentTarget>)targets.ToArray()
				: new ModDeploymentTarget[0];
		}

		private bool IsDeploymentTargetPromotedCore(ModDeploymentTarget target)
		{
			return target != null && _deploymentByTarget.ContainsKey(target);
		}

		private void SetDeploymentOwnersCore(ModDeploymentTarget target, IEnumerable<string> ownerKeys)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));

			string[] owners = NormalizeDeploymentOwnerKeys(ownerKeys);
			RemoveDeploymentTargetFromInverseIndex(target);

			if (owners.Length == 0)
			{
				_deploymentByTarget.Remove(target);
				return;
			}

			_deploymentByTarget[target] = new DeploymentEntry(owners);
			foreach (string ownerKey in owners)
			{
				if (ownerKey.Equals(OriginalValuesKey, StringComparison.OrdinalIgnoreCase))
					continue;

				HashSet<ModDeploymentTarget> targets;
				if (!_deploymentTargetsByModKey.TryGetValue(ownerKey, out targets))
				{
					targets = new HashSet<ModDeploymentTarget>();
					_deploymentTargetsByModKey.Add(ownerKey, targets);
				}

				targets.Add(target);
			}
		}

		private void RemoveDeploymentTargetCore(ModDeploymentTarget target)
		{
			if (target == null)
				return;

			RemoveDeploymentTargetFromInverseIndex(target);
			_deploymentByTarget.Remove(target);
		}

		private void RemoveDeploymentTargetFromInverseIndex(ModDeploymentTarget target)
		{
			DeploymentEntry existing;
			if (!_deploymentByTarget.TryGetValue(target, out existing))
				return;

			foreach (string ownerKey in existing.OwnerKeys)
			{
				HashSet<ModDeploymentTarget> targets;
				if (!_deploymentTargetsByModKey.TryGetValue(ownerKey, out targets))
					continue;

				targets.Remove(target);
				if (targets.Count == 0)
					_deploymentTargetsByModKey.Remove(ownerKey);
			}
		}
	}
}
