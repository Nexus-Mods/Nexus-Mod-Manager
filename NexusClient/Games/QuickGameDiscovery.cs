using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using Nexus.Client.Games.DataDriven;

namespace Nexus.Client.Games
{
	/// <summary>
	/// Only bounded launcher/registry lookups. Recursive FileSearcher/GameDiscoverer is
	/// deliberately absent: whole-drive scans require a separate explicit UI command.
	/// This service does not write InstalledGames or any configuration paths.
	/// </summary>
	public sealed class QuickGameDiscovery
	{
		private readonly Func<GameStoreInstallInfo, string> _findStorePath;
		private readonly Func<IGameModeFactory, string> _findLegacyPath;

		// These legacy factory getters were inspected in the supplied baseline. They
		// perform Steam/registry/settings lookups, not recursive game discovery.
		// Newly added factories must be reviewed before being opted in.
		private static readonly HashSet<string> AuditedLegacyFactories = new HashSet<string>(StringComparer.Ordinal)
		{
			"Nexus.Client.Games.BaldursGate3.BaldursGate3GameModeFactory",
			"Nexus.Client.Games.BreakingWheel.BreakingWheelGameModeFactory",
			"Nexus.Client.Games.Cyberpunk2077.Cyberpunk2077GameModeFactory",
			"Nexus.Client.Games.DarkSouls.DarkSoulsGameModeFactory",
			"Nexus.Client.Games.DarkSouls2.DarkSouls2GameModeFactory",
			"Nexus.Client.Games.DragonAge.DragonAgeGameModeFactory",
			"Nexus.Client.Games.DragonAge2.DragonAge2GameModeFactory",
			"Nexus.Client.Games.DragonsDogma.DragonsDogmaGameModeFactory",
			"Nexus.Client.Games.Enderal.EnderalGameModeFactory",
			"Nexus.Client.Games.EnderalSE.EnderalSEGameModeFactory",
			"Nexus.Client.Games.Fallout3.Fallout3GameModeFactory",
			"Nexus.Client.Games.Fallout4.Fallout4GameModeFactory",
			"Nexus.Client.Games.Fallout4VR.Fallout4VRGameModeFactory",
			"Nexus.Client.Games.FalloutNV.FalloutNVGameModeFactory",
			"Nexus.Client.Games.Grimrock.GrimrockGameModeFactory",
			"Nexus.Client.Games.HogwartsLegacy.HogwartsLegacyGameModeFactory",
			"Nexus.Client.Games.MonsterHunterWorld.MonsterHunterWorldGameModeFactory",
			"Nexus.Client.Games.Morrowind.MorrowindGameModeFactory",
			"Nexus.Client.Games.MountAndBlade2Bannerlord.MountAndBlade2BannerlordGameModeFactory",
			"Nexus.Client.Games.NoMansSky.NoMansSkyGameModeFactory",
			"Nexus.Client.Games.Oblivion.OblivionGameModeFactory",
			"Nexus.Client.Games.OblivionRemastered.OblivionRemasteredGameModeFactory",
			"Nexus.Client.Games.Sims4.Sims4GameModeFactory",
			"Nexus.Client.Games.Skyrim.SkyrimGameModeFactory",
			"Nexus.Client.Games.SkyrimGOG.SkyrimGOGGameModeFactory",
			"Nexus.Client.Games.SkyrimSE.SkyrimSEGameModeFactory",
			"Nexus.Client.Games.SkyrimVR.SkyrimVRGameModeFactory",
			"Nexus.Client.Games.Starbound.StarboundGameModeFactory",
			"Nexus.Client.Games.StardewValley.StardewValleyGameModeFactory",
			"Nexus.Client.Games.Starfield.StarfieldGameModeFactory",
			"Nexus.Client.Games.StateOfDecay.StateOfDecayGameModeFactory",
			"Nexus.Client.Games.Subnautica.SubnauticaGameModeFactory",
			"Nexus.Client.Games.SubnauticaBelowZero.SubnauticaBelowZeroGameModeFactory",
			"Nexus.Client.Games.TESO.TESOGameModeFactory",
			"Nexus.Client.Games.WarThunder.WarThunderGameModeFactory",
			"Nexus.Client.Games.Witcher2.Witcher2GameModeFactory",
			"Nexus.Client.Games.Witcher3.Witcher3GameModeFactory",
			"Nexus.Client.Games.WorldOfTanks.WoTGameModeFactory",
			"Nexus.Client.Games.XCOM2.XCOM2GameModeFactory",
			"Nexus.Client.Games.XRebirth.XRebirthGameModeFactory"
		};

