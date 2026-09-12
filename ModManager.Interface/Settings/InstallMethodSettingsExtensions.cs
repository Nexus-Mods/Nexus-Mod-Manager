using System;
using Nexus.Client.ModManagement;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// Provides typed access to the per-game preferred mod install method setting.
	/// </summary>
	public static class InstallMethodSettingsExtensions
	{
		/// <summary>
		/// Gets the preferred install method for a game, defaulting missing or invalid values to Virtual.
		/// </summary>
		/// <param name="settings">The application settings.</param>
		/// <param name="gameModeId">The game mode identifier.</param>
		/// <returns>The preferred install method.</returns>
		public static ModInstallMethod GetPreferredInstallMethod(this ISettings settings, string gameModeId)
		{
			if (settings == null)
				throw new ArgumentNullException(nameof(settings));
			if (string.IsNullOrWhiteSpace(gameModeId) || settings.PreferredInstallMethod == null)
				return ModInstallMethod.Virtual;

			ModInstallMethod method;
			string value = settings.PreferredInstallMethod[gameModeId];
			return Enum.TryParse(value, true, out method) && Enum.IsDefined(typeof(ModInstallMethod), method)
				? method
				: ModInstallMethod.Virtual;
		}

		/// <summary>
		/// Sets the preferred install method for a game using its stable persisted name.
		/// </summary>
		/// <param name="settings">The application settings.</param>
		/// <param name="gameModeId">The game mode identifier.</param>
		/// <param name="method">The preferred install method.</param>
		public static void SetPreferredInstallMethod(this ISettings settings, string gameModeId, ModInstallMethod method)
		{
			if (settings == null)
				throw new ArgumentNullException(nameof(settings));
			if (string.IsNullOrWhiteSpace(gameModeId))
				throw new ArgumentException("A game mode id is required.", nameof(gameModeId));
			if (!Enum.IsDefined(typeof(ModInstallMethod), method))
				throw new ArgumentOutOfRangeException(nameof(method));
			if (settings.PreferredInstallMethod == null)
				throw new InvalidOperationException("The preferred install method settings store is unavailable.");

			settings.PreferredInstallMethod[gameModeId] = method.ToString();
		}
	}
}
