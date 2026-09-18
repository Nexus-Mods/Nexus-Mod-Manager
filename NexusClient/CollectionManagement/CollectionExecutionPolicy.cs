using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies the user-selected policy for applying a collection revision to a target.
	/// </summary>
	public enum CollectionExecutionPolicyKind
	{
		/// <summary>
		/// No valid policy has been selected.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// Preserve unrelated managed mods and reconcile the incoming collection with the current setup.
		/// </summary>
		InstallIntoCurrentSetup = 1,

		/// <summary>
		/// Perform an explicitly reviewed transition to the incoming selected recipe, removing outgoing managed effects where required.
		/// </summary>
		ReplaceCurrentManagedSetup = 2
	}

	/// <summary>
	/// Records the user's optional persistent Local Collection backup decision for a replacement operation.
	/// </summary>
	/// <remarks>
	/// This choice is independent from mandatory operation recovery inputs. Declining a persistent Local Collection backup
	/// never authorizes the coordinator to discard rollback/reconciliation material required by the active operation.
	/// </remarks>
	public enum CollectionReplacementBackupChoice
	{
		/// <summary>
		/// The backup decision does not apply to the selected policy.
		/// </summary>
		NotApplicable = 0,

		/// <summary>
		/// Replacement has been selected but the user has not yet chosen whether to create a persistent Local Collection backup.
		/// </summary>
		Undecided = 1,

		/// <summary>
		/// The user requested a persistent Local Collection capture before destructive replacement work.
		/// </summary>
		CreateLocalCollection = 2,

		/// <summary>
		/// The user explicitly chose to continue replacement without a persistent Local Collection backup.
		/// </summary>
		ContinueWithoutLocalCollection = 3
	}

	/// <summary>
	/// Immutable execution-policy selection used by collection planning and later execution.
	/// </summary>
	/// <remarks>
	/// The policy describes product intent only. It does not authorize mutation by itself and does not contain a native
	/// execution plan. Importing, downloading or previewing a collection must not manufacture a replacement operation.
	/// </remarks>
	public sealed class CollectionExecutionPolicy : IEquatable<CollectionExecutionPolicy>
	{
		private CollectionExecutionPolicy(CollectionExecutionPolicyKind kind, CollectionReplacementBackupChoice replacementBackupChoice)
		{
			if (!Enum.IsDefined(typeof(CollectionExecutionPolicyKind), kind) || kind == CollectionExecutionPolicyKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (!Enum.IsDefined(typeof(CollectionReplacementBackupChoice), replacementBackupChoice))
				throw new ArgumentOutOfRangeException(nameof(replacementBackupChoice));

			if (kind == CollectionExecutionPolicyKind.InstallIntoCurrentSetup &&
				replacementBackupChoice != CollectionReplacementBackupChoice.NotApplicable)
				throw new ArgumentException("A replacement backup choice cannot be attached to the install-into-current-setup policy.", nameof(replacementBackupChoice));

			if (kind == CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup &&
				replacementBackupChoice == CollectionReplacementBackupChoice.NotApplicable)
				throw new ArgumentException("Replacement must retain an explicit backup-decision state until destructive work begins.", nameof(replacementBackupChoice));

			Kind = kind;
			ReplacementBackupChoice = replacementBackupChoice;
		}

		/// <summary>
		/// Creates the additive policy that preserves unrelated managed mods.
		/// </summary>
		public static CollectionExecutionPolicy InstallIntoCurrentSetup()
		{
			return new CollectionExecutionPolicy(CollectionExecutionPolicyKind.InstallIntoCurrentSetup,
				CollectionReplacementBackupChoice.NotApplicable);
		}

		/// <summary>
		/// Creates the replacement policy with its optional persistent-backup decision still unresolved.
		/// </summary>
		public static CollectionExecutionPolicy ReplaceCurrentManagedSetup()
		{
			return new CollectionExecutionPolicy(CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup,
				CollectionReplacementBackupChoice.Undecided);
		}

		/// <summary>
		/// Creates the replacement policy with an explicit persistent-backup decision.
		/// </summary>
		public static CollectionExecutionPolicy ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice backupChoice)
		{
			return new CollectionExecutionPolicy(CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup, backupChoice);
		}

		/// <summary>
		/// Gets the selected application policy.
		/// </summary>
		public CollectionExecutionPolicyKind Kind { get; }

		/// <summary>
		/// Gets the persistent Local Collection backup decision associated with replacement.
		/// </summary>
		public CollectionReplacementBackupChoice ReplacementBackupChoice { get; }

		/// <summary>
		/// Gets whether unrelated currently managed mods are preserved by default rather than treated as outgoing replacement state.
		/// </summary>
		/// <remarks>
		/// A false value does not mean that every unrelated mod must be removed. A later replacement plan may explicitly
		/// retain compatible items when the reviewed intended result and ownership remain correct.
		/// </remarks>
		public bool PreservesUnrelatedManagedModsByDefault
		{
			get { return Kind == CollectionExecutionPolicyKind.InstallIntoCurrentSetup; }
		}

		/// <summary>
		/// Gets whether the policy may remove outgoing NMM-managed effects as part of the reviewed transition.
		/// </summary>
		public bool MayRemoveOutgoingManagedEffects
		{
			get { return Kind == CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup; }
		}

		/// <summary>
		/// Gets whether the ordinary archive library must be preserved by this policy.
		/// </summary>
		public bool PreservesArchiveLibrary
		{
			get { return true; }
		}

		/// <summary>
		/// Gets whether unknown or unmanaged files must be preserved rather than recursively purged.
		/// </summary>
		public bool PreservesUnknownOrUnmanagedFiles
		{
			get { return true; }
		}

		/// <summary>
		/// Gets whether a reviewed outgoing removal/reconfiguration scope is required before destructive replacement mutation.
		/// </summary>
		public bool RequiresReviewedRemovalScope
		{
			get { return Kind == CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup; }
		}

		/// <summary>
		/// Gets whether the user must be offered an optional persistent Local Collection backup before destructive work.
		/// </summary>
		public bool OffersOptionalLocalCollectionBackup
		{
			get { return Kind == CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup; }
		}

		/// <summary>
		/// Gets whether installer conditions must be planned against the incoming intended environment rather than the outgoing setup.
		/// </summary>
		/// <remarks>
		/// The replacement intended environment includes selected incoming members and any explicitly retained items.
		/// </remarks>
		public bool UsesIncomingIntendedEnvironment
		{
			get { return Kind == CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup; }
		}

		/// <summary>
		/// Gets whether the replacement backup decision is sufficiently explicit to cross a later destructive boundary.
		/// </summary>
		/// <remarks>
		/// This is only one prerequisite for mutation. It does not imply that preflight, consent, recovery inputs or target
		/// authority have been satisfied.
		/// </remarks>
		public bool HasResolvedReplacementBackupDecision
		{
			get
			{
				return Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup ||
					ReplacementBackupChoice == CollectionReplacementBackupChoice.CreateLocalCollection ||
					ReplacementBackupChoice == CollectionReplacementBackupChoice.ContinueWithoutLocalCollection;
			}
		}

		/// <summary>
		/// Returns a replacement-policy snapshot with the user's persistent-backup decision changed.
		/// </summary>
		public CollectionExecutionPolicy WithReplacementBackupChoice(CollectionReplacementBackupChoice backupChoice)
		{
			if (Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
				throw new InvalidOperationException("Only replacement policy has a persistent Local Collection backup decision.");

			return ReplaceCurrentManagedSetup(backupChoice);
		}

		/// <inheritdoc />
		public bool Equals(CollectionExecutionPolicy other)
		{
			return !ReferenceEquals(other, null) && Kind == other.Kind && ReplacementBackupChoice == other.ReplacementBackupChoice;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionExecutionPolicy);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return ((int)Kind * 397) ^ (int)ReplacementBackupChoice;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			if (Kind == CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
				return Kind + ":" + ReplacementBackupChoice;
			return Kind.ToString();
		}
	}
}
