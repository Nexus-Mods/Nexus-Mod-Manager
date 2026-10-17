using System;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>W3R.5: the decision is about PHYSICAL game roots, not Nexus domains or ModeIds.</summary>
	public enum Witcher3RootOwnershipStatus
	{
		Allowed = 0,
		SharedManagedGameRoot = 1,
		OverlappingManagedGameRoots = 2,
		SharedInstallInfo = 3,
		UnverifiablePriorOwner = 4
	}

	/// <summary>A read-only preflight result. Allowed never grants permission to bypass native ownership checks.</summary>
	public sealed class Witcher3RootOwnershipDecision
	{
		internal Witcher3RootOwnershipDecision(Witcher3RootOwnershipStatus status, string reason)
		{
			Status = status;
			Reason = reason;
		}

		public Witcher3RootOwnershipStatus Status { get; }
		public bool AllowsNormalInitialization => Status == Witcher3RootOwnershipStatus.Allowed;
		public string Reason { get; }
	}

	/// <summary>
	/// A pure, side-effect-free policy. Identity arguments MUST already represent resolved physical
	/// directories (not just Path.GetFullPath values). Do not use this as an ownership migration tool.
	/// A mere discovery of another edition must not disable a previously working Witcher installation;
	/// a completed setup or a native footprint is a stronger claim that requires explicit migration.
	/// </summary>
	public static class Witcher3RootOwnershipPolicy
	{
		public static Witcher3RootOwnershipDecision Evaluate(string currentPhysicalRoot,
			string otherPhysicalRoot, string currentPhysicalInstallInfo, string otherPhysicalInstallInfo,
			bool otherModeHasPriorManagementState, bool otherModeIsConfigured)
		{
			if (!otherModeIsConfigured && !otherModeHasPriorManagementState)
				return Decision(Witcher3RootOwnershipStatus.Allowed,
					"No other Witcher 3 mode is configured or has native management state.");

			if (String.IsNullOrWhiteSpace(currentPhysicalRoot) ||
				(otherModeHasPriorManagementState && String.IsNullOrWhiteSpace(otherPhysicalRoot)))
				return Decision(Witcher3RootOwnershipStatus.UnverifiablePriorOwner,
					"Cannot verify the physical game directories while another Witcher 3 mode has managed state.");

			// A shared InstallInfo directory is unsafe EVEN if two physical game installations differ:
			// InstallLog, recovery metadata and backup ownership would be shared accidentally.
			if (!String.IsNullOrWhiteSpace(currentPhysicalInstallInfo) &&
				!String.IsNullOrWhiteSpace(otherPhysicalInstallInfo) &&
				Equal(currentPhysicalInstallInfo, otherPhysicalInstallInfo))
				return Decision(Witcher3RootOwnershipStatus.SharedInstallInfo,
					"The two Witcher 3 game modes use the same physical InstallInfo directory.");

			if (String.IsNullOrWhiteSpace(otherPhysicalRoot))
				return Decision(Witcher3RootOwnershipStatus.Allowed,
					"The other Witcher 3 mode has no established physical game directory or managed state.");

			if (Equal(currentPhysicalRoot, otherPhysicalRoot))
				return otherModeHasPriorManagementState
					? Decision(Witcher3RootOwnershipStatus.SharedManagedGameRoot,
						"Another Witcher 3 mode already has native management state for this physical game directory.")
					: Decision(Witcher3RootOwnershipStatus.Allowed,
						"The other edition has only been discovered; it has no native management state.");

			if (Contains(currentPhysicalRoot, otherPhysicalRoot) || Contains(otherPhysicalRoot, currentPhysicalRoot))
				return otherModeHasPriorManagementState
					? Decision(Witcher3RootOwnershipStatus.OverlappingManagedGameRoots,
						"Another managed Witcher 3 root is nested inside this one (or vice versa).")
					: Decision(Witcher3RootOwnershipStatus.Allowed,
						"Nested game roots have no known second native owner yet.");

			return Decision(Witcher3RootOwnershipStatus.Allowed,
				"The two Witcher 3 modes resolve to distinct physical game and InstallInfo directories.");
		}

		private static bool Equal(string left, string right)
		{
			return String.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
		}

		private static bool Contains(string parent, string child)
		{
			string prefix = Normalize(parent);
			if (!prefix.EndsWith("\\", StringComparison.Ordinal)) prefix += "\\";
			return Normalize(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
		}

		private static string Normalize(string identity)
		{
			return identity.Trim().Replace('/', '\\').TrimEnd('\\');
		}

		private static Witcher3RootOwnershipDecision Decision(Witcher3RootOwnershipStatus status, string reason)
		{
			return new Witcher3RootOwnershipDecision(status, reason);
		}
	}
}
