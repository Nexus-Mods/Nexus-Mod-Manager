using System;
using Nexus.Client.ModManagement;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// Provides typed access to the per-game preferred mod install method setting.
	/// </summary>
	public static class InstallMethodSettingsExtensions
	{
		/// <summary>Recognizes an explicitly empty Virtual folder while preserving legacy unset settings.</summary>
		public static bool IsVirtualInstallDisabled(this ISettings settings, string gameModeId)
		{
			if (settings == null)
				throw new ArgumentNullException(nameof(settings));
			return !string.IsNullOrWhiteSpace(gameModeId) && settings.VirtualFolder != null &&
				settings.VirtualFolder.ContainsKey(gameModeId) && string.IsNullOrWhiteSpace(settings.VirtualFolder[gameModeId]);
		}

		/// <summary>
		/// Gets the preferred method, using Direct for an explicitly empty Virtual folder and legacy Virtual defaults otherwise.
		/// </summary>
		/// <param name="settings">The application settings.</param>
		/// <param name="gameModeId">The game mode identifier.</param>
		/// <returns>The preferred install method.</returns>
		public static ModInstallMethod GetPreferredInstallMethod(this ISettings settings, string gameModeId)
		{
			if (settings == null)
				throw new ArgumentNullException(nameof(settings));
			if (settings.IsVirtualInstallDisabled(gameModeId))
				return ModInstallMethod.Direct;
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
