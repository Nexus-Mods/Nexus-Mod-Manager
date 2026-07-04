using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Nexus.Client.Games
{
	public sealed class GameStoreInstallationPathDetector
	{
		private static readonly Lazy<GameStoreInstallationPathDetector> Lazy = new Lazy<GameStoreInstallationPathDetector>(() => new GameStoreInstallationPathDetector());

		public static GameStoreInstallationPathDetector Instance => Lazy.Value;

		private GameStoreInstallationPathDetector()
		{
		}

		public string GetInstallationPath(IEnumerable<GameStoreInstallInfo> p_enmStores)
		{
			if (p_enmStores == null)
				return null;

			foreach (GameStoreInstallInfo store in p_enmStores.Where(x => x != null))
			{
				string path = GetInstallationPath(store);
				if (!string.IsNullOrWhiteSpace(path))
					return path;
			}

			return null;
		}

		public string GetInstallationPath(GameStoreInstallInfo p_gsiStore)
		{
			if (p_gsiStore == null)
				return null;

			switch (p_gsiStore.Store)
			{
				case GameStore.Steam:
					return DetectSteam(p_gsiStore);
				case GameStore.Gog:
					return DetectGog(p_gsiStore);
				case GameStore.Epic:
					return DetectEpic(p_gsiStore);
				case GameStore.Registry:
					return DetectRegistry(p_gsiStore);
				default:
					return null;
			}
		}

		private string DetectSteam(GameStoreInstallInfo p_gsiStore)
		{
			if (string.IsNullOrWhiteSpace(p_gsiStore.Id))
				return null;

			string path = SteamInstallationPathDetector.Instance.GetSteamInstallationPath(p_gsiStore.Id, p_gsiStore.InstallFolderName, p_gsiStore.ExecutableName);
			return ValidatePath(path, p_gsiStore);
		}

		private string DetectGog(GameStoreInstallInfo p_gsiStore)
		{
			string valueName = string.IsNullOrWhiteSpace(p_gsiStore.RegistryValueName) ? "PATH" : p_gsiStore.RegistryValueName;

			if (!string.IsNullOrWhiteSpace(p_gsiStore.RegistryKey))
				return ValidatePath(ReadRegistryPath(p_gsiStore.RegistryKey, valueName), p_gsiStore);

			if (string.IsNullOrWhiteSpace(p_gsiStore.Id))
				return null;

			string[] keys =
			{
				@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\Games\" + p_gsiStore.Id,
				@"HKEY_LOCAL_MACHINE\SOFTWARE\GOG.com\Games\" + p_gsiStore.Id,
				@"HKEY_CURRENT_USER\SOFTWARE\GOG.com\Games\" + p_gsiStore.Id
			};

			foreach (string key in keys)
			{
				string path = ValidatePath(ReadRegistryPath(key, valueName), p_gsiStore);
				if (!string.IsNullOrWhiteSpace(path))
					return path;
			}

			return null;
		}

		private string DetectRegistry(GameStoreInstallInfo p_gsiStore)
		{
			if (string.IsNullOrWhiteSpace(p_gsiStore.RegistryKey))
				return null;

			string valueName = string.IsNullOrWhiteSpace(p_gsiStore.RegistryValueName) ? "InstallLocation" : p_gsiStore.RegistryValueName;
			return ValidatePath(ReadRegistryPath(p_gsiStore.RegistryKey, valueName), p_gsiStore);
		}

		private string DetectEpic(GameStoreInstallInfo p_gsiStore)
		{
			string manifestRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
			if (!Directory.Exists(manifestRoot))
				return null;

			foreach (string manifest in Directory.GetFiles(manifestRoot, "*.item"))
			{
				try
				{
					string text = File.ReadAllText(manifest);
					if (!MatchesEpicManifest(text, p_gsiStore))
						continue;

					string path = ValidatePath(ReadJsonString(text, "InstallLocation"), p_gsiStore);
					if (!string.IsNullOrWhiteSpace(path))
						return path;
				}
				catch (Exception e)
				{
					Trace.TraceWarning("Unable to read Epic manifest '{0}': {1}", manifest, e.Message);
				}
			}

			return null;
		}

		private static bool MatchesEpicManifest(string p_strManifest, GameStoreInstallInfo p_gsiStore)
		{
			if (!string.IsNullOrWhiteSpace(p_gsiStore.Id))
			{
				string[] keys = { "AppName", "CatalogItemId", "MainGameCatalogItemId" };
				if (keys.Any(key => string.Equals(ReadJsonString(p_strManifest, key), p_gsiStore.Id, StringComparison.OrdinalIgnoreCase)))
					return true;
			}

			return !string.IsNullOrWhiteSpace(p_gsiStore.InstallFolderName) &&
				string.Equals(ReadJsonString(p_strManifest, "DisplayName"), p_gsiStore.InstallFolderName, StringComparison.OrdinalIgnoreCase);
		}

		private static string ReadJsonString(string p_strText, string p_strKey)
		{
			Match match = Regex.Match(p_strText, "\"" + Regex.Escape(p_strKey) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
			if (!match.Success)
				return null;

			return Regex.Unescape(match.Groups[1].Value);
		}

		private static string ReadRegistryPath(string p_strRegistryKey, string p_strValueName)
		{
			try
			{
				return Registry.GetValue(p_strRegistryKey, p_strValueName, null)?.ToString();
			}
			catch
			{
				return null;
			}
		}

		private static string ValidatePath(string p_strPath, GameStoreInstallInfo p_gsiStore)
		{
			if (string.IsNullOrWhiteSpace(p_strPath))
				return null;

			string path = Environment.ExpandEnvironmentVariables(p_strPath);
			if (!string.IsNullOrWhiteSpace(p_gsiStore.PathSuffix))
				path = Path.Combine(path, p_gsiStore.PathSuffix);

			if (!Directory.Exists(path))
				return null;

			if (!string.IsNullOrWhiteSpace(p_gsiStore.ExecutableName) && !File.Exists(Path.Combine(path, p_gsiStore.ExecutableName)))
				return null;

			return path;
		}
	}
}