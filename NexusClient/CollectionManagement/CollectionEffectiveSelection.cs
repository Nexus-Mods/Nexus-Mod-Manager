using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Records one explicit effective-selection decision for an optional Collection member.
	/// </summary>
	public sealed class CollectionOptionalMemberSelection
	{
		/// <summary>
		/// Creates an immutable optional-member selection decision keyed by stable member identity.
		/// </summary>
		public CollectionOptionalMemberSelection(CollectionMemberKey memberKey, CollectionMemberSelection selection)
		{
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));
			if (!Enum.IsDefined(typeof(CollectionMemberSelection), selection) || selection == CollectionMemberSelection.Unknown)
				throw new ArgumentOutOfRangeException(nameof(selection));

			MemberKey = memberKey;
			Selection = selection;
		}

		/// <summary>
		/// Gets the stable optional-member identity being selected or unselected.
		/// </summary>
		public CollectionMemberKey MemberKey { get; }

		/// <summary>
		/// Gets the requested effective selection state.
		/// </summary>
		public CollectionMemberSelection Selection { get; }
	}

	/// <summary>
	/// Immutable selection-only projection over one exact normalized Collection manifest.
	/// </summary>
	/// <remarks>
	/// Provider-normalized data is never mutated. Capability is recalculated against the effective selected closure while
	/// preserving the explicit source/adapter findings from the exact normalized manifest.
	/// </remarks>
	public sealed class CollectionEffectiveSelection
	{
		internal CollectionEffectiveSelection(
			NormalizedCollectionManifest manifest,
			CollectionCapabilityReport capabilityReport,
			string selectionFingerprintFormatVersion,
			string selectionFingerprint)
		{
			Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
			CapabilityReport = capabilityReport ?? throw new ArgumentNullException(nameof(capabilityReport));
			if (!ReferenceEquals(manifest, capabilityReport.Manifest))
				throw new ArgumentException("The effective capability report must describe the effective manifest.", nameof(capabilityReport));
			SelectionFingerprintFormatVersion = CollectionIdentityValidation.RequireOpaqueToken(
				selectionFingerprintFormatVersion, nameof(selectionFingerprintFormatVersion));
			SelectionFingerprint = CollectionIdentityValidation.RequireOpaqueToken(selectionFingerprint, nameof(selectionFingerprint));
		}

		/// <summary>
		/// Gets the immutable manifest projection carrying the effective selected/unselected state.
		/// </summary>
		public NormalizedCollectionManifest Manifest { get; }

		/// <summary>
		/// Gets the capability report recalculated for the effective selected closure.
		/// </summary>
		public CollectionCapabilityReport CapabilityReport { get; }

		/// <summary>
		/// Gets the format identity used to calculate <see cref="SelectionFingerprint"/>.
		/// </summary>
		public string SelectionFingerprintFormatVersion { get; }

		/// <summary>
		/// Gets a deterministic SHA-256 identity for this exact source plus effective member selections.
		/// </summary>
		/// <remarks>
		/// C6.15.4 can use this identity when deciding whether changed optional choices require a new resolved-plan version.
		/// </remarks>
		public string SelectionFingerprint { get; }
	}

	/// <summary>
	/// Builds immutable effective optional-member selections without changing provider-normalized Collection data.
	/// </summary>
	public sealed class CollectionEffectiveSelectionBuilder
	{
		private const string FingerprintFormatVersion = "nmm-ce.collections.effective-selection/1";

		/// <summary>
		/// Applies explicit optional-member decisions to one normalized capability snapshot and recalculates selected-closure capability.
		/// </summary>
		/// <param name="normalizedCapabilityReport">The exact normalized source capability snapshot.</param>
		/// <param name="optionalSelections">Explicit stable-key decisions for optional members only.</param>
		public CollectionEffectiveSelection Build(
			CollectionCapabilityReport normalizedCapabilityReport,
			IEnumerable<CollectionOptionalMemberSelection> optionalSelections)
		{
			if (normalizedCapabilityReport == null)
				throw new ArgumentNullException(nameof(normalizedCapabilityReport));
			if (optionalSelections == null)
				throw new ArgumentNullException(nameof(optionalSelections));

			NormalizedCollectionManifest sourceManifest = normalizedCapabilityReport.Manifest;
			Dictionary<CollectionMemberKey, NormalizedCollectionMember> resolvedMembers =
				new Dictionary<CollectionMemberKey, NormalizedCollectionMember>();
			foreach (NormalizedCollectionMember member in sourceManifest.Members)
			{
				if (member.IdentityResolution.IsResolved)
					resolvedMembers.Add(member.IdentityResolution.Key, member);
			}

			Dictionary<CollectionMemberKey, CollectionMemberSelection> overrides =
				new Dictionary<CollectionMemberKey, CollectionMemberSelection>();
			foreach (CollectionOptionalMemberSelection optionalSelection in optionalSelections)
			{
				if (optionalSelection == null)
					throw new ArgumentException("An effective selection cannot contain a null optional-member decision.", nameof(optionalSelections));

				NormalizedCollectionMember member;
				if (!resolvedMembers.TryGetValue(optionalSelection.MemberKey, out member))
					throw new ArgumentException("An optional-member selection must reference a resolved member in the normalized manifest.", nameof(optionalSelections));
				if (member.Requirement != CollectionMemberRequirement.Optional)
					throw new ArgumentException("Only optional Collection members can be changed by the effective-selection overlay.", nameof(optionalSelections));
				if (overrides.ContainsKey(optionalSelection.MemberKey))
					throw new ArgumentException("An optional Collection member cannot have more than one effective-selection decision.", nameof(optionalSelections));

				overrides.Add(optionalSelection.MemberKey, optionalSelection.Selection);
			}

			List<NormalizedCollectionMember> effectiveMembers = new List<NormalizedCollectionMember>();
			foreach (NormalizedCollectionMember member in sourceManifest.Members)
			{
				CollectionMemberSelection selection = member.Selection;
				if (member.Requirement == CollectionMemberRequirement.Optional && member.IdentityResolution.IsResolved)
				{
					CollectionMemberSelection overrideSelection;
					if (overrides.TryGetValue(member.IdentityResolution.Key, out overrideSelection))
						selection = overrideSelection;
				}

				effectiveMembers.Add(new NormalizedCollectionMember(
					member.SourceOrdinal,
					member.IdentityResolution,
					member.Requirement,
					selection,
					member.Artifact,
					member.RecipeIdentity,
					member.DisplayName,
					member.InstallationPhase));
			}

			NormalizedCollectionManifest effectiveManifest = new NormalizedCollectionManifest(
				sourceManifest.Revision,
				sourceManifest.Source,
				sourceManifest.MemberSetCompleteness,
				sourceManifest.IncompletenessReason,
				effectiveMembers,
				sourceManifest.Dependencies,
				sourceManifest.FilePriorityRules);
			CollectionCapabilityReport effectiveCapabilityReport = normalizedCapabilityReport.RecalculateForSelection(effectiveManifest);

			return new CollectionEffectiveSelection(
				effectiveManifest,
				effectiveCapabilityReport,
				FingerprintFormatVersion,
				ComputeSelectionFingerprint(effectiveManifest));
		}

		private static string ComputeSelectionFingerprint(NormalizedCollectionManifest manifest)
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(FingerprintFormatVersion);
				writer.Write((int)manifest.Revision.Collection.Origin);
				writer.Write(manifest.Revision.Collection.StableId);
				writer.Write(manifest.Revision.StableRevisionId);
				writer.Write(manifest.Revision.NexusRevisionNumber.HasValue);
				if (manifest.Revision.NexusRevisionNumber.HasValue)
					writer.Write(manifest.Revision.NexusRevisionNumber.Value);
				writer.Write((int)manifest.Source.ContentHash.Algorithm);
				writer.Write(manifest.Source.ContentHash.Value);
				writer.Write(manifest.Source.ByteLength);
				writer.Write(manifest.Source.SchemaIdentity);
				writer.Write(manifest.Source.NormalizerVersion);
				writer.Write(manifest.Members.Count);
				foreach (NormalizedCollectionMember member in manifest.Members)
				{
					writer.Write(member.SourceOrdinal);
					writer.Write((int)member.Requirement);
					writer.Write((int)member.Selection);
					writer.Write(member.IdentityResolution.IsResolved);
					if (member.IdentityResolution.IsResolved)
					{
						writer.Write((int)member.IdentityResolution.Key.Kind);
						writer.Write(member.IdentityResolution.Key.Value);
					}
				}
				writer.Flush();

				using (SHA256 sha256 = SHA256.Create())
				{
					return "sha256:" + BitConverter.ToString(sha256.ComputeHash(stream.ToArray())).Replace("-", String.Empty).ToLowerInvariant();
				}
			}
		}
	}
}
