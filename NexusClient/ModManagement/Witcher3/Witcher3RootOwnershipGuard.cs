using System;
using System.IO;
using System.Linq;
using NativeInstallLog = Nexus.Client.ModManagement.InstallationLog.InstallLog;
using Nexus.Client.GameStorage;

namespace Nexus.Client.ModManagement.Witcher3
{
	/// <summary>
	/// Preflight for a future separately registered Witcher3Remastered ModeId. Never reads or
	/// replays another mode's InstallLog into the current mode, and never copies/modifies state.
	/// It must run before services and again after path setup. This is NOT a migration mechanism.
	/// </summary>
	public static class Witcher3RootOwnershipGuard
	{
		public const string LegacyModeId = "Witcher3";
		public const string RemasteredModeId = "Witcher3Remastered";

		public static bool IsWitcherMode(string modeId)
		{
			return String.Equals(modeId, LegacyModeId, StringComparison.OrdinalIgnoreCase) ||
				String.Equals(modeId, RemasteredModeId, StringComparison.OrdinalIgnoreCase);
		}

		public static string CounterpartModeId(string modeId)
		{
			if (String.Equals(modeId, LegacyModeId, StringComparison.OrdinalIgnoreCase)) return RemasteredModeId;
			if (String.Equals(modeId, RemasteredModeId, StringComparison.OrdinalIgnoreCase)) return LegacyModeId;
			throw new ArgumentException("A Witcher 3 mode ID is required.", nameof(modeId));
		}

		/// <summary>
		/// Checks the other mode's configured game root, InstallInfo store and independent persisted
		/// native footprints. An orphaned authority marker, interrupted migration or saved storage claim counts as evidence;
		/// the guard intentionally never calls InstallLog.Initialize or runs recovery.
		/// </summary>
		public static Witcher3RootOwnershipDecision Check(IEnvironmentInfo environment,
			string currentModeId, string currentGameRoot, string currentInstallInfo)
		{
			return CheckCore(environment, currentModeId, currentGameRoot, currentInstallInfo, false);
		}

		/// <summary>
		/// Stronger pre-mutation check. A harmless Legacy discovery does not grant the
		/// separately identified Remastered mode the right to write into that same root.
		/// Repeat after native authority reload under the native target lease, never
		/// substitute this read-only preflight for the lease or for native recovery.
		/// </summary>
		public static Witcher3RootOwnershipDecision CheckForNativeMutation(IEnvironmentInfo environment,
			string currentModeId, string currentGameRoot, string currentInstallInfo)
		{
			return CheckCore(environment, currentModeId, currentGameRoot, currentInstallInfo, true);
		}

		/// <summary>
		/// Validate the actual destination of a Game Storage reassignment, not the old
		/// VirtualFolder setting. The caller must provide the final VirtualInstall path.
		/// No directory or native metadata is created during this check.
		/// </summary>
		public static Witcher3RootOwnershipDecision CheckForStorageTransition(IEnvironmentInfo environment,
			string currentModeId, string proposedGameRoot, string proposedInstallInfo,
			string proposedVirtualInstall)
		{
			if (IsWitcherMode(currentModeId) && String.IsNullOrWhiteSpace(proposedVirtualInstall))
				return Unverifiable("The proposed Witcher 3 VirtualInstall path is missing.");
			return CheckCore(environment, currentModeId, proposedGameRoot, proposedInstallInfo,
				true, proposedVirtualInstall);
		}

		/// <summary>Rejects unsafe Witcher native mutation without touching any state.</summary>
		public static void RequireNativeMutation(IEnvironmentInfo environment, string currentModeId,
			string currentGameRoot, string currentInstallInfo)
		{
			if (!IsWitcherMode(currentModeId)) return;
			Witcher3RootOwnershipDecision decision = CheckForNativeMutation(environment,
				currentModeId, currentGameRoot, currentInstallInfo);
			if (!decision.AllowsNativeMutation)
				throw new InvalidDataException("Witcher 3 native mutation blocked (" + decision.Status + "): " +
					decision.Reason + " No ownership migration is performed automatically.");
		}

