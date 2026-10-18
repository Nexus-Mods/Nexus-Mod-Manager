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
		UnverifiablePriorOwner = 4,
		// Write-time only. Discovery must not invalidate an existing working Legacy setup.
		SharedConfiguredGameRoot = 5,
		OverlappingConfiguredGameRoots = 6,
		SharedVirtualInstall = 7,
		OverlappingVirtualInstall = 8,
		OverlappingNativeStorageRoles = 9,
		OverlappingHistoricalInstallInfo = 10,
		OverlappingInstallInfo = 11
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
		public bool AllowsNativeMutation => Status == Witcher3RootOwnershipStatus.Allowed;
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

		/// <summary>
		/// Evaluates a prospective native write separately from harmless game discovery.
		/// If an unowned Legacy mode merely points to the Remastered target, startup may
		/// still open it for inspection; a new Remastered owner must not claim that root.
		/// Legacy remains usable with an unowned discovered Remastered mode, but cannot
		/// write once the Remastered mode has an actual independent native owner.
		/// Paths are already resolved physical identities, as in Evaluate.
		/// </summary>
		public static Witcher3RootOwnershipDecision EvaluateNativeMutation(string currentPhysicalRoot,
			string otherPhysicalRoot, string currentPhysicalInstallInfo, string otherPhysicalInstallInfo,
			bool otherModeHasPriorManagementState, bool otherModeIsConfigured, bool currentIsRemastered)
		{
			Witcher3RootOwnershipDecision baseDecision = Evaluate(currentPhysicalRoot, otherPhysicalRoot,
				currentPhysicalInstallInfo, otherPhysicalInstallInfo,
				otherModeHasPriorManagementState, otherModeIsConfigured);
			if (!baseDecision.AllowsNormalInitialization || !currentIsRemastered || !otherModeIsConfigured ||
				String.IsNullOrWhiteSpace(otherPhysicalRoot))
				return baseDecision;

			if (Equal(currentPhysicalRoot, otherPhysicalRoot))
				return Decision(Witcher3RootOwnershipStatus.SharedConfiguredGameRoot,
					"The Legacy Witcher 3 mode is configured for this physical game directory. " +
					"Remastered cannot establish a second native owner even though Legacy has no recorded installed mods.");
			if (Contains(currentPhysicalRoot, otherPhysicalRoot) || Contains(otherPhysicalRoot, currentPhysicalRoot))
				return Decision(Witcher3RootOwnershipStatus.OverlappingConfiguredGameRoots,
					"The Legacy and Remastered game directories physically overlap. " +
					"A new Remastered owner requires an independent game installation.");
			return baseDecision;
		}

		/// <summary>
		/// Write-only authority guard for the *actual* VirtualInstall directories, not their
		/// legacy parent settings. Inputs are resolved physical filesystem identities.
		/// Two Witcher modes are not registered as compatible shared Virtual storage owners.
		/// Also reject nesting across the InstallInfo and VirtualInstall native roles.
		/// </summary>
		public static Witcher3RootOwnershipDecision EvaluateNativeStorage(string currentVirtual,
			string otherVirtual, string currentInfo, string otherInfo)
		{
			if (!String.IsNullOrWhiteSpace(otherVirtual) && String.IsNullOrWhiteSpace(currentVirtual))
				return Decision(Witcher3RootOwnershipStatus.UnverifiablePriorOwner,
					"Cannot verify this mode's VirtualInstall directory against another Witcher owner's storage.");
			if (!String.IsNullOrWhiteSpace(currentVirtual) && !String.IsNullOrWhiteSpace(otherVirtual))
			{
				if (Equal(currentVirtual, otherVirtual))
					return Decision(Witcher3RootOwnershipStatus.SharedVirtualInstall,
						"Both Witcher 3 modes resolve to the same physical VirtualInstall directory.");
				if (Contains(currentVirtual, otherVirtual) || Contains(otherVirtual, currentVirtual))
					return Decision(Witcher3RootOwnershipStatus.OverlappingVirtualInstall,
						"The Witcher 3 VirtualInstall directories physically overlap.");
			}
			if (!String.IsNullOrWhiteSpace(currentInfo) && !String.IsNullOrWhiteSpace(otherInfo) &&
				(Contains(currentInfo, otherInfo) || Contains(otherInfo, currentInfo)))
				return Decision(Witcher3RootOwnershipStatus.OverlappingInstallInfo,
					"The two Witcher 3 InstallInfo stores physically overlap.");
			if (Overlap(currentVirtual, otherInfo) || Overlap(currentInfo, otherVirtual))
				return Decision(Witcher3RootOwnershipStatus.OverlappingNativeStorageRoles,
					"A Witcher 3 VirtualInstall directory overlaps the other mode's native InstallInfo store.");
			return Decision(Witcher3RootOwnershipStatus.Allowed,
				"The observed Witcher 3 native storage directories are independent.");
		}

		/// <summary>Protect historical InstallInfo paths after the counterpart settings have moved.</summary>
		public static Witcher3RootOwnershipDecision EvaluateHistoricalInstallInfo(string currentInfo,
			string historicalInfo)
		{
			if (String.IsNullOrWhiteSpace(currentInfo) || String.IsNullOrWhiteSpace(historicalInfo))
				return Decision(Witcher3RootOwnershipStatus.Allowed, "No historical InstallInfo collision observed.");
			if (Equal(currentInfo, historicalInfo))
				return Decision(Witcher3RootOwnershipStatus.SharedInstallInfo,
					"The current InstallInfo directory belongs to a historical Witcher native owner.");
			if (Contains(currentInfo, historicalInfo) || Contains(historicalInfo, currentInfo))
				return Decision(Witcher3RootOwnershipStatus.OverlappingHistoricalInstallInfo,
					"The current InstallInfo directory overlaps a historical Witcher native owner.");
			return Decision(Witcher3RootOwnershipStatus.Allowed, "The historical InstallInfo directories are independent.");
		}

		private static bool Overlap(string left, string right)
		{
			return !String.IsNullOrWhiteSpace(left) && !String.IsNullOrWhiteSpace(right) &&
				(Equal(left, right) || Contains(left, right) || Contains(right, left));
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