		public QuickGameDiscovery()
			: this(GameStoreInstallationPathDetector.Instance.GetInstallationPath, factory => factory.GetInstallationPath())
		{
		}

		internal QuickGameDiscovery(Func<GameStoreInstallInfo, string> findStorePath,
			Func<IGameModeFactory, string> findLegacyPath)
		{
			_findStorePath = findStorePath ?? throw new ArgumentNullException(nameof(findStorePath));
			_findLegacyPath = findLegacyPath ?? throw new ArgumentNullException(nameof(findLegacyPath));
		}

		public IReadOnlyList<GameDiscoveryInstallationCandidate> Find(
			IEnumerable<IGameModeFactory> factories, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (factories == null) throw new ArgumentNullException(nameof(factories));
			var candidates = new List<GameDiscoveryInstallationCandidate>();
			foreach (IGameModeFactory factory in factories)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (factory?.GameModeDescriptor == null) continue;
				IGameModeDescriptor descriptor = factory.GameModeDescriptor;
				if (factory is DataDrivenGameModeFactory defined)
				{
					try
					{
						foreach (GameStoreInstallInfo store in DataDrivenGameModeFactory.BuildStoreDiscovery(defined.Definition.Discovery))
						{
							cancellationToken.ThrowIfCancellationRequested();
							TryAdd(candidates, descriptor, store.Store.ToString(), () => _findStorePath(store));
						}
					}
					catch (Exception error) when (!(error is OperationCanceledException) && !(error is OutOfMemoryException))
					{
						Trace.TraceWarning("Quick game metadata lookup failed for {0}: {1}", descriptor.ModeId, error.Message);
					}
				}
				else if (AuditedLegacyFactories.Contains(factory.GetType().FullName))
				{
					TryAdd(candidates, descriptor, "Launcher/registry", () => _findLegacyPath(factory));
				}
			}
			cancellationToken.ThrowIfCancellationRequested();
			return candidates.AsReadOnly();
		}

		private static void TryAdd(ICollection<GameDiscoveryInstallationCandidate> output,
			IGameModeDescriptor descriptor, string source, Func<string> locate)
		{
			try
			{
				string path = locate();
				if (!IsVerifiedGameRoot(descriptor, path)) return;
				string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				if (!output.Any(existing => String.Equals(existing.ModeId, descriptor.ModeId, StringComparison.OrdinalIgnoreCase) &&
					String.Equals(existing.GameRoot, normalized, StringComparison.OrdinalIgnoreCase)))
					output.Add(new GameDiscoveryInstallationCandidate(descriptor.ModeId, normalized, source));
			}
			catch (Exception error) when (!(error is OperationCanceledException) && !(error is OutOfMemoryException))
			{
				Trace.TraceWarning("Quick game detection failed for {0} ({1}): {2}",
					descriptor.ModeId, source, error.Message);
			}
		}

		/// <summary>Read-only executable + edition check; also suitable for a future manual-path flow.</summary>
		public static bool IsVerifiedGameRoot(IGameModeDescriptor descriptor, string path)
		{
			if (descriptor == null || String.IsNullOrWhiteSpace(path)) return false;
			try
			{
				if (!Path.IsPathRooted(path) || !Directory.Exists(path)) return false;
				string expectedRoot = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				if (!(descriptor.GameExecutables ?? Array.Empty<string>()).Any(executable =>
				{
					string resolvedRoot;
					return !String.IsNullOrWhiteSpace(executable) &&
						GameInstallationCandidatePath.TryResolveGameRoot(Path.Combine(expectedRoot, executable),
							executable, out resolvedRoot) &&
						String.Equals(resolvedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
							expectedRoot, StringComparison.OrdinalIgnoreCase) &&
						File.Exists(Path.Combine(expectedRoot, executable));
				})) return false;
				var validator = descriptor as IGameInstallationCandidateValidator;
				return validator == null || validator.IsValidInstallationCandidate(path);
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
				error is SecurityException || error is ArgumentException || error is NotSupportedException)
			{
				return false;
			}
		}
	}
}
