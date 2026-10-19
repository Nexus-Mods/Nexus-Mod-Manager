namespace Nexus.Client.Games
{
	using System;
	using System.Diagnostics;
	using System.Linq;
	using System.Windows.Forms;
	using DevExpress.XtraEditors;
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
			if (String.Equals(factory.GameModeDescriptor.ModeId, Witcher3RootOwnershipGuard.RemasteredModeId,
				StringComparison.OrdinalIgnoreCase) && !Witcher3RemasteredReleasePolicy.NativeActivationApproved)
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
			for (;;)
			{
				using (var setup = new GameStorageSetupForm(service, paths, health, false))
				{
					// The game mode is only registered AFTER explicit storage configuration succeeds.
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
						if (service.ApplyInitialSetupCandidate(paths, setup.SelectedCandidate, out health))
						{
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
				// A failed metadata/ownership validation must not silently take the
				// paths-only fallback or claim a healthy Managed configuration.
				string reason = health?.ToUserMessage() ?? LanguageManager.Get("GameDiscovery.SetupFailed", "Game Storage could not be configured.");
				Trace.TraceWarning("Game Discovery setup was not applied for {0}: {1}", id, reason);
				if (XtraMessageBox.Show(owner, reason + Environment.NewLine + Environment.NewLine +
					LanguageManager.Get("GameDiscovery.ReviewAgain", "Review the storage choices and try again?"),
					LanguageManager.Get("GameStorage.Setup.GenericTitle", "Game Storage setup"),
					MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry)
				{
					message = reason;
					return false;
				}
				paths = GameStorageInitialPaths.Create(_environment, service, factory, installedPath);
			}
		}
	}
}
