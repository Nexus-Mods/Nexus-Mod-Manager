using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Produces the deterministic versioned fingerprint for a C6 native-state index.
	/// </summary>
	internal static class CollectionNativeStateFingerprintBuilder
	{
		private const string FormatVersion = "native-state-index-v2";

		/// <summary>Builds the deterministic composite fingerprint for one captured state index.</summary>
		public static CollectionCurrentStateFingerprint Build(CollectionNativeStateIndex index)
		{
			if (index == null)
				throw new ArgumentNullException(nameof(index));

			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				Write(writer, index.Target.Fingerprint);
				writer.Write(index.DeploymentCommitSequence);
				writer.Write((int)index.PluginCoverage);
				writer.Write((int)index.AssociationCoverage);

				writer.Write(index.Roots.Count);
				foreach (CollectionNativeRootState root in index.Roots.OrderBy(x => (int)x.Root))
				{
					writer.Write((int)root.Root);
					Write(writer, root.PhysicalPath);
				}

				writer.Write(index.Mods.Count);
				foreach (CollectionNativeModState mod in index.Mods.Values.OrderBy(x => x.Identity.NativeModKey, StringComparer.Ordinal))
				{
					Write(writer, mod.Identity.NativeModKey);
					Write(writer, mod.ArchivePath);
					Write(writer, mod.FileName);
					Write(writer, mod.NexusModId);
					Write(writer, mod.NexusFileId);
					Write(writer, mod.HumanReadableVersion);
					Write(writer, mod.MachineVersion);
					writer.Write(mod.HasInstallScript);
					writer.Write((int)mod.InstallRoot);
					writer.Write((int)mod.InstallMethod);
				}

				writer.Write(index.Files.Count);
				foreach (CollectionNativeFileState file in index.Files.Values
					.OrderBy(x => (int)x.Target.Root).ThenBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase))
				{
					writer.Write((int)file.Target.Root);
					Write(writer, file.Target.RelativePath);
					Write(writer, file.PhysicalPath);
					writer.Write(file.Promoted);
					writer.Write(file.RecordedByInstallLog);
					writer.Write(file.RecordedByVirtualState);
					Write(writer, file.EffectiveOwnerKey);
					WriteOwners(writer, file.InstallLogOwners);
					WriteOwners(writer, file.DeploymentOwners);
					WriteOwners(writer, file.VirtualOwners);
				}

				writer.Write(index.IniEdits.Count);
				foreach (CollectionNativeIniState ini in index.IniEdits.Values
					.OrderBy(x => x.Key.File, StringComparer.OrdinalIgnoreCase)
					.ThenBy(x => x.Key.Section, StringComparer.OrdinalIgnoreCase)
					.ThenBy(x => x.Key.Key, StringComparer.OrdinalIgnoreCase))
				{
					Write(writer, ini.Key.File.ToUpperInvariant());
					Write(writer, ini.Key.Section.ToUpperInvariant());
					Write(writer, ini.Key.Key.ToUpperInvariant());
					writer.Write(ini.Values.Count);
					foreach (CollectionNativeTextOwnerValue value in ini.Values)
					{
						Write(writer, value.OwnerKey);
						Write(writer, value.Value);
					}
				}

				writer.Write(index.GameValues.Count);
				foreach (CollectionNativeGameValueState value in index.GameValues.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
				{
					Write(writer, value.Key);
					writer.Write(value.Values.Count);
					foreach (CollectionNativeBinaryOwnerValue owner in value.Values)
					{
						Write(writer, owner.OwnerKey);
						byte[] bytes = owner.UnsafeValue;
						writer.Write(bytes == null ? -1 : bytes.Length);
						if (bytes != null)
							writer.Write(bytes);
					}
				}

				writer.Write(index.Plugins.Count);
				foreach (CollectionNativePluginState plugin in index.Plugins.Values
					.OrderBy(x => x.Priority).ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
				{
					Write(writer, plugin.FileName.ToUpperInvariant());
					writer.Write(plugin.Active);
					writer.Write(plugin.Priority);
					writer.Write(plugin.AllocatedIndex.HasValue);
					if (plugin.AllocatedIndex.HasValue)
						writer.Write(plugin.AllocatedIndex.Value);
					Write(writer, plugin.ModIndex);
					writer.Write((int)plugin.ParseStatus);
					writer.Write((int)plugin.AddressClass);
					writer.Write((int)plugin.HeaderFlags);
					writer.Write((int)plugin.SpecialFlags);
					writer.Write(plugin.EffectiveMaster);
					writer.Write(plugin.FormVersion);
					writer.Write(plugin.Masters.Count);
					foreach (string master in plugin.Masters)
						Write(writer, (master ?? String.Empty).ToUpperInvariant());
					writer.Write(plugin.Diagnostics.Count);
					foreach (CollectionNativePluginDiagnostic diagnostic in plugin.Diagnostics)
					{
						writer.Write((int)diagnostic.Kind);
						writer.Write((int)diagnostic.Severity);
					}
				}

				writer.Write(index.Associations.Count);
				foreach (CollectionTargetAssociation association in index.Associations.Values.OrderBy(x => x.AssociationId))
				{
					Write(writer, association.AssociationId.ToString("D"));
					Write(writer, association.Revision.ToString());
					writer.Write((int)association.State);
				}

				writer.Write(index.BindingsByNativeMod.Values.Sum(x => x.Count));
				foreach (CollectionMemberBinding binding in index.BindingsByNativeMod.Values.SelectMany(x => x)
					.OrderBy(x => x.Association.AssociationId).ThenBy(x => x.MemberKey.ToString(), StringComparer.Ordinal))
				{
					Write(writer, binding.Association.AssociationId.ToString("D"));
					Write(writer, binding.MemberKey.ToString());
					Write(writer, binding.NativeMod.NativeModKey);
					Write(writer, binding.VerifiedRecipe.Fingerprint);
					writer.Write((int)binding.BindingKind);
				}

				writer.Write(index.OverridesByAssociation.Values.Sum(x => x.Count));
				foreach (UserOverride userOverride in index.OverridesByAssociation.Values.SelectMany(x => x)
					.OrderBy(x => x.OverrideId))
				{
					Write(writer, userOverride.OverrideId.ToString("D"));
					Write(writer, userOverride.Requirement.AssociationId.ToString("D"));
					Write(writer, userOverride.Requirement.BaselineRevision.ToString());
					Write(writer, userOverride.Requirement.MemberKey == null ? null : userOverride.Requirement.MemberKey.ToString());
					writer.Write((int)userOverride.Requirement.Aspect);
					Write(writer, userOverride.Requirement.SubjectKey);
					writer.Write((int)userOverride.BaselineState.Kind);
					Write(writer, userOverride.BaselineState.FormatVersion);
					Write(writer, userOverride.BaselineState.Fingerprint);
					writer.Write((int)userOverride.UserChosenState.Kind);
					Write(writer, userOverride.UserChosenState.FormatVersion);
					Write(writer, userOverride.UserChosenState.Fingerprint);
				}

				writer.Write(index.Issues.Count);
				foreach (CollectionNativeStateIssue issue in index.Issues
					.OrderBy(x => (int)x.Kind).ThenBy(x => x.ResourceKey, StringComparer.Ordinal))
				{
					writer.Write((int)issue.Kind);
					Write(writer, issue.ResourceKey);
				}
				writer.Flush();

				using (SHA256 sha256 = SHA256.Create())
				{
					byte[] hash = sha256.ComputeHash(stream.ToArray());
					return new CollectionCurrentStateFingerprint(FormatVersion,
						String.Concat(hash.Select(x => x.ToString("x2"))));
				}
			}
		}

		private static void WriteOwners(BinaryWriter writer, System.Collections.Generic.ICollection<CollectionNativeOwnerState> owners)
		{
			writer.Write(owners.Count);
			foreach (CollectionNativeOwnerState owner in owners)
			{
				Write(writer, owner.OwnerKey);
				Write(writer, owner.UnresolvedReference);
				writer.Write((int)owner.Kind);
				writer.Write(owner.VirtualLinkActive.HasValue);
				if (owner.VirtualLinkActive.HasValue)
					writer.Write(owner.VirtualLinkActive.Value);
				writer.Write(owner.VirtualPriority.HasValue);
				if (owner.VirtualPriority.HasValue)
					writer.Write(owner.VirtualPriority.Value);
				Write(writer, owner.VirtualStagedSourcePath);
			}
		}

		private static void Write(BinaryWriter writer, string value)
		{
			bool hasValue = value != null;
			writer.Write(hasValue);
			if (hasValue)
				writer.Write(value);
		}
	}
}