		private static Witcher3RootOwnershipDecision CheckCore(IEnvironmentInfo environment,
			string currentModeId, string currentGameRoot, string currentInstallInfo,
			bool forNativeMutation, string proposedVirtualInstall = null)
		{
			if (environment == null) throw new ArgumentNullException(nameof(environment));
			if (!IsWitcherMode(currentModeId))
				return Witcher3RootOwnershipPolicy.Evaluate(null, null, null, null, false, false);

			string otherMode = CounterpartModeId(currentModeId);
			var settings = environment.Settings;
			string otherRoot = settings.InstallationPaths[otherMode];
			string otherInfo = settings.InstallInfoFolder[otherMode];
			string otherVirtualSetting = settings.VirtualFolder[otherMode];
			bool completed = settings.CompletedSetup[otherMode];
			var storage = new GameStorageService(environment);
			bool footprint;
			try
			{
				footprint = HasNativeStoreFootprint(otherInfo) ||
					HasVirtualInstallFootprint(otherVirtualSetting, environment) ||
					storage.HasKnownNativeOwnershipEvidence(otherMode);
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is System.Security.SecurityException || error is ArgumentException ||
				error is NotSupportedException)
			{
				return Unverifiable("Cannot inspect the other Witcher 3 native store: " + error.Message);
			}

			bool hasState = completed || footprint;
			bool configured = hasState || !String.IsNullOrWhiteSpace(otherRoot) ||
				!String.IsNullOrWhiteSpace(otherInfo) || !String.IsNullOrWhiteSpace(otherVirtualSetting);
			if (!configured)
				return Witcher3RootOwnershipPolicy.Evaluate(null, null, null, null, false, false);
			if (hasState && String.IsNullOrWhiteSpace(otherInfo))
				return Unverifiable("The other Witcher 3 mode has native state, but its InstallInfo directory is not configured.");

			try
			{
				string currentPhysical = ResolveOptional(currentGameRoot, true);
				string otherPhysical = ResolveOptional(otherRoot, hasState);
				string currentInfoPhysical = ResolveOptional(currentInstallInfo, false);
				string otherInfoPhysical = ResolveOptional(otherInfo, false);
				Witcher3RootOwnershipDecision rootDecision = forNativeMutation
					? Witcher3RootOwnershipPolicy.EvaluateNativeMutation(currentPhysical, otherPhysical,
						currentInfoPhysical, otherInfoPhysical, hasState, configured,
						String.Equals(currentModeId, RemasteredModeId, StringComparison.OrdinalIgnoreCase))
					: Witcher3RootOwnershipPolicy.Evaluate(currentPhysical, otherPhysical,
						currentInfoPhysical, otherInfoPhysical, hasState, configured);
				if (!forNativeMutation || !rootDecision.AllowsNativeMutation)
					return rootDecision;

				// Never infer physical VirtualInstall identity from a simple path string: two
				// different parents may contain junctions, aliases or projected missing children.
				string currentVirtual = proposedVirtualInstall == null
					? ResolveVirtual(storage, settings.VirtualFolder[currentModeId])
					: ResolveVirtualPath(proposedVirtualInstall);
				string otherVirtual = ResolveVirtual(storage, otherVirtualSetting);
				Witcher3RootOwnershipDecision storageDecision =
					Witcher3RootOwnershipPolicy.EvaluateNativeStorage(currentVirtual,
						otherVirtual, currentInfoPhysical, otherInfoPhysical);
				if (!storageDecision.AllowsNativeMutation)
					return storageDecision;

				// Historical claimed stores remain protected even when the other mode's
				// current settings point somewhere else. Only real native-state claims
				// are considered; download-only locations do not reserve VirtualInstall.
				foreach (GameStorageNativeOwnershipLocation claim in storage.GetKnownNativeOwnershipLocations(otherMode))
				{
					if ((claim.HadVirtualPayload && String.IsNullOrWhiteSpace(claim.VirtualInstallPath)) ||
						(claim.HadInstallLog && String.IsNullOrWhiteSpace(claim.InstallInfoPath)))
						return Unverifiable("A previous Witcher 3 native owner has an incomplete storage-location record.");
					string historicalInfo = ResolveOptional(claim.InstallInfoPath, false);
					Witcher3RootOwnershipDecision historicalInfoDecision =
						Witcher3RootOwnershipPolicy.EvaluateHistoricalInstallInfo(currentInfoPhysical, historicalInfo);
					if (!historicalInfoDecision.AllowsNativeMutation)
						return historicalInfoDecision;
					storageDecision = Witcher3RootOwnershipPolicy.EvaluateNativeStorage(currentVirtual,
						ResolveVirtualPath(claim.VirtualInstallPath), currentInfoPhysical, historicalInfo);
					if (!storageDecision.AllowsNativeMutation)
						return storageDecision;
				}
				return rootDecision;
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is System.Security.SecurityException || error is ArgumentException || error is NotSupportedException ||
				error is System.ComponentModel.Win32Exception || error is System.Runtime.InteropServices.ExternalException ||
				error is DllNotFoundException || error is EntryPointNotFoundException)
			{
				return Unverifiable("Cannot establish independent physical paths for the Witcher 3 modes: " + error.Message);
			}
		}

