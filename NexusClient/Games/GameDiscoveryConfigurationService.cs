namespace Nexus.Client.Games
{
	using System;
	using System.Diagnostics;
	using System.Collections.Generic;
	using Nexus.Client.Settings;
	using System.Linq;
	using System.Windows.Forms;
	using Nexus.Client.GameStorage;
	using Nexus.Client.GameStorage.UI;
	using Nexus.Client.ModManagement.Witcher3;
	using Nexus.Client.Games.DataDriven;
	using Nexus.Client.Util.Localization;

	/// <summary>Explicitly configures an identified installation without constructing IGameMode,
	/// opening native mod services, applying delayed settings or entering a game session.</summary>
	internal sealed class GameDiscoveryConfigurationService
	{
		private readonly EnvironmentInfo _environment;
		internal GameDiscoveryConfigurationService(EnvironmentInfo environment)
		{
			_environment = environment ?? throw new ArgumentNullException(nameof(environment));
		}

		/// <summary>Confirms only the game installation and leaves existing mod storage pending explicit configuration.</summary>
		internal static bool ConfirmInstallation(ISettings settings, IGameModeFactory factory, string selectedRoot, out string message)
		{
			message = null;
			if (settings == null || factory == null || !QuickGameDiscovery.IsVerifiedGameRoot(factory.GameModeDescriptor, selectedRoot))
			{
				message = LanguageManager.Get("GameDiscovery.InvalidLocation", "The selected folder is not a valid installation of this game/edition.");
				return false;
			}
			string id = factory.GameModeDescriptor.ModeId;
			string installedPath = factory.GetInstallationPath(selectedRoot);
			string executablePath = factory.GetExecutablePath(selectedRoot);
			if (String.IsNullOrWhiteSpace(installedPath) || String.IsNullOrWhiteSpace(executablePath))
			{
				message = LanguageManager.Get("GameDiscovery.InvalidMappedLocation", "The game factory could not resolve this installation.");
				return false;
			}
			lock (settings)
			{
				string previousInstallation, previousExecutable;
				bool previousSetup;
				bool hadInstallation = settings.InstallationPaths.TryGetValue(id, out previousInstallation);
				bool hadExecutable = settings.ExecutablePaths.TryGetValue(id, out previousExecutable);
				bool hadSetup = settings.CompletedSetup.TryGetValue(id, out previousSetup);
				var previousSections = settings.GameDiscoverySections;
				settings.InstallationPaths[id] = installedPath;
				settings.ExecutablePaths[id] = executablePath;
				settings.CompletedSetup[id] = false;
				settings.GameDiscoverySections = CopySettings(previousSections);
				settings.GameDiscoverySections[id] = nameof(GameDiscoveryCollection.ToConfigure);
				try { settings.Save(); }
				catch
				{
					RestoreSetting(settings.InstallationPaths, id, hadInstallation, previousInstallation);
					RestoreSetting(settings.ExecutablePaths, id, hadExecutable, previousExecutable);
					RestoreSetting(settings.CompletedSetup, id, hadSetup, previousSetup);
					settings.GameDiscoverySections = previousSections;
					throw;
				}
			}
			message = LanguageManager.Get("GameDiscovery.InstallationConfirmed", "Installation path confirmed. Configure game storage in To configure.");
			return true;
		}

		/// <summary>Copies a per-game setting dictionary so a failed save can restore the original state.</summary>
		private static PerGameModeSettings<T> CopySettings<T>(PerGameModeSettings<T> source)
		{
			var copy = new PerGameModeSettings<T>();
			if (source != null)
				foreach (KeyValuePair<string, T> item in source) copy[item.Key] = item.Value;
			return copy;
		}

		/// <summary>Restores only the affected game's setting when persisting confirmation fails.</summary>
		private static void RestoreSetting<T>(PerGameModeSettings<T> settings, string id, bool existed, T previous)
		{
			if (existed) settings[id] = previous;
			else settings.Remove(id);
		}

		internal bool Configure(IWin32Window owner, IGameModeFactory factory, string selectedRoot,
			out string message, Func<IDisposable> acquireNativeWriteReservation = null)
		{
			message = null;
			if (factory == null || !QuickGameDiscovery.IsVerifiedGameRoot(factory.GameModeDescriptor, selectedRoot))
			{
				message = LanguageManager.Get("GameDiscovery.InvalidLocation", "The selected folder is not a valid installation of this game/edition.");
				return false;
			}

			// Honor the existing release gate before provisioning any storage.
			if (GameModeRuntimePolicies.For(factory.GameModeDescriptor.ModeId).RequiresReviewedNativeInstall && !Witcher3RemasteredReleasePolicy.NativeActivationApproved)
			{
				message = Witcher3RemasteredReleasePolicy.CreateActivationBlockMessage().Message;
				return false;
			}

			// Factory paths can differ (for example a game's managed Documents directory).
			// Do not overwrite a different, previously configured edition's root implicitly.
			string id = factory.GameModeDescriptor.ModeId;
			string installedPath = factory.GetInstallationPath(selectedRoot);
			string executablePath = factory.GetExecutablePath(selectedRoot);
			if (String.IsNullOrWhiteSpace(installedPath) || String.IsNullOrWhiteSpace(executablePath))
			{
				message = LanguageManager.Get("GameDiscovery.InvalidMappedLocation", "The game factory could not resolve this installation.");
				return false;
			}

			var service = new GameStorageService(_environment);
			GameStoragePathSet paths = GameStorageInitialPaths.Create(_environment, service, factory, installedPath);
			GameStorageHealthCheck health = service.ValidateStorage(paths);
			using (var setup = new GameStorageSetupForm(service, paths, health, false))
			{
				// Register only after the user saves the selected storage paths.
				// The old initializer retains its legacy fallback for historical setups.
				if (setup.ShowDialog(owner) != DialogResult.OK)
					return false;
				if (!QuickGameDiscovery.IsVerifiedGameRoot(factory.GameModeDescriptor, selectedRoot))
				{
					message = LanguageManager.Get("GameDiscovery.LocationChanged", "The game installation changed while you were configuring storage. Locate it again.");
					return false;
				}
				// Hold the shared native-process reservation ONLY while applying the
				// selected storage. The modal review itself never interrupts a
				// different game's live session.
				IDisposable reservation = acquireNativeWriteReservation == null
					? null : acquireNativeWriteReservation();
				if (acquireNativeWriteReservation != null && reservation == null)
				{
					message = LanguageManager.Get("GameDiscovery.StorageBusy",
						"A mod or Collection operation is running. Finish it before saving Game Storage.");
					return false;
				}
				using (reservation)
				{
					// Saving the reviewed selection accepts intentional empty folders. Other
					// metadata warnings use the same selected-path fallback as initial setup.
					if (!service.ApplyInitialSetupCandidate(paths, setup.SelectedCandidate, true, out health))
					{
						string reason = health?.ToUserMessage() ?? LanguageManager.Get("GameDiscovery.SetupFailed", "Game Storage could not be configured.");
						Trace.TraceWarning("Applying explicitly selected Game Discovery storage paths for {0}: {1}", id, reason);
						if (!service.ApplySelectedCandidatePaths(paths, setup.SelectedCandidate))
						{
							message = reason;
							return false;
						}
					}
					// Finish the target registration under the same native-process
					// gate as the metadata write. The active mode's keys are untouched.
					_environment.Settings.InstallationPaths[id] = installedPath;
					_environment.Settings.ExecutablePaths[id] = executablePath;
					if (!_environment.Settings.InstalledGames.Cast<string>().Any(value =>
						String.Equals(value, id, StringComparison.OrdinalIgnoreCase)))
						_environment.Settings.InstalledGames.Add(id);
					_environment.Settings.CompletedSetup[id] = true;
					_environment.Settings.InstalledGamesDetected = true;
					_environment.Settings.Save();
					message = LanguageManager.Format("GameDiscovery.Configured", "{0} is configured. Select Manage to open it.", factory.GameModeDescriptor.Name);
					return true;
				}
			}
		}
	}
}
