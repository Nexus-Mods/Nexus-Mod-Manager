using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Creates canonical deployment targets using the same root and path rules as game deployment.
	/// </summary>
	public static class ModDeploymentTargetResolver
	{
		/// <summary>
		/// Resolves a logical mod destination into a canonical deployment target.
		/// </summary>
		/// <param name="gameMode">The current game mode.</param>
		/// <param name="mod">The mod owning the destination, when available.</param>
		/// <param name="relativePath">The logical destination path.</param>
		/// <param name="installRoot">The operation-level install root.</param>
		/// <returns>The canonical deployment target.</returns>
		public static ModDeploymentTarget Resolve(IGameMode gameMode, IMod mod, string relativePath, ModInstallRoot installRoot)
		{
			if (gameMode == null)
				throw new ArgumentNullException(nameof(gameMode));

			installRoot = NormalizeInstallRoot(installRoot);
			if (installRoot == ModInstallRoot.GameRoot)
				return FromCanonical(ModDeploymentRoot.GameRoot, relativePath);

			string adjustedPath = mod == null
				? gameMode.GetModFormatAdjustedPath(null, relativePath, true)
				: gameMode.GetModFormatAdjustedPath(mod.Format, relativePath, mod, true);

			ModDeploymentRoot root = ModDeploymentRoot.Data;
			if (mod != null && gameMode.HasSecondaryInstallPath && gameMode.CheckSecondaryInstall(mod, adjustedPath))
				root = ModDeploymentRoot.Secondary;

			return FromCanonical(root, adjustedPath);
		}

		/// <summary>
		/// Rehydrates an already root-resolved persisted target while applying canonical path validation.
		/// </summary>
		/// <param name="root">The persisted deployment root.</param>
		/// <param name="relativePath">The persisted relative path.</param>
		/// <returns>The canonical deployment target.</returns>
		public static ModDeploymentTarget FromCanonical(ModDeploymentRoot root, string relativePath)
		{
			if (!Enum.IsDefined(typeof(ModDeploymentRoot), root))
				throw new ArgumentOutOfRangeException(nameof(root));

			return new ModDeploymentTarget(root, NormalizeRelativePath(relativePath));
		}

		/// <summary>
		/// Resolves a canonical deployment root to its physical game path.
		/// </summary>
		/// <param name="gameMode">The current game mode.</param>
		/// <param name="root">The canonical deployment root.</param>
		/// <returns>The configured physical root path.</returns>
		public static string GetPhysicalRootPath(IGameMode gameMode, ModDeploymentRoot root)
		{
			if (gameMode == null)
				throw new ArgumentNullException(nameof(gameMode));

			string rootPath;
			switch (root)
			{
				case ModDeploymentRoot.Data:
					rootPath = gameMode.UsesPlugins ? gameMode.PluginDirectory : gameMode.InstallationPath;
					break;
				case ModDeploymentRoot.GameRoot:
					rootPath = gameMode.InstallationPath;
					break;
				case ModDeploymentRoot.Secondary:
					rootPath = gameMode.SecondaryInstallationPath;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(root));
			}

			if (string.IsNullOrWhiteSpace(rootPath))
				throw new InvalidOperationException(string.Format("Deployment root '{0}' is not configured for the current game mode.", root));

			return rootPath;
		}

		/// <summary>
		/// Resolves a canonical deployment target to its contained physical filesystem path.
		/// </summary>
		/// <param name="gameMode">The current game mode.</param>
		/// <param name="target">The canonical deployment target.</param>
		/// <returns>The physical deployment path.</returns>
		public static string GetPhysicalPath(IGameMode gameMode, ModDeploymentTarget target)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));

			string rootPath = Path.GetFullPath(GetPhysicalRootPath(gameMode, target.Root));
			string rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
				rootPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
					? rootPath
					: rootPath + Path.DirectorySeparatorChar;
			string path = Path.GetFullPath(Path.Combine(rootPath, target.RelativePath));
			if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(string.Format("The deployment path '{0}' escapes its configured root.", target.RelativePath));

			return path;
		}

		private static ModInstallRoot NormalizeInstallRoot(ModInstallRoot installRoot)
		{
			if (installRoot == ModInstallRoot.Data)
				return ModInstallRoot.Data;
			if (installRoot == ModInstallRoot.GameRoot)
				return ModInstallRoot.GameRoot;

			throw new ArgumentOutOfRangeException(nameof(installRoot));
		}

		private static string NormalizeRelativePath(string relativePath)
		{
			if (string.IsNullOrWhiteSpace(relativePath))
				throw new ArgumentException("Deployment path must not be empty.", nameof(relativePath));

			string normalized = relativePath.Replace('/', '\\');
			if (normalized[0] == '\\' || Path.IsPathRooted(relativePath) ||
				(normalized.Length > 1 && normalized[1] == ':'))
			{
				throw new InvalidDataException(string.Format("Deployment path '{0}' must be relative to its deployment root.", relativePath));
			}

			var parts = new List<string>();
			foreach (string part in normalized.Split('\\'))
			{
				if (string.IsNullOrEmpty(part) || part == ".")
					continue;
				if (part == "..")
					throw new InvalidDataException(string.Format("Deployment path '{0}' escapes its deployment root.", relativePath));
				if (part.IndexOf(':') >= 0)
					throw new InvalidDataException(string.Format("Deployment path '{0}' contains an invalid root qualifier.", relativePath));

				parts.Add(part);
			}

			if (parts.Count == 0)
				throw new InvalidDataException(string.Format("Deployment path '{0}' does not identify a file beneath its deployment root.", relativePath));

			return string.Join("\\", parts.ToArray());
		}

	}
}
