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
