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
			if (environment == null) throw new ArgumentNullException(nameof(environment));
			if (!IsWitcherMode(currentModeId))
				return Witcher3RootOwnershipPolicy.Evaluate(null, null, null, null, false, false);

			string otherMode = CounterpartModeId(currentModeId);
			var settings = environment.Settings;
			string otherRoot = settings.InstallationPaths[otherMode];
			string otherInfo = settings.InstallInfoFolder[otherMode];
			bool completed = settings.CompletedSetup[otherMode];
			bool footprint;
			try
			{
				footprint = HasNativeStoreFootprint(otherInfo) ||
					HasVirtualInstallFootprint(environment.Settings.VirtualFolder[otherMode], environment) ||
					new GameStorageService(environment).HasKnownNativeOwnershipEvidence(otherMode);
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is System.Security.SecurityException || error is ArgumentException ||
				error is NotSupportedException)
			{
				return Unverifiable("Cannot inspect the other Witcher 3 native store: " + error.Message);
			}

			bool hasState = completed || footprint;
			bool configured = hasState || !String.IsNullOrWhiteSpace(otherRoot) || !String.IsNullOrWhiteSpace(otherInfo);
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
				return Witcher3RootOwnershipPolicy.Evaluate(currentPhysical, otherPhysical,
					currentInfoPhysical, otherInfoPhysical, hasState, configured);
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