		/// <summary>Only the existence of a Virtual payload entry is examined; no recursive enumeration.</summary>
		public static bool HasVirtualInstallFootprint(string virtualRoot, IEnvironmentInfo environment)
		{
			if (String.IsNullOrWhiteSpace(virtualRoot)) return false;
			if (environment == null) throw new ArgumentNullException(nameof(environment));
			string directory = new GameStorageService(environment).NormalizeVirtualInstallDirectory(virtualRoot);
			return Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
		}

		private static string ResolveVirtual(GameStorageService service, string setting)
		{
			if (String.IsNullOrWhiteSpace(setting)) return null;
			if (!Path.IsPathRooted(setting))
				throw new ArgumentException("The Witcher 3 VirtualInstall parent must be absolute.", nameof(setting));
			return ResolveVirtualPath(service.NormalizeVirtualInstallDirectory(setting));
		}

		private static string ResolveVirtualPath(string path)
		{
			return ResolveOptional(path, false);
		}

		private static string ResolveOptional(string path, bool requireExisting)
		{
			return String.IsNullOrWhiteSpace(path) ? null : Witcher3PhysicalPathIdentity.Resolve(path, requireExisting);
		}

		private static Witcher3RootOwnershipDecision Unverifiable(string message)
		{
			return new Witcher3RootOwnershipDecision(Witcher3RootOwnershipStatus.UnverifiablePriorOwner, message);
		}

		/// <summary>
		/// Conservatively detects SQLite, legacy XML, authority markers, incomplete migrations and
		/// native recovery files. Presence does NOT prove there are mods; it prevents guessing.
		/// </summary>
		public static bool HasNativeStoreFootprint(string installInfoDirectory)
		{
			if (String.IsNullOrWhiteSpace(installInfoDirectory)) return false;
			if (!Path.IsPathRooted(installInfoDirectory))
				throw new ArgumentException("The other mode's InstallInfo path is not absolute.", nameof(installInfoDirectory));
			string file = Path.Combine(Path.GetFullPath(installInfoDirectory), "InstallLog.xml");
			if (NativeInstallLog.HasPersistedLog(file)) return true;
			string sqlite = Path.ChangeExtension(file, ".sqlite");
			// Interrupted SQLite publishes, journal remnants and backups must not be ignored.
			if (File.Exists(file + ".pre-sqlite") || File.Exists(sqlite + "-wal") ||
				File.Exists(sqlite + "-shm") || File.Exists(sqlite + "-journal") ||
				File.Exists(sqlite + ".authority")) return true;
			for (int i = 0; i < 6; i++)
				if (File.Exists(sqlite + ".bak" + (i == 0 ? "" : i.ToString()))) return true;
			return false;
		}
	}
}
