using System;
using Nexus.Client.ModAuthoring;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Defines how one Collection acquisition batch resolves collisions with existing NMM mod archives.</summary>
	/// <remarks>
	/// The policy is intentionally in-memory and operation scoped. It is not persisted as blanket consent and does not affect
	/// ordinary Add Mod calls or native installation file-conflict handling.
	/// </remarks>
	public sealed class CollectionArchiveOverwritePolicy : IEquatable<CollectionArchiveOverwritePolicy>
	{
		private CollectionArchiveOverwritePolicy(CollectionArchiveOverwriteMode mode)
		{
			Mode = mode;
		}

		/// <summary>Preserves the normal per-archive overwrite/keep-both/cancel prompt.</summary>
		public static CollectionArchiveOverwritePolicy Prompt { get; } =
			new CollectionArchiveOverwritePolicy(CollectionArchiveOverwriteMode.Prompt);

		/// <summary>Uses the original archive destination without prompting when that destination already exists.</summary>
		public static CollectionArchiveOverwritePolicy OverwriteExistingArchives { get; } =
			new CollectionArchiveOverwritePolicy(CollectionArchiveOverwriteMode.OverwriteExistingArchives);

		/// <summary>Gets the immutable policy mode.</summary>
		public CollectionArchiveOverwriteMode Mode { get; }

		/// <summary>Gets whether existing Collection-correlated archive destinations should be replaced automatically.</summary>
		public bool AutomaticallyOverwritesExistingArchives
		{
			get { return Mode == CollectionArchiveOverwriteMode.OverwriteExistingArchives; }
		}

		/// <summary>Creates the callback captured by one acquisition batch.</summary>
		/// <remarks>
		/// Automatic mode never searches for an alternate filename and never consults mutable UI state after the batch starts.
		/// </remarks>
		public ConfirmOverwriteCallback Bind(ConfirmOverwriteCallback promptCallback)
		{
			if (!AutomaticallyOverwritesExistingArchives)
				return promptCallback;

			return AcceptOriginalDestination;
		}

		private static bool AcceptOriginalDestination(string oldPath, out string newPath)
		{
			newPath = oldPath;
			return true;
		}

		public bool Equals(CollectionArchiveOverwritePolicy other)
		{
			return other != null && other.Mode == Mode;
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionArchiveOverwritePolicy);
		}

		public override int GetHashCode()
		{
			return (int)Mode;
		}

		public override string ToString()
		{
			return Mode.ToString();
		}
	}

	/// <summary>Supported Collection archive-collision modes.</summary>
	public enum CollectionArchiveOverwriteMode
	{
		Prompt = 0,
		OverwriteExistingArchives = 1
	}

	/// <summary>Raised when a later Collection consumer requests a different overwrite policy from an active shared producer.</summary>
	public sealed class CollectionArchiveOverwritePolicyConflictException : InvalidOperationException
	{
		public CollectionArchiveOverwritePolicyConflictException(CollectionArchiveOverwritePolicy activePolicy,
			CollectionArchiveOverwritePolicy requestedPolicy)
			: base(BuildMessage(activePolicy, requestedPolicy))
		{
			ActivePolicy = activePolicy ?? throw new ArgumentNullException(nameof(activePolicy));
			RequestedPolicy = requestedPolicy ?? throw new ArgumentNullException(nameof(requestedPolicy));
		}

		public CollectionArchiveOverwritePolicy ActivePolicy { get; }
		public CollectionArchiveOverwritePolicy RequestedPolicy { get; }

		private static string BuildMessage(CollectionArchiveOverwritePolicy activePolicy,
			CollectionArchiveOverwritePolicy requestedPolicy)
		{
			if (activePolicy == null) throw new ArgumentNullException(nameof(activePolicy));
			if (requestedPolicy == null) throw new ArgumentNullException(nameof(requestedPolicy));
			return "A shared Collection archive acquisition is already running with archive overwrite policy '" +
				activePolicy + "'. The new consumer requested '" + requestedPolicy +
				"'. Wait for the current acquisition to finish, then prepare again so NMM does not silently change or inherit overwrite consent.";
		}
	}
}
