using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Owns the C7.11 durable boundary that preserves/detaches an outgoing NMM profile and quarantines outgoing Collection associations.
	/// </summary>
	internal sealed class CollectionLocalRestoreProfileBoundaryCoordinator
	{
		private const string IntentFormat = "nmm-ce.collections.local-restore-profile-boundary-intent/1";
		private const string IntentRole = "local-restore-profile-boundary-intent-v1";
		private const string PreparedRole = "local-restore-profile-boundary-prepared-v1";
		private const int BufferSize = 81920;

		private readonly ServiceManager _services;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		internal CollectionLocalRestoreProfileBoundaryCoordinator(ServiceManager services,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			if (_services.ModManager == null)
				throw new InvalidOperationException("Local restore profile reconciliation requires the live native ModManager.");
		}

		/// <summary>Establishes the profile/association boundary before the first restore native child is allowed to mutate state.</summary>
		internal CollectionLocalRestoreProfileBoundaryIntent Prepare(CollectionOperation operation,
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan)
		{
			ValidateOperation(operation, sealedCapture, reviewedPlan);
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
			if (existing != null)
				return EnsurePrepared(operation, sealedCapture, reviewedPlan);

			IProfileManager profileManager = _services.ModManager.ProfileManager;
			CollectionLocalRestoreOutgoingProfile outgoingProfile = null;
			if (profileManager != null && profileManager.CurrentProfile != null)
			{
				profileManager.UpdateCurrentDeploymentManifest();
				IModProfile currentProfile = profileManager.CurrentProfile;
				outgoingProfile = new CollectionLocalRestoreOutgoingProfile(currentProfile.Id, currentProfile.Name,
					ComputeProfileContentFingerprint(profileManager.GetProfilePath(currentProfile)));
			}

			IReadOnlyList<CollectionTargetAssociation> associations = _associationStore.GetAssociationsForTarget(operation.Target);
			if (associations.Any(x => x.State == CollectionAssociationState.Recovering))
				throw new InvalidOperationException("A Local restore cannot supersede an outgoing Collection association that already requires recovery.");

			var intent = new CollectionLocalRestoreProfileBoundaryIntent(sealedCapture.Capture.Identity.ToString(),
				reviewedPlan.PlanFingerprint, operation.Target.Fingerprint, outgoingProfile,
				associations.Select(x => new CollectionLocalRestoreOutgoingAssociation(x.AssociationId, x.State)));
			CollectionsRetainedArtifactReferenceRecord persisted = PersistIntent(ownerId, intent);
			try
			{
				ApplyPreparedBoundary(intent, persisted.ArtifactId, false);
				return intent;
			}
			catch
			{
				MarkRecoveryRequired(operation);
				throw;
			}
		}

		/// <summary>Re-establishes or verifies the exact durable C7.11 boundary after restart without detaching an unrelated profile.</summary>
		internal CollectionLocalRestoreProfileBoundaryIntent EnsurePrepared(CollectionOperation operation,
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan)
		{
			ValidateOperation(operation, sealedCapture, reviewedPlan);
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
			if (existing == null)
			{
				MarkRecoveryRequired(operation);
				throw new InvalidOperationException("This persisted Local restore predates the required C7.11 profile boundary and cannot safely resume automatically; rebuild and review the restore from current state.");
			}

			CollectionLocalRestoreProfileBoundaryIntent intent;
			try
			{
				intent = ReadIntent(existing.ArtifactId);
				ValidateIntent(intent, sealedCapture, reviewedPlan, operation.Target, ownerId);
				bool prepared = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
					ownerId, PreparedRole) != null;
				ApplyPreparedBoundary(intent, existing.ArtifactId, prepared);
				return intent;
			}
			catch
			{
				MarkRecoveryRequired(operation);
				throw;
			}
		}

		/// <summary>Proves the preserved outgoing profile remains detached and byte-for-byte unchanged.</summary>
		internal void VerifyProfilePreserved(CollectionLocalRestoreProfileBoundaryIntent intent)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			IProfileManager profileManager = _services.ModManager.ProfileManager;
			if (profileManager != null && profileManager.CurrentProfile != null)
				throw new InvalidOperationException("A profile became current while Local restore still owns the detached profile boundary.");
			if (intent.Profile == null)
				return;
			if (profileManager == null)
				throw new InvalidOperationException("The outgoing profile manager is unavailable while verifying Local restore profile preservation.");
			IModProfile profile = profileManager.ModProfiles.FirstOrDefault(x =>
				StringComparer.OrdinalIgnoreCase.Equals(x.Id, intent.Profile.ProfileId));
			if (profile == null)
				throw new InvalidOperationException("The outgoing profile was removed while Local restore was in progress.");
			if (!StringComparer.Ordinal.Equals(profile.Name ?? String.Empty, intent.Profile.ProfileName))
				throw new InvalidOperationException("The outgoing profile identity metadata changed while Local restore was in progress.");
			string actual = ComputeProfileContentFingerprint(profileManager.GetProfilePath(profile));
			if (!StringComparer.Ordinal.Equals(actual, intent.Profile.ContentFingerprint))
				throw new InvalidOperationException("The preserved outgoing profile changed while Local restore was in progress.");
		}

		/// <summary>Gets the current durable intent, or throws when the C7.11 boundary was never established.</summary>
		internal CollectionLocalRestoreProfileBoundaryIntent RequireIntent(CollectionOperation operation,
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan)
		{
			ValidateOperation(operation, sealedCapture, reviewedPlan);
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
			if (existing == null)
				throw new InvalidOperationException("The Local restore has no durable C7.11 profile/association boundary intent.");
			CollectionLocalRestoreProfileBoundaryIntent intent = ReadIntent(existing.ArtifactId);
			ValidateIntent(intent, sealedCapture, reviewedPlan, operation.Target, ownerId);
			return intent;
		}

		/// <summary>Verifies the preserved profile and prepared association quarantine without reapplying the restore boundary.</summary>
		internal CollectionLocalRestoreProfileBoundaryIntent VerifySafeStop(CollectionOperation operation,
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan)
		{
			CollectionLocalRestoreProfileBoundaryIntent intent = RequireIntent(operation, sealedCapture, reviewedPlan);
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord prepared = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, PreparedRole);
			CollectionsRetainedArtifactReferenceRecord original = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
			if (prepared == null || !StringComparer.Ordinal.Equals(prepared.ArtifactId, original.ArtifactId))
				throw new InvalidOperationException("The outgoing profile boundary is not fully prepared; reconcile it before stopping this restore.");
			VerifyPreparedAssociationStates(intent);
			VerifyProfilePreserved(intent);
			return intent;
		}

		private void ApplyPreparedBoundary(CollectionLocalRestoreProfileBoundaryIntent intent, string intentArtifactId,
			bool preparedMarkerExists)
		{
			IProfileManager profileManager = _services.ModManager.ProfileManager;
			if (!preparedMarkerExists)
			{
				var expectedStates = intent.Associations.ToDictionary(x => x.AssociationId, x => x.OriginalState);
				_associationStore.MarkLocalRestoreOutgoingAssociationsRecovering(
					CollectionTargetIdentity.FromFingerprint(intent.TargetFingerprint), expectedStates);

				if (intent.Profile == null)
				{
					if (profileManager != null && profileManager.CurrentProfile != null)
						throw new InvalidOperationException("A new current profile appeared after Local restore review; it will not be detached implicitly.");
				}
				else
				{
					if (profileManager == null)
						throw new InvalidOperationException("The reviewed outgoing profile manager is unavailable at the Local restore mutation boundary.");
					if (profileManager.CurrentProfile != null &&
						!StringComparer.OrdinalIgnoreCase.Equals(profileManager.CurrentProfile.Id, intent.Profile.ProfileId))
						throw new InvalidOperationException("A different profile became current after Local restore review; it will not be detached implicitly.");
					profileManager.DetachCurrentProfileForExternalMutation();
				}

				VerifyPreparedAssociationStates(intent);
				VerifyProfilePreserved(intent);
				_referenceStore.AcquireExclusiveRoleReference(intentArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
					intent.OperationOwnerId, PreparedRole);
				return;
			}

			VerifyPreparedAssociationStates(intent);
			VerifyProfilePreserved(intent);
		}

		private void VerifyPreparedAssociationStates(CollectionLocalRestoreProfileBoundaryIntent intent)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint(intent.TargetFingerprint);
			IReadOnlyList<CollectionTargetAssociation> currentAssociations = _associationStore.GetAssociationsForTarget(target);
			if (!new HashSet<Guid>(currentAssociations.Select(x => x.AssociationId)).SetEquals(intent.Associations.Select(x => x.AssociationId)))
				throw new InvalidOperationException("The outgoing Collection association set changed while Local restore was in progress.");
			foreach (CollectionLocalRestoreOutgoingAssociation expected in intent.Associations)
			{
				CollectionTargetAssociation current = _associationStore.GetAssociation(expected.AssociationId);
				if (current == null || !current.Target.Equals(target))
					throw new InvalidOperationException("An outgoing Collection association disappeared while Local restore was in progress.");
				if (current.State != CollectionAssociationState.Recovering && current.State != CollectionAssociationState.Incomplete)
					throw new InvalidOperationException("An outgoing Collection association escaped the Local restore recovery boundary.");
			}
		}

		private CollectionsRetainedArtifactReferenceRecord PersistIntent(string ownerId,
			CollectionLocalRestoreProfileBoundaryIntent intent)
		{
			intent.OperationOwnerId = ownerId;
			byte[] bytes = SerializeIntent(intent);
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(bytes, false))
				artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				ownerId, IntentRole);
			return _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation, ownerId, IntentRole);
		}

		private byte[] SerializeIntent(CollectionLocalRestoreProfileBoundaryIntent intent)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue(IntentFormat);
				writer.WritePropertyName("captureId"); writer.WriteValue(intent.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(intent.PlanFingerprint);
				writer.WritePropertyName("targetFingerprint"); writer.WriteValue(intent.TargetFingerprint);
				writer.WritePropertyName("operationOwnerId"); writer.WriteValue(intent.OperationOwnerId);
				writer.WritePropertyName("profile");
				if (intent.Profile == null) writer.WriteNull(); else
				{
					writer.WriteStartObject();
					writer.WritePropertyName("profileId"); writer.WriteValue(intent.Profile.ProfileId);
					writer.WritePropertyName("profileName"); writer.WriteValue(intent.Profile.ProfileName);
					writer.WritePropertyName("contentFingerprint"); writer.WriteValue(intent.Profile.ContentFingerprint);
					writer.WriteEndObject();
				}
				writer.WritePropertyName("associations"); writer.WriteStartArray();
				foreach (CollectionLocalRestoreOutgoingAssociation association in intent.Associations.OrderBy(x => x.AssociationId))
				{
					writer.WriteStartObject();
					writer.WritePropertyName("associationId"); writer.WriteValue(association.AssociationId.ToString("D"));
					writer.WritePropertyName("originalState"); writer.WriteValue((int)association.OriginalState);
					writer.WriteEndObject();
				}
				writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private CollectionLocalRestoreProfileBoundaryIntent ReadIntent(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable Local restore profile-boundary intent no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], IntentFormat))
					throw new InvalidDataException("Unsupported Local restore profile-boundary intent format.");
				CollectionLocalRestoreOutgoingProfile profile = null;
				JObject profileObject = root["profile"] as JObject;
				if (profileObject != null)
					profile = new CollectionLocalRestoreOutgoingProfile((string)profileObject["profileId"],
						(string)profileObject["profileName"], (string)profileObject["contentFingerprint"]);
				var associations = new List<CollectionLocalRestoreOutgoingAssociation>();
				JArray associationArray = root["associations"] as JArray;
				if (associationArray == null) throw new InvalidDataException("The Local restore profile-boundary intent is missing its association set.");
				foreach (JObject value in associationArray.OfType<JObject>())
				{
					Guid associationId;
					if (!Guid.TryParse((string)value["associationId"], out associationId) || associationId == Guid.Empty)
						throw new InvalidDataException("The Local restore profile-boundary intent contains an invalid association identity.");
					CollectionAssociationState state = (CollectionAssociationState)(int)value["originalState"];
					associations.Add(new CollectionLocalRestoreOutgoingAssociation(associationId, state));
				}
				return new CollectionLocalRestoreProfileBoundaryIntent((string)root["captureId"],
					(string)root["planFingerprint"], (string)root["targetFingerprint"], profile, associations)
				{
					OperationOwnerId = (string)root["operationOwnerId"]
				};
			}
		}

		private static void ValidateIntent(CollectionLocalRestoreProfileBoundaryIntent intent,
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan, CollectionTargetIdentity target,
			string operationOwnerId)
		{
			if (intent == null || String.IsNullOrWhiteSpace(intent.OperationOwnerId) ||
				!StringComparer.OrdinalIgnoreCase.Equals(intent.OperationOwnerId, operationOwnerId) ||
				!StringComparer.Ordinal.Equals(intent.CaptureId, sealedCapture.Capture.Identity.ToString()) ||
				!StringComparer.Ordinal.Equals(intent.PlanFingerprint, reviewedPlan.PlanFingerprint) ||
				!StringComparer.Ordinal.Equals(intent.TargetFingerprint, target.Fingerprint))
				throw new InvalidDataException("The durable Local restore profile-boundary intent differs from the reviewed restore.");
			if (intent.Associations.GroupBy(x => x.AssociationId).Any(x => x.Count() != 1))
				throw new InvalidDataException("The Local restore profile-boundary intent contains duplicate outgoing associations.");
		}

		private static void ValidateOperation(CollectionOperation operation, CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan)
		{
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal)
				throw new InvalidOperationException("The C7.11 boundary requires one active RestoreLocalCapture operation.");
			if (!operation.Target.Equals(reviewedPlan.Target) || !sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity))
				throw new InvalidOperationException("The C7.11 boundary inputs do not belong to the same reviewed Local restore.");
		}

		private void MarkRecoveryRequired(CollectionOperation operation)
		{
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (current.IsTerminal || (current.Phase == CollectionOperationPhase.RecoveryRequired &&
				current.ResultState == CollectionOperationResultState.RecoveryRequired)) return;
			_operationStore.SaveOperation(new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
				current.Revision, current.PlanIdentity, checked(current.CheckpointSequence + 1), CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired, current.NativeChildren));
		}

		internal static string ComputeProfileContentFingerprint(string profilePath)
		{
			using (SHA256 sha = SHA256.Create())
			using (var sink = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
			using (var writer = new BinaryWriter(sink, Encoding.UTF8, true))
			{
				if (String.IsNullOrWhiteSpace(profilePath) || !Directory.Exists(profilePath))
				{
					writer.Write("profile-absent-v1"); writer.Flush(); sink.FlushFinalBlock();
					return ToHex(sha.Hash);
				}

				string fullProfilePath = Path.GetFullPath(profilePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				writer.Write("profile-content-v1");
				foreach (string file in Directory.GetFiles(fullProfilePath, "*", SearchOption.AllDirectories)
					.OrderBy(x => Path.GetFullPath(x), StringComparer.OrdinalIgnoreCase))
				{
					string relative = Path.GetFullPath(file).Substring(fullProfilePath.Length)
						.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
						.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
					writer.Write(relative);
					var info = new FileInfo(file); writer.Write(info.Length); writer.Flush();
					using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
						input.CopyTo(sink, BufferSize);
				}
				writer.Flush(); sink.FlushFinalBlock();
				return ToHex(sha.Hash);
			}
		}

		private static string ToHex(byte[] bytes)
		{
			var builder = new StringBuilder(bytes.Length * 2);
			foreach (byte value in bytes) builder.Append(value.ToString("x2"));
			return "profile-sha256:" + builder;
		}
	}

	internal sealed class CollectionLocalRestoreProfileBoundaryIntent
	{
		private readonly IReadOnlyList<CollectionLocalRestoreOutgoingAssociation> _associations;
		internal CollectionLocalRestoreProfileBoundaryIntent(string captureId, string planFingerprint, string targetFingerprint,
			CollectionLocalRestoreOutgoingProfile profile, IEnumerable<CollectionLocalRestoreOutgoingAssociation> associations)
		{
			if (String.IsNullOrWhiteSpace(captureId)) throw new ArgumentException("A capture identity is required.", nameof(captureId));
			if (String.IsNullOrWhiteSpace(planFingerprint)) throw new ArgumentException("A plan fingerprint is required.", nameof(planFingerprint));
			if (String.IsNullOrWhiteSpace(targetFingerprint)) throw new ArgumentException("A target fingerprint is required.", nameof(targetFingerprint));
			CaptureId = captureId; PlanFingerprint = planFingerprint; TargetFingerprint = targetFingerprint; Profile = profile;
			List<CollectionLocalRestoreOutgoingAssociation> copied = (associations ?? throw new ArgumentNullException(nameof(associations))).ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("Outgoing association intent cannot contain null entries.", nameof(associations));
			_associations = new ReadOnlyCollection<CollectionLocalRestoreOutgoingAssociation>(copied.OrderBy(x => x.AssociationId).ToList());
		}
		internal string CaptureId { get; }
		internal string PlanFingerprint { get; }
		internal string TargetFingerprint { get; }
		internal CollectionLocalRestoreOutgoingProfile Profile { get; }
		internal IReadOnlyList<CollectionLocalRestoreOutgoingAssociation> Associations { get { return _associations; } }
		internal string OperationOwnerId { get; set; }
	}

	internal sealed class CollectionLocalRestoreOutgoingProfile
	{
		internal CollectionLocalRestoreOutgoingProfile(string profileId, string profileName, string contentFingerprint)
		{
			if (String.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("An outgoing profile identity is required.", nameof(profileId));
			if (String.IsNullOrWhiteSpace(contentFingerprint)) throw new ArgumentException("An outgoing profile content fingerprint is required.", nameof(contentFingerprint));
			ProfileId = profileId; ProfileName = profileName ?? String.Empty; ContentFingerprint = contentFingerprint;
		}
		internal string ProfileId { get; }
		internal string ProfileName { get; }
		internal string ContentFingerprint { get; }
	}

	internal sealed class CollectionLocalRestoreOutgoingAssociation
	{
		internal CollectionLocalRestoreOutgoingAssociation(Guid associationId, CollectionAssociationState originalState)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("A non-empty association identity is required.", nameof(associationId));
			if (!Enum.IsDefined(typeof(CollectionAssociationState), originalState) || originalState == CollectionAssociationState.Unknown ||
				originalState == CollectionAssociationState.Recovering)
				throw new ArgumentOutOfRangeException(nameof(originalState));
			AssociationId = associationId; OriginalState = originalState;
		}
		internal Guid AssociationId { get; }
		internal CollectionAssociationState OriginalState { get; }
	}
}
