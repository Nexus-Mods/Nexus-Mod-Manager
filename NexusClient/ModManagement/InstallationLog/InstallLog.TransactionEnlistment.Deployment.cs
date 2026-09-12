namespace Nexus.Client.ModManagement.InstallationLog
{
	using System;
	using System.Collections.Generic;
	using System.Linq;

	using Nexus.Client.Mods;

	public partial class InstallLog
	{
		/// <summary>
		/// Tracks Step 1 install-method and sparse deployment-registry state inside the InstallLog transaction.
		/// </summary>
		private partial class TransactionEnlistment
		{
			private readonly Dictionary<string, ModInstallMethod> _modInstallMethods = new Dictionary<string, ModInstallMethod>(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<ModDeploymentTarget, string[]> _deploymentChanges = new Dictionary<ModDeploymentTarget, string[]>();
			private readonly HashSet<ModDeploymentTarget> _removedDeploymentTargets = new HashSet<ModDeploymentTarget>();

			private void SetModInstallMethod(string key, ModInstallMethod installMethod)
			{
				if (string.IsNullOrEmpty(key))
					return;

				_modInstallMethods[key] = NormalizeInstallMethod(installMethod);
			}

			public ModInstallMethod GetModInstallMethodByKey(string key)
			{
				ModInstallMethod installMethod;
				if (!string.IsNullOrEmpty(key) && _modInstallMethods.TryGetValue(key, out installMethod))
					return installMethod;

				return EnlistedInstallLog.GetModInstallMethodByKey(key);
			}

			public ModInstallMethod GetModInstallMethod(IMod mod)
			{
				return GetModInstallMethodByKey(GetModKey(mod));
			}

			public bool HasDeploymentTargets
			{
				get
				{
					if (_deploymentChanges.Count > 0)
						return true;
					if (_removedDeploymentTargets.Count == 0)
						return EnlistedInstallLog.HasDeploymentTargetsCore;

					return EnlistedInstallLog._deploymentByTarget.Keys.Any(x => !_removedDeploymentTargets.Contains(x));
				}
			}

			public IReadOnlyList<string> GetDeploymentOwnerKeys(ModDeploymentTarget target)
			{
				if (target == null)
					throw new ArgumentNullException(nameof(target));
				if (_removedDeploymentTargets.Contains(target))
					return new string[0];

				string[] owners;
				return _deploymentChanges.TryGetValue(target, out owners)
					? (IReadOnlyList<string>)owners.ToArray()
					: EnlistedInstallLog.GetDeploymentOwnerKeysCore(target);
			}

			public IReadOnlyCollection<ModDeploymentTarget> GetDeploymentTargetsForMod(string modKey)
			{
				if (string.IsNullOrWhiteSpace(modKey))
					return new ModDeploymentTarget[0];

				var targets = new HashSet<ModDeploymentTarget>(EnlistedInstallLog.GetDeploymentTargetsForModCore(modKey));
				foreach (ModDeploymentTarget target in _removedDeploymentTargets)
					targets.Remove(target);

				foreach (KeyValuePair<ModDeploymentTarget, string[]> change in _deploymentChanges)
				{
					if (change.Value.Any(x => x.Equals(modKey, StringComparison.OrdinalIgnoreCase)))
						targets.Add(change.Key);
					else
						targets.Remove(change.Key);
				}

				return targets.ToArray();
			}

			public bool IsDeploymentTargetPromoted(ModDeploymentTarget target)
			{
				if (target == null || _removedDeploymentTargets.Contains(target))
					return false;

				return _deploymentChanges.ContainsKey(target) || EnlistedInstallLog.IsDeploymentTargetPromotedCore(target);
			}

			public void SetDeploymentOwners(ModDeploymentTarget target, IEnumerable<string> ownerKeys)
			{
				if (target == null)
					throw new ArgumentNullException(nameof(target));

				string[] owners = NormalizeDeploymentOwnerKeys(ownerKeys);
				if (owners.Length == 0)
				{
					RemoveDeploymentTarget(target);
					return;
				}

				if (GetDeploymentOwnerKeys(target).SequenceEqual(owners, StringComparer.OrdinalIgnoreCase))
					return;

				_deploymentChanges[target] = owners;
				_removedDeploymentTargets.Remove(target);
				PersistOrEnlistFoundationChanges();
			}

			public void RemoveDeploymentTarget(ModDeploymentTarget target)
			{
				if (target == null)
					throw new ArgumentNullException(nameof(target));
				if (!IsDeploymentTargetPromoted(target))
					return;

				_deploymentChanges.Remove(target);
				_removedDeploymentTargets.Add(target);
				PersistOrEnlistFoundationChanges();
			}

			private void CommitDeploymentChanges()
			{
				foreach (ModDeploymentTarget target in _removedDeploymentTargets)
					EnlistedInstallLog.RemoveDeploymentTargetCore(target);

				foreach (KeyValuePair<ModDeploymentTarget, string[]> change in _deploymentChanges)
					EnlistedInstallLog.SetDeploymentOwnersCore(change.Key, change.Value);
			}

			private void ClearFoundationChanges()
			{
				_modInstallMethods.Clear();
				_deploymentChanges.Clear();
				_removedDeploymentTargets.Clear();
			}

			private void PersistOrEnlistFoundationChanges()
			{
				if (CurrentTransaction == null)
					Commit();
				else
					Enlist();
			}
		}
	}
}
