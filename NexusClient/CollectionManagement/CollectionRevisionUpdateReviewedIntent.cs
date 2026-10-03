using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>One immutable member row shown by the C10 revision-update review.</summary>
	public sealed class CollectionRevisionUpdateReviewEntry
	{
		private readonly ReadOnlyCollection<Guid> _preservedOverrideIds;
		private readonly ReadOnlyCollection<Guid> _unacceptedDriftIds;

		internal CollectionRevisionUpdateReviewEntry(CollectionMemberKey memberKey, CollectionRevisionUpdateChangeKind changeKind,
			CollectionRevisionUpdateCurrentStateKind currentStateKind, CollectionRevisionUpdateDisposition disposition,
			CollectionRevisionUpdatePreparationKind preparationKind, string oldPreparedFingerprint, string newPreparedFingerprint,
			bool standaloneProtected, string detail, IEnumerable<Guid> preservedOverrideIds, IEnumerable<Guid> unacceptedDriftIds)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateChangeKind), changeKind)) throw new ArgumentOutOfRangeException(nameof(changeKind));
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateCurrentStateKind), currentStateKind)) throw new ArgumentOutOfRangeException(nameof(currentStateKind));
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateDisposition), disposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdatePreparationKind), preparationKind)) throw new ArgumentOutOfRangeException(nameof(preparationKind));
			ChangeKind = changeKind;
			CurrentStateKind = currentStateKind;
			Disposition = disposition;
			PreparationKind = preparationKind;
			OldPreparedFingerprint = OptionalToken(oldPreparedFingerprint, nameof(oldPreparedFingerprint));
			NewPreparedFingerprint = OptionalToken(newPreparedFingerprint, nameof(newPreparedFingerprint));
			StandaloneProtected = standaloneProtected;
			Detail = detail ?? String.Empty;
			_preservedOverrideIds = FreezeGuids(preservedOverrideIds, nameof(preservedOverrideIds));
			_unacceptedDriftIds = FreezeGuids(unacceptedDriftIds, nameof(unacceptedDriftIds));
		}

		public CollectionMemberKey MemberKey { get; }
		public CollectionRevisionUpdateChangeKind ChangeKind { get; }
		public CollectionRevisionUpdateCurrentStateKind CurrentStateKind { get; }
		public CollectionRevisionUpdateDisposition Disposition { get; }
		public CollectionRevisionUpdatePreparationKind PreparationKind { get; }
		public string OldPreparedFingerprint { get; }
		public string NewPreparedFingerprint { get; }
		public bool StandaloneProtected { get; }
		public string Detail { get; }
		public ReadOnlyCollection<Guid> PreservedOverrideIds { get { return _preservedOverrideIds; } }
		public ReadOnlyCollection<Guid> UnacceptedDriftIds { get { return _unacceptedDriftIds; } }

		private static string OptionalToken(string value, string parameterName)
		{
			return value == null ? null : CollectionIdentityValidation.RequireOpaqueToken(value, parameterName);
		}

		private static ReadOnlyCollection<Guid> FreezeGuids(IEnumerable<Guid> values, string parameterName)
		{
			List<Guid> copied = (values ?? throw new ArgumentNullException(parameterName)).ToList();
			if (copied.Any(x => x == Guid.Empty) || copied.Distinct().Count() != copied.Count)
				throw new ArgumentException("Durable revision-update decision identifiers must be non-empty and unique.", parameterName);
			copied.Sort();
			return new ReadOnlyCollection<Guid>(copied);
		}
	}

	/// <summary>One exact effect-level delta frozen into the C10 update review when C10.1 had exact effect previews.</summary>
	public sealed class CollectionRevisionUpdateEffectReview
	{
		internal CollectionRevisionUpdateEffectReview(CollectionMemberKey memberKey, CollectionRevisionUpdateEffectKind kind,
			string resourceKey, CollectionRevisionUpdateEffectChangeKind changeKind)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateEffectKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateEffectChangeKind), changeKind)) throw new ArgumentOutOfRangeException(nameof(changeKind));
			Kind = kind;
			ResourceKey = CollectionIdentityValidation.RequireOpaqueToken(resourceKey, nameof(resourceKey));
			ChangeKind = changeKind;
		}
		public CollectionMemberKey MemberKey { get; }
		public CollectionRevisionUpdateEffectKind Kind { get; }
		public string ResourceKey { get; }
		public CollectionRevisionUpdateEffectChangeKind ChangeKind { get; }
	}

	/// <summary>
	/// Durable C10.2 review intent binding the exact old/current/new comparison and the already explicit C9 overrides to preserve.
	/// </summary>
	/// <remarks>
	/// Approval of this intent does not mutate native or Collections association state. Unaccepted drift, missing/ambiguous native
	/// bindings and unsafe standalone removal remain blockers. Existing C9 overrides are not blockers: approval freezes their exact
	/// durable identities as customization that later C10 execution must preserve or reapply.
	/// </remarks>
	public sealed class CollectionRevisionUpdateReviewedIntent
	{
		private const string ReviewFingerprintFormat = "nmm-ce.collections.revision-update-review-fingerprint/1";
		private readonly ReadOnlyCollection<CollectionRevisionUpdateReviewEntry> _members;
		private readonly ReadOnlyCollection<CollectionRevisionUpdateEffectReview> _effects;
		private readonly ReadOnlyCollection<Guid> _preservedOverrideIds;
		private readonly ReadOnlyCollection<Guid> _unacceptedDriftIds;

		internal CollectionRevisionUpdateReviewedIntent(Guid associationId, CollectionAssociationState associationState,
			CollectionRevisionIdentity oldRevision, CollectionRevisionIdentity candidateRevision, CollectionTargetIdentity target,
			CollectionPlanIdentity oldPlanIdentity, CollectionPlanIdentity candidatePlanIdentity,
			CollectionCurrentStateFingerprint observedStateFingerprint, IEnumerable<CollectionRevisionUpdateReviewEntry> members,
			IEnumerable<CollectionRevisionUpdateEffectReview> effects, IEnumerable<Guid> preservedOverrideIds,
			IEnumerable<Guid> unacceptedDriftIds, bool requiresNativeRepreparation, bool preparedNativeOutputChanged,
			string reviewFingerprint)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("A revision-update review requires a non-empty association identity.", nameof(associationId));
			if (!Enum.IsDefined(typeof(CollectionAssociationState), associationState) || associationState == CollectionAssociationState.Unknown)
				throw new ArgumentOutOfRangeException(nameof(associationState));
			AssociationId = associationId;
			AssociationState = associationState;
			OldRevision = oldRevision ?? throw new ArgumentNullException(nameof(oldRevision));
			CandidateRevision = candidateRevision ?? throw new ArgumentNullException(nameof(candidateRevision));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			OldPlanIdentity = oldPlanIdentity ?? throw new ArgumentNullException(nameof(oldPlanIdentity));
			CandidatePlanIdentity = candidatePlanIdentity ?? throw new ArgumentNullException(nameof(candidatePlanIdentity));
			ObservedStateFingerprint = observedStateFingerprint ?? throw new ArgumentNullException(nameof(observedStateFingerprint));
			if (!OldRevision.Collection.Equals(CandidateRevision.Collection))
				throw new ArgumentException("A revision-update review must remain within one Collection lineage.", nameof(candidateRevision));
			if (OldRevision.Equals(CandidateRevision))
				throw new ArgumentException("A revision-update review requires a distinct candidate revision.", nameof(candidateRevision));

			List<CollectionRevisionUpdateReviewEntry> memberList = (members ?? throw new ArgumentNullException(nameof(members))).ToList();
			if (memberList.Any(x => x == null) || memberList.Select(x => x.MemberKey).Distinct().Count() != memberList.Count)
				throw new ArgumentException("A revision-update review cannot contain null or duplicate member rows.", nameof(members));
			memberList = memberList.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList();
			_members = new ReadOnlyCollection<CollectionRevisionUpdateReviewEntry>(memberList);

			List<CollectionRevisionUpdateEffectReview> effectList = (effects ?? throw new ArgumentNullException(nameof(effects))).ToList();
			if (effectList.Any(x => x == null)) throw new ArgumentException("A revision-update review cannot contain a null effect row.", nameof(effects));
			if (effectList.GroupBy(x => ((int)x.MemberKey.Kind).ToString() + "|" + x.MemberKey.Value + "|" + (int)x.Kind + "|" + x.ResourceKey, StringComparer.Ordinal).Any(x => x.Count() > 1))
				throw new ArgumentException("A revision-update review cannot contain duplicate effect rows.", nameof(effects));
			effectList = effectList.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal)
				.ThenBy(x => x.Kind).ThenBy(x => x.ResourceKey, StringComparer.Ordinal).ToList();
			_effects = new ReadOnlyCollection<CollectionRevisionUpdateEffectReview>(effectList);

			_preservedOverrideIds = FreezeGuids(preservedOverrideIds, nameof(preservedOverrideIds));
			_unacceptedDriftIds = FreezeGuids(unacceptedDriftIds, nameof(unacceptedDriftIds));
			var preservedSet = new HashSet<Guid>(_preservedOverrideIds);
			var driftSet = new HashSet<Guid>(_unacceptedDriftIds);
			if (_members.SelectMany(x => x.PreservedOverrideIds).Any(x => !preservedSet.Contains(x)) ||
				_members.SelectMany(x => x.UnacceptedDriftIds).Any(x => !driftSet.Contains(x)))
				throw new ArgumentException("Member review rows reference customization decisions missing from the aggregate reviewed intent.");
			RequiresNativeRepreparation = requiresNativeRepreparation;
			PreparedNativeOutputChanged = preparedNativeOutputChanged;
			ReviewFingerprint = CollectionIdentityValidation.RequireOpaqueToken(reviewFingerprint, nameof(reviewFingerprint));
			string computed = ComputeReviewFingerprint(this);
			if (!StringComparer.Ordinal.Equals(computed, ReviewFingerprint))
				throw new InvalidDataException("The persisted revision-update review fingerprint does not match its immutable contents.");
		}

		public Guid AssociationId { get; }
		public CollectionAssociationState AssociationState { get; }
		public CollectionRevisionIdentity OldRevision { get; }
		public CollectionRevisionIdentity CandidateRevision { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionPlanIdentity OldPlanIdentity { get; }
		public CollectionPlanIdentity CandidatePlanIdentity { get; }
		public CollectionCurrentStateFingerprint ObservedStateFingerprint { get; }
		public ReadOnlyCollection<CollectionRevisionUpdateReviewEntry> Members { get { return _members; } }
		public ReadOnlyCollection<CollectionRevisionUpdateEffectReview> Effects { get { return _effects; } }
		public ReadOnlyCollection<Guid> PreservedOverrideIds { get { return _preservedOverrideIds; } }
		public ReadOnlyCollection<Guid> UnacceptedDriftIds { get { return _unacceptedDriftIds; } }
		public bool RequiresNativeRepreparation { get; }
		public bool PreparedNativeOutputChanged { get; }
		public string ReviewFingerprint { get; }
		public bool IsApprovable
		{
			get
			{
				return AssociationState != CollectionAssociationState.Incomplete && AssociationState != CollectionAssociationState.Recovering &&
					_unacceptedDriftIds.Count == 0 && !_members.Any(x => x.Disposition == CollectionRevisionUpdateDisposition.DriftRequiresReview ||
						x.Disposition == CollectionRevisionUpdateDisposition.ActionRequired);
			}
		}

		public static CollectionRevisionUpdateReviewedIntent Create(CollectionRevisionUpdatePlan plan)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (plan.NewPlan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new ArgumentException("C10 revision update review requires the additive/current-setup execution policy.", nameof(plan));
			if (!plan.NewPlan.CurrentStateFingerprint.Equals(plan.ObservedStateFingerprint))
				throw new ArgumentException("The candidate revision plan must be resolved against the exact current state observed by the three-way planner.", nameof(plan));

			var members = plan.Members.Select(x => new CollectionRevisionUpdateReviewEntry(x.MemberKey, x.ChangeKind, x.CurrentStateKind,
				x.Disposition, x.PreparationKind, x.OldPreparedIdentity == null ? null : x.OldPreparedIdentity.Fingerprint,
				x.NewPreparedIdentity == null ? null : x.NewPreparedIdentity.Fingerprint, x.StandaloneProtected, x.Detail,
				x.Overrides.Select(o => o.OverrideId), x.Drift.Select(d => d.ObservationId))).ToList();
			var effects = plan.Effects.Select(x => new CollectionRevisionUpdateEffectReview(x.MemberKey, x.Kind, x.ResourceKey, x.ChangeKind)).ToList();
			List<Guid> overrides = plan.Members.SelectMany(x => x.Overrides).Concat(plan.UnscopedOverrides).Select(x => x.OverrideId).Distinct().OrderBy(x => x).ToList();
			List<Guid> drift = plan.Members.SelectMany(x => x.Drift).Concat(plan.UnscopedDrift).Select(x => x.ObservationId).Distinct().OrderBy(x => x).ToList();

			var shell = new CollectionRevisionUpdateReviewedIntentData(plan.Association.AssociationId, plan.Association.State,
				plan.OldPlan.Revision, plan.NewPlan.Revision, plan.NewPlan.Target, plan.OldPlan.Identity, plan.NewPlan.Identity,
				plan.ObservedStateFingerprint, members, effects, overrides, drift, plan.RequiresNativeRepreparation, plan.PreparedNativeOutputChanged);
			string fingerprint = ComputeReviewFingerprint(shell);
			return new CollectionRevisionUpdateReviewedIntent(shell.AssociationId, shell.AssociationState, shell.OldRevision,
				shell.CandidateRevision, shell.Target, shell.OldPlanIdentity, shell.CandidatePlanIdentity, shell.ObservedStateFingerprint,
				shell.Members, shell.Effects, shell.PreservedOverrideIds, shell.UnacceptedDriftIds,
				shell.RequiresNativeRepreparation, shell.PreparedNativeOutputChanged, fingerprint);
		}

		/// <summary>Fails closed when a freshly recomputed C10.1 plan no longer equals the exact reviewed intent.</summary>
		public void ValidateCurrentPlan(CollectionRevisionUpdatePlan currentPlan)
		{
			CollectionRevisionUpdateReviewedIntent current = Create(currentPlan ?? throw new ArgumentNullException(nameof(currentPlan)));
			if (!StringComparer.Ordinal.Equals(ReviewFingerprint, current.ReviewFingerprint))
				throw new InvalidOperationException("The revision-update review is stale because the old/current/new inputs or preservation decisions changed.");
		}

		private static ReadOnlyCollection<Guid> FreezeGuids(IEnumerable<Guid> values, string parameterName)
		{
			List<Guid> copied = (values ?? throw new ArgumentNullException(parameterName)).ToList();
			if (copied.Any(x => x == Guid.Empty) || copied.Distinct().Count() != copied.Count)
				throw new ArgumentException("Revision-update review identifiers must be non-empty and unique.", parameterName);
			copied.Sort();
			return new ReadOnlyCollection<Guid>(copied);
		}

		private static string ComputeReviewFingerprint(CollectionRevisionUpdateReviewedIntent intent)
		{
			return ComputeReviewFingerprint(new CollectionRevisionUpdateReviewedIntentData(intent.AssociationId, intent.AssociationState,
				intent.OldRevision, intent.CandidateRevision, intent.Target, intent.OldPlanIdentity, intent.CandidatePlanIdentity,
				intent.ObservedStateFingerprint, intent.Members, intent.Effects, intent.PreservedOverrideIds, intent.UnacceptedDriftIds,
				intent.RequiresNativeRepreparation, intent.PreparedNativeOutputChanged));
		}

		private static string ComputeReviewFingerprint(CollectionRevisionUpdateReviewedIntentData data)
		{
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
			{
				writer.Write(ReviewFingerprintFormat);
				writer.Write(data.AssociationId.ToString("D"));
				writer.Write((int)data.AssociationState);
				WriteRevision(writer, data.OldRevision);
				WriteRevision(writer, data.CandidateRevision);
				writer.Write(data.Target.Fingerprint);
				WritePlan(writer, data.OldPlanIdentity);
				WritePlan(writer, data.CandidatePlanIdentity);
				writer.Write(data.ObservedStateFingerprint.FormatVersion);
				writer.Write(data.ObservedStateFingerprint.Value);
				writer.Write(data.RequiresNativeRepreparation);
				writer.Write(data.PreparedNativeOutputChanged);
				writer.Write(data.Members.Count);
				foreach (CollectionRevisionUpdateReviewEntry member in data.Members.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal))
				{
					WriteMemberKey(writer, member.MemberKey);
					writer.Write((int)member.ChangeKind);
					writer.Write((int)member.CurrentStateKind);
					writer.Write((int)member.Disposition);
					writer.Write((int)member.PreparationKind);
					WriteOptional(writer, member.OldPreparedFingerprint);
					WriteOptional(writer, member.NewPreparedFingerprint);
					writer.Write(member.StandaloneProtected);
					writer.Write(member.Detail);
					WriteGuids(writer, member.PreservedOverrideIds);
					WriteGuids(writer, member.UnacceptedDriftIds);
				}
				writer.Write(data.Effects.Count);
				foreach (CollectionRevisionUpdateEffectReview effect in data.Effects.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal)
					.ThenBy(x => x.Kind).ThenBy(x => x.ResourceKey, StringComparer.Ordinal))
				{
					WriteMemberKey(writer, effect.MemberKey);
					writer.Write((int)effect.Kind);
					writer.Write(effect.ResourceKey);
					writer.Write((int)effect.ChangeKind);
				}
				WriteGuids(writer, data.PreservedOverrideIds);
				WriteGuids(writer, data.UnacceptedDriftIds);
				writer.Flush();
				using (SHA256 sha = SHA256.Create())
					return "sha256:" + String.Concat(sha.ComputeHash(stream.ToArray()).Select(x => x.ToString("x2")));
			}
		}

		private static void WriteRevision(BinaryWriter writer, CollectionRevisionIdentity revision)
		{
			writer.Write((int)revision.Collection.Origin);
			writer.Write(revision.Collection.StableId);
			writer.Write(revision.StableRevisionId);
			writer.Write(revision.NexusRevisionNumber.HasValue);
			if (revision.NexusRevisionNumber.HasValue) writer.Write(revision.NexusRevisionNumber.Value);
		}
		private static void WritePlan(BinaryWriter writer, CollectionPlanIdentity plan)
		{
			writer.Write(plan.PlanId.ToString("D")); writer.Write(plan.Version);
		}
		private static void WriteMemberKey(BinaryWriter writer, CollectionMemberKey key)
		{
			writer.Write((int)key.Kind); writer.Write(key.Value);
		}
		private static void WriteOptional(BinaryWriter writer, string value)
		{
			writer.Write(value != null); if (value != null) writer.Write(value);
		}
		private static void WriteGuids(BinaryWriter writer, IEnumerable<Guid> values)
		{
			List<Guid> list = values.OrderBy(x => x).ToList();
			writer.Write(list.Count); foreach (Guid value in list) writer.Write(value.ToString("D"));
		}

		private sealed class CollectionRevisionUpdateReviewedIntentData
		{
			public CollectionRevisionUpdateReviewedIntentData(Guid associationId, CollectionAssociationState associationState,
				CollectionRevisionIdentity oldRevision, CollectionRevisionIdentity candidateRevision, CollectionTargetIdentity target,
				CollectionPlanIdentity oldPlanIdentity, CollectionPlanIdentity candidatePlanIdentity,
				CollectionCurrentStateFingerprint observedStateFingerprint, IEnumerable<CollectionRevisionUpdateReviewEntry> members,
				IEnumerable<CollectionRevisionUpdateEffectReview> effects, IEnumerable<Guid> preservedOverrideIds,
				IEnumerable<Guid> unacceptedDriftIds, bool requiresNativeRepreparation, bool preparedNativeOutputChanged)
			{
				AssociationId = associationId; AssociationState = associationState; OldRevision = oldRevision; CandidateRevision = candidateRevision;
				Target = target; OldPlanIdentity = oldPlanIdentity; CandidatePlanIdentity = candidatePlanIdentity;
				ObservedStateFingerprint = observedStateFingerprint; Members = members.ToList(); Effects = effects.ToList();
				PreservedOverrideIds = preservedOverrideIds.ToList(); UnacceptedDriftIds = unacceptedDriftIds.ToList();
				RequiresNativeRepreparation = requiresNativeRepreparation; PreparedNativeOutputChanged = preparedNativeOutputChanged;
			}
			public Guid AssociationId; public CollectionAssociationState AssociationState; public CollectionRevisionIdentity OldRevision;
			public CollectionRevisionIdentity CandidateRevision; public CollectionTargetIdentity Target; public CollectionPlanIdentity OldPlanIdentity;
			public CollectionPlanIdentity CandidatePlanIdentity; public CollectionCurrentStateFingerprint ObservedStateFingerprint;
			public List<CollectionRevisionUpdateReviewEntry> Members; public List<CollectionRevisionUpdateEffectReview> Effects;
			public List<Guid> PreservedOverrideIds; public List<Guid> UnacceptedDriftIds; public bool RequiresNativeRepreparation;
			public bool PreparedNativeOutputChanged;
		}
	}

	/// <summary>Versioned persistence codec for the immutable C10.2 review intent.</summary>
	public static class CollectionRevisionUpdateReviewedIntentCodec
	{
		public const string PayloadFormat = "nmm-ce.collections.revision-update-review/1";

		public static byte[] Serialize(CollectionRevisionUpdateReviewedIntent intent)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			var dto = new IntentDto
			{
				AssociationId = intent.AssociationId.ToString("D"), AssociationState = (int)intent.AssociationState,
				OldRevision = RevisionDto.From(intent.OldRevision), CandidateRevision = RevisionDto.From(intent.CandidateRevision),
				Target = intent.Target.Fingerprint, OldPlan = PlanDto.From(intent.OldPlanIdentity), CandidatePlan = PlanDto.From(intent.CandidatePlanIdentity),
				StateFormat = intent.ObservedStateFingerprint.FormatVersion, StateValue = intent.ObservedStateFingerprint.Value,
				Members = intent.Members.Select(MemberDto.From).ToList(), Effects = intent.Effects.Select(EffectDto.From).ToList(),
				PreservedOverrides = intent.PreservedOverrideIds.Select(x => x.ToString("D")).ToList(),
				UnacceptedDrift = intent.UnacceptedDriftIds.Select(x => x.ToString("D")).ToList(),
				RequiresNativeRepreparation = intent.RequiresNativeRepreparation, PreparedNativeOutputChanged = intent.PreparedNativeOutputChanged,
				ReviewFingerprint = intent.ReviewFingerprint
			};
			return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(dto, Formatting.None));
		}

		public static CollectionRevisionUpdateReviewedIntent Deserialize(byte[] payload)
		{
			if (payload == null) throw new ArgumentNullException(nameof(payload));
			if (payload.Length == 0) throw new ArgumentException("A revision-update review payload cannot be empty.", nameof(payload));
			try
			{
				IntentDto dto = JsonConvert.DeserializeObject<IntentDto>(new UTF8Encoding(false, true).GetString(payload));
				if (dto == null) throw new InvalidDataException("The revision-update review payload is empty.");
				Guid associationId = ParseGuid(dto.AssociationId, "association");
				CollectionAssociationState associationState = (CollectionAssociationState)dto.AssociationState;
				CollectionRevisionIdentity oldRevision = dto.OldRevision.ToRevision();
				CollectionRevisionIdentity candidateRevision = dto.CandidateRevision.ToRevision();
				var members = Require(dto.Members, "members").Select(x => x.ToMember()).ToList();
				var effects = Require(dto.Effects, "effects").Select(x => x.ToEffect()).ToList();
				return new CollectionRevisionUpdateReviewedIntent(associationId, associationState, oldRevision, candidateRevision,
					CollectionTargetIdentity.FromFingerprint(dto.Target), dto.OldPlan.ToPlan(), dto.CandidatePlan.ToPlan(),
					new CollectionCurrentStateFingerprint(dto.StateFormat, dto.StateValue), members, effects,
					Require(dto.PreservedOverrides, "preserved overrides").Select(x => ParseGuid(x, "override")),
					Require(dto.UnacceptedDrift, "unaccepted drift").Select(x => ParseGuid(x, "drift")),
					dto.RequiresNativeRepreparation, dto.PreparedNativeOutputChanged, dto.ReviewFingerprint);
			}
			catch (InvalidDataException) { throw; }
			catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException || ex is FormatException || ex is JsonException || ex is NullReferenceException)
			{
				throw new InvalidDataException("The revision-update review payload is malformed or inconsistent.", ex);
			}
		}

		private static IList<T> Require<T>(IList<T> value, string name)
		{
			if (value == null) throw new InvalidDataException("The revision-update review is missing " + name + ".");
			return value;
		}
		private static Guid ParseGuid(string value, string name)
		{
			Guid parsed; if (!Guid.TryParseExact(value, "D", out parsed) || parsed == Guid.Empty) throw new InvalidDataException("Invalid " + name + " identifier.");
			return parsed;
		}
		private static CollectionIdentity ReadCollection(int origin, string stableId)
		{
			CollectionOrigin value = (CollectionOrigin)origin;
			if (value == CollectionOrigin.NexusMods) return CollectionIdentity.FromNexus(stableId);
			if (value == CollectionOrigin.Local) return CollectionIdentity.FromLocal(ParseGuid(stableId, "local Collection"));
			throw new InvalidDataException("Unsupported Collection origin in revision-update review.");
		}
		private static CollectionMemberKey ReadMemberKey(int kind, string value)
		{
			switch ((CollectionMemberKeyKind)kind)
			{
				case CollectionMemberKeyKind.ProviderStable: return CollectionMemberKey.FromProvider(value);
				case CollectionMemberKeyKind.Local: return CollectionMemberKey.FromLocal(ParseGuid(value, "local member"));
				case CollectionMemberKeyKind.ValidatedMatch: return CollectionMemberKey.FromValidatedMatch(value);
				default: throw new InvalidDataException("Unsupported member-key kind in revision-update review.");
			}
		}

		private sealed class IntentDto
		{
			public string AssociationId; public int AssociationState; public RevisionDto OldRevision; public RevisionDto CandidateRevision;
			public string Target; public PlanDto OldPlan; public PlanDto CandidatePlan; public string StateFormat; public string StateValue;
			public List<MemberDto> Members; public List<EffectDto> Effects; public List<string> PreservedOverrides; public List<string> UnacceptedDrift;
			public bool RequiresNativeRepreparation; public bool PreparedNativeOutputChanged; public string ReviewFingerprint;
		}
		private sealed class RevisionDto
		{
			public int Origin; public string Collection; public string Revision; public long? Number;
			public static RevisionDto From(CollectionRevisionIdentity value) { return new RevisionDto { Origin=(int)value.Collection.Origin, Collection=value.Collection.StableId, Revision=value.StableRevisionId, Number=value.NexusRevisionNumber }; }
			public CollectionRevisionIdentity ToRevision()
			{
				CollectionIdentity collection = ReadCollection(Origin, Collection);
				if (collection.Origin == CollectionOrigin.NexusMods)
				{
					if (!Number.HasValue) throw new InvalidDataException("A Nexus revision-update review requires its concrete revision number.");
					return CollectionRevisionIdentity.FromNexus(collection, Revision, Number.Value);
				}
				if (Number.HasValue) throw new InvalidDataException("A Local revision-update review cannot carry a Nexus revision number.");
				return CollectionRevisionIdentity.FromLocal(collection, ParseGuid(Revision, "local revision"));
			}
		}
		private sealed class PlanDto
		{
			public string Id; public int Version;
			public static PlanDto From(CollectionPlanIdentity value) { return new PlanDto { Id=value.PlanId.ToString("D"), Version=value.Version }; }
			public CollectionPlanIdentity ToPlan() { return CollectionPlanIdentity.From(ParseGuid(Id, "plan"), Version); }
		}
		private sealed class MemberDto
		{
			public int KeyKind; public string Key; public int Change; public int Current; public int Disposition; public int Preparation;
			public string OldPrepared; public string NewPrepared; public bool StandaloneProtected; public string Detail; public List<string> Overrides; public List<string> Drift;
			public static MemberDto From(CollectionRevisionUpdateReviewEntry value) { return new MemberDto { KeyKind=(int)value.MemberKey.Kind, Key=value.MemberKey.Value, Change=(int)value.ChangeKind, Current=(int)value.CurrentStateKind, Disposition=(int)value.Disposition, Preparation=(int)value.PreparationKind, OldPrepared=value.OldPreparedFingerprint, NewPrepared=value.NewPreparedFingerprint, StandaloneProtected=value.StandaloneProtected, Detail=value.Detail, Overrides=value.PreservedOverrideIds.Select(x=>x.ToString("D")).ToList(), Drift=value.UnacceptedDriftIds.Select(x=>x.ToString("D")).ToList() }; }
			public CollectionRevisionUpdateReviewEntry ToMember() { return new CollectionRevisionUpdateReviewEntry(ReadMemberKey(KeyKind, Key), (CollectionRevisionUpdateChangeKind)Change, (CollectionRevisionUpdateCurrentStateKind)Current, (CollectionRevisionUpdateDisposition)Disposition, (CollectionRevisionUpdatePreparationKind)Preparation, OldPrepared, NewPrepared, StandaloneProtected, Detail, Require(Overrides,"member overrides").Select(x=>ParseGuid(x,"override")), Require(Drift,"member drift").Select(x=>ParseGuid(x,"drift"))); }
		}
		private sealed class EffectDto
		{
			public int KeyKind; public string Key; public int Kind; public string Resource; public int Change;
			public static EffectDto From(CollectionRevisionUpdateEffectReview value) { return new EffectDto { KeyKind=(int)value.MemberKey.Kind, Key=value.MemberKey.Value, Kind=(int)value.Kind, Resource=value.ResourceKey, Change=(int)value.ChangeKind }; }
			public CollectionRevisionUpdateEffectReview ToEffect() { return new CollectionRevisionUpdateEffectReview(ReadMemberKey(KeyKind, Key), (CollectionRevisionUpdateEffectKind)Kind, Resource, (CollectionRevisionUpdateEffectChangeKind)Change); }
		}
	}
}
