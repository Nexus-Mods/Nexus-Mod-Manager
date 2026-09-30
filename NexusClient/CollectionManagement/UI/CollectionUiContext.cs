using System;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Identifies the user-facing scope that owns one Collections UI action or presentation.</summary>
	internal enum CollectionUiContextKind
	{
		None = 0,
		CurrentSetup = 1,
		IncomingCollection = 2,
		InstalledCollection = 3,
		SavedLocalCollection = 4
	}

	/// <summary>
	/// Immutable UI ownership token used to prevent asynchronous Collections work from repainting a newer context.
	/// </summary>
	/// <remarks>
	/// <see cref="Generation"/> identifies the presentation generation. Exact revision/association/capture/operation
	/// identities are retained when they are known so a command never has to infer its target from mutable controls.
	/// </remarks>
	internal sealed class CollectionUiContext
	{
		private CollectionUiContext(int generation, CollectionUiContextKind kind, CollectionRevisionIdentity revision,
			LocalCaptureIdentity localCapture, Guid? associationId, CollectionOperationIdentity operation)
		{
			if (generation < 0)
				throw new ArgumentOutOfRangeException(nameof(generation));
			if (!Enum.IsDefined(typeof(CollectionUiContextKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));

			if (associationId.HasValue && associationId.Value == Guid.Empty)
				throw new ArgumentException("An installed Collection UI context requires a non-empty association identifier.", nameof(associationId));

			switch (kind)
			{
				case CollectionUiContextKind.None:
				case CollectionUiContextKind.CurrentSetup:
					if (revision != null || localCapture != null || associationId.HasValue || operation != null)
						throw new ArgumentException("This UI context kind cannot carry Collection-specific identity.", nameof(kind));
					break;
				case CollectionUiContextKind.IncomingCollection:
					if (localCapture != null || associationId.HasValue)
						throw new ArgumentException("An incoming Collection UI context cannot target a Local capture or installed association.", nameof(kind));
					if (revision != null && revision.Collection.Origin != CollectionOrigin.NexusMods)
						throw new ArgumentException("An incoming Collection UI context requires a Nexus revision identity when a revision is known.", nameof(revision));
					break;
				case CollectionUiContextKind.InstalledCollection:
					if (revision == null)
						throw new ArgumentNullException(nameof(revision));
					if (!associationId.HasValue)
						throw new ArgumentException("An installed Collection UI context requires an association identifier.", nameof(associationId));
					if (localCapture != null)
						throw new ArgumentException("An installed Collection UI context cannot target a Local capture.", nameof(localCapture));
					break;
				case CollectionUiContextKind.SavedLocalCollection:
					if (revision == null)
						throw new ArgumentNullException(nameof(revision));
					if (revision.Collection.Origin != CollectionOrigin.Local)
						throw new ArgumentException("A saved Local Collection UI context requires a Local revision identity.", nameof(revision));
					if (localCapture == null)
						throw new ArgumentNullException(nameof(localCapture));
					if (associationId.HasValue)
						throw new ArgumentException("A saved Local Collection UI context cannot target an installed association.", nameof(associationId));
					break;
			}

			Generation = generation;
			Kind = kind;
			Revision = revision;
			LocalCapture = localCapture;
			AssociationId = associationId;
			Operation = operation;
		}

		public int Generation { get; }
		public CollectionUiContextKind Kind { get; }
		public CollectionRevisionIdentity Revision { get; }
		public LocalCaptureIdentity LocalCapture { get; }
		public Guid? AssociationId { get; }
		public CollectionOperationIdentity Operation { get; }

		public static CollectionUiContext None(int generation)
		{
			return new CollectionUiContext(generation, CollectionUiContextKind.None, null, null, null, null);
		}

		public static CollectionUiContext CurrentSetup(int generation)
		{
			return new CollectionUiContext(generation, CollectionUiContextKind.CurrentSetup, null, null, null, null);
		}

		public static CollectionUiContext Incoming(int generation, CollectionRevisionIdentity revision, CollectionOperationIdentity operation)
		{
			return new CollectionUiContext(generation, CollectionUiContextKind.IncomingCollection, revision, null, null, operation);
		}

		public static CollectionUiContext Installed(int generation, CollectionRevisionIdentity revision, Guid associationId)
		{
			return new CollectionUiContext(generation, CollectionUiContextKind.InstalledCollection, revision, null, associationId, null);
		}

		public static CollectionUiContext SavedLocal(int generation, CollectionRevisionIdentity revision, LocalCaptureIdentity localCapture)
		{
			return new CollectionUiContext(generation, CollectionUiContextKind.SavedLocalCollection, revision, localCapture, null, null);
		}

		public CollectionUiContext WithRevision(CollectionRevisionIdentity revision)
		{
			if (Kind != CollectionUiContextKind.IncomingCollection)
				throw new InvalidOperationException("Only an incoming Collection UI context can acquire a resolved revision identity.");
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			return Incoming(Generation, revision, Operation);
		}

		public CollectionUiContext WithOperation(CollectionOperationIdentity operation)
		{
			if (Kind == CollectionUiContextKind.None || Kind == CollectionUiContextKind.CurrentSetup)
				throw new InvalidOperationException("This UI context kind does not own a Collection operation.");
			return new CollectionUiContext(Generation, Kind, Revision, LocalCapture, AssociationId, operation);
		}

		public bool IsCurrentGeneration(int generation)
		{
			return Generation == generation;
		}

		public override string ToString()
		{
			string identity = Revision == null ? string.Empty : " / " + Revision;
			if (AssociationId.HasValue)
				identity += " / association:" + AssociationId.Value.ToString("D");
			if (LocalCapture != null)
				identity += " / capture:" + LocalCapture;
			if (Operation != null)
				identity += " / operation:" + Operation;
			return Kind + " / generation:" + Generation + identity;
		}
	}
}
