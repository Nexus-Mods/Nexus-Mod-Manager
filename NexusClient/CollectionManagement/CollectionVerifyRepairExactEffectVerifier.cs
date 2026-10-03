using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Applies exact, read-only effect checks for C10.10 verify/repair from characterized member effect previews.</summary>
	public sealed class CollectionVerifyRepairExactEffectVerifier
	{
		public IReadOnlyList<CollectionVerifyRepairFinding> Verify(CollectionTargetAssociation association,
			CollectionNativeStateIndex state, IEnumerable<CollectionMemberBinding> bindings,
			IEnumerable<CollectionMemberEffectPreview> previews, IEnumerable<UserOverride> overrides = null)
		{
			if (association == null) throw new ArgumentNullException(nameof(association));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (bindings == null) throw new ArgumentNullException(nameof(bindings));
			if (previews == null) throw new ArgumentNullException(nameof(previews));
			if (!association.Target.Equals(state.Target)) throw new ArgumentException("Effect verification target mismatch.");

			Dictionary<CollectionMemberKey, CollectionMemberBinding> bindingByMember = bindings
				.Where(x => x.Association.AssociationId == association.AssociationId)
				.ToDictionary(x => x.MemberKey);
			var findings = new List<CollectionVerifyRepairFinding>();
			List<UserOverride> overrideList = (overrides ?? Enumerable.Empty<UserOverride>()).Where(x => x.Requirement.AssociationId == association.AssociationId).ToList();
			foreach (CollectionMemberEffectPreview preview in previews)
			{
				if (preview == null || !preview.IsComplete)
					throw new InvalidOperationException("Exact verify/repair requires complete characterized member effect previews.");
				CollectionMemberBinding binding;
				if (!bindingByMember.TryGetValue(preview.MemberKey, out binding)) continue;
				string ownerKey = binding.NativeMod.NativeModKey;

				foreach (CollectionPlannedFileEffect effect in preview.Files)
				{
					CollectionNativeFileState file;
					bool owned = state.Files.TryGetValue(effect.Target, out file) && ContainsOwner(file, ownerKey);
					if (!owned || String.IsNullOrWhiteSpace(file.PhysicalPath) || !File.Exists(file.PhysicalPath))
					{
						findings.Add(EffectFinding(association, preview.MemberKey, CollectionRequirementAspect.FileWinner, effect.Target.ToString(), overrideList,
							CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch, CollectionVerifyRepairDisposition.RestoreExpectedState,
							"A characterized managed file effect is missing or is no longer owned by the bound Collection member."));
						continue;
					}
					if (!effect.HasExactContentIdentity)
					{
						findings.Add(EffectFinding(association, preview.MemberKey, CollectionRequirementAspect.FileWinner, effect.Target.ToString(), overrideList,
							CollectionVerifyRepairFindingKind.FileContentVerificationUnavailable, CollectionVerifyRepairDisposition.ActionRequired,
							"Managed ownership and physical presence are proven, but this effect preview does not carry an exact destination-content hash; byte corruption cannot be repaired automatically."));
						continue;
					}
					FileInfo info = new FileInfo(file.PhysicalPath);
					if (info.Length != effect.ExpectedByteLength.Value || !effect.ExpectedContentHash.Equals(ComputeHash(file.PhysicalPath)))
						findings.Add(EffectFinding(association, preview.MemberKey, CollectionRequirementAspect.FileWinner, effect.Target.ToString(), overrideList,
							CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch, CollectionVerifyRepairDisposition.RestoreExpectedState,
							"The managed file is present but its exact bytes do not match the retained native recipe."));
				}

				foreach (CollectionPlannedIniEffect effect in preview.IniEdits)
				{
					CollectionNativeIniState ini;
					CollectionNativeTextOwnerValue current = null;
					if (state.IniEdits.TryGetValue(effect.Key, out ini) && ini.Values.Count > 0) current = ini.Values[ini.Values.Count - 1];
					if (current == null || !StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) || !StringComparer.Ordinal.Equals(current.Value, effect.Value))
						findings.Add(EffectFinding(association, preview.MemberKey, CollectionRequirementAspect.ConfigurationState, effect.Key.ToString(), overrideList,
							CollectionVerifyRepairFindingKind.IniEffectMismatch, CollectionVerifyRepairDisposition.RestoreExpectedState, "A characterized INI effect differs from the retained recipe."));
				}

				foreach (CollectionPlannedGameValueEffect effect in preview.GameValues)
				{
					CollectionNativeGameValueState value;
					CollectionNativeBinaryOwnerValue current = null;
					if (state.GameValues.TryGetValue(effect.Key, out value) && value.Values.Count > 0) current = value.Values[value.Values.Count - 1];
					if (current == null || !StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) || !BytesEqual(current.UnsafeValue, effect.UnsafeValue))
						findings.Add(EffectFinding(association, preview.MemberKey, CollectionRequirementAspect.ConfigurationState, effect.Key, overrideList,
							CollectionVerifyRepairFindingKind.GameValueEffectMismatch, CollectionVerifyRepairDisposition.RestoreExpectedState, "A characterized game-specific value differs from the retained recipe."));
				}

				if (preview.PluginEffects.Count > 0 && state.PluginCoverage != CollectionNativeStateCoverage.Complete)
					findings.Add(EffectFinding(association, preview.MemberKey, CollectionRequirementAspect.PluginState, "plugin-state", overrideList,
						CollectionVerifyRepairFindingKind.PluginEffectMismatch, CollectionVerifyRepairDisposition.ActionRequired, "Plugin state is unavailable for exact verification."));
				else
					foreach (CollectionPlannedPluginEffect effect in preview.PluginEffects)
						if (!CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(state, effect))
							findings.Add(EffectFinding(association, preview.MemberKey, CollectionRequirementAspect.PluginState, String.Join("|", effect.PluginPaths), overrideList,
								CollectionVerifyRepairFindingKind.PluginEffectMismatch, CollectionVerifyRepairDisposition.RestoreExpectedState, "A characterized plugin effect differs from the retained recipe."));
			}
			return findings;
		}

		private static CollectionContentHash ComputeHash(string path)
		{
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant());
		}

		private static CollectionVerifyRepairFinding EffectFinding(CollectionTargetAssociation association, CollectionMemberKey member,
			CollectionRequirementAspect aspect, string subject, IList<UserOverride> overrides, CollectionVerifyRepairFindingKind kind,
			CollectionVerifyRepairDisposition disposition, string detail)
		{
			var requirement = new CollectionRequirementReference(association, member, aspect, subject);
			UserOverride local = overrides == null ? null : overrides.SingleOrDefault(x => x.Requirement.Equals(requirement));
			if (local != null)
				return new CollectionVerifyRepairFinding(member, requirement, CollectionVerifyRepairFindingKind.PreservedExplicitOverride,
					CollectionVerifyRepairDisposition.PreserveLocalDecision, local.UserChosenState, local.UserChosenState,
					"The exact native effect differs from the retained recipe at a requirement with an explicit C9 override; verify/repair preserves that local decision.");
			return new CollectionVerifyRepairFinding(member, requirement, kind, disposition, null, null, detail);
		}

		private static bool ContainsOwner(CollectionNativeFileState file, string ownerKey)
		{
			return file.InstallLogOwners.Concat(file.DeploymentOwners).Concat(file.VirtualOwners)
				.Any(x => x.Kind == CollectionNativeOwnerKind.NativeMod && StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey));
		}

		private static bool BytesEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
			return true;
		}
	}
}
