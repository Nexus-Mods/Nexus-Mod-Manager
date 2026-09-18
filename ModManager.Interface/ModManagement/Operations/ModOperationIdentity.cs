using System;

namespace Nexus.Client.ModManagement.Operations
{
	/// <summary>
	/// Identifies one logical native mod operation and one concrete execution attempt of that operation.
	/// </summary>
	public sealed class ModOperationIdentity
	{
		/// <summary>
		/// Initializes a native mod-operation identity.
		/// </summary>
		/// <param name="operationId">The stable logical operation identifier.</param>
		/// <param name="attemptId">The identifier of this concrete execution attempt.</param>
		/// <param name="origin">The explicit workflow that requested the operation.</param>
		/// <param name="fingerprint">The immutable requested target/context/recipe identity.</param>
		public ModOperationIdentity(Guid operationId, Guid attemptId, ModOperationOrigin origin, ModOperationFingerprint fingerprint)
		{
			if (operationId == Guid.Empty)
				throw new ArgumentException("An operation identifier is required.", nameof(operationId));
			if (attemptId == Guid.Empty)
				throw new ArgumentException("An attempt identifier is required.", nameof(attemptId));
			if (!Enum.IsDefined(typeof(ModOperationOrigin), origin) || origin == ModOperationOrigin.Unknown)
				throw new ArgumentOutOfRangeException(nameof(origin));
			if (fingerprint == null)
				throw new ArgumentNullException(nameof(fingerprint));

			OperationId = operationId;
			AttemptId = attemptId;
			Origin = origin;
			Fingerprint = fingerprint;
		}

		/// <summary>
		/// Gets the stable identifier of the logical operation across retries or recovery attempts.
		/// </summary>
		public Guid OperationId { get; }

		/// <summary>
		/// Gets the identifier of this specific execution attempt.
		/// </summary>
		public Guid AttemptId { get; }

		/// <summary>
		/// Gets the explicit workflow origin.
		/// </summary>
		public ModOperationOrigin Origin { get; }

		/// <summary>
		/// Gets the immutable requested target/context/recipe identity.
		/// </summary>
		public ModOperationFingerprint Fingerprint { get; }

		/// <summary>
		/// Creates a new logical operation and its first execution attempt.
		/// </summary>
		/// <param name="origin">The explicit workflow that requested the operation.</param>
		/// <param name="fingerprint">The immutable requested target/context/recipe identity.</param>
		/// <returns>The new operation identity.</returns>
		public static ModOperationIdentity CreateNew(ModOperationOrigin origin, ModOperationFingerprint fingerprint)
		{
			return new ModOperationIdentity(Guid.NewGuid(), Guid.NewGuid(), origin, fingerprint);
		}

		/// <summary>
		/// Creates a new execution attempt for the same logical operation.
		/// </summary>
		/// <returns>An identity with the same operation, origin and fingerprint and a new attempt identifier.</returns>
		public ModOperationIdentity CreateNextAttempt()
		{
			return new ModOperationIdentity(OperationId, Guid.NewGuid(), Origin, Fingerprint);
		}
	}
}
