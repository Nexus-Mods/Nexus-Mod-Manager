using System;
using System.Collections.Generic;
using Nexus.Client.Games.DataDriven.Tools.AI;
using Nexus.Client.Games.Gamebryo.Tools.AI;
using Nexus.Client.Games.Gamebryo.Tools.AI.UI;
using Nexus.Client.Games.Tools;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>
	/// Exposes the in-process tools configured for a data-driven
	/// Gamebryo game mode.
	/// </summary>
	public sealed class DataDrivenGamebryoToolLauncher : IToolLauncher
	{
		private readonly List<ITool> _tools = new List<ITool>();

		public DataDrivenGamebryoToolLauncher(DataDrivenGamebryoGameMode gameMode, GameModeDefinition definition)
		{
			if (gameMode == null)
				throw new ArgumentNullException(nameof(gameMode));

			if (definition == null)
				throw new ArgumentNullException(nameof(definition));

			AddArchiveInvalidationTool(
				gameMode,
				definition.Gamebryo);
		}

		public IEnumerable<ITool> Tools
		{
			get
			{
				return _tools;
			}
		}

		private void AddArchiveInvalidationTool(DataDrivenGamebryoGameMode gameMode, GameModeGamebryoDefinition definition)
		{
			if (definition == null)
				return;

			string profile =
				DataDrivenArchiveInvalidationProfiles.Normalize(
					definition.ArchiveInvalidationProfile);

			if (profile == DataDrivenArchiveInvalidationProfiles.None)
				return;

			ArchiveInvalidationBase archiveInvalidation;

			switch (profile)
			{
				case DataDrivenArchiveInvalidationProfiles.Fallout3:
					archiveInvalidation =
						new Fallout3ArchiveInvalidation(gameMode);
					break;

				case DataDrivenArchiveInvalidationProfiles.FalloutNV:
					archiveInvalidation =
						new FalloutNVArchiveInvalidation(gameMode);
					break;

				case DataDrivenArchiveInvalidationProfiles.Oblivion:
					archiveInvalidation =
						new OblivionArchiveInvalidation(gameMode);
					break;

				default:
					throw new InvalidOperationException(
						"Unknown Archive Invalidation profile '" +
						definition.ArchiveInvalidationProfile +
						"'.");
			}

			archiveInvalidation.SetToolView(
				new ArchiveInvalidationView(archiveInvalidation));

			_tools.Add(archiveInvalidation);
		}
	}

	internal static class DataDrivenArchiveInvalidationProfiles
	{
		public const string None = "none";
		public const string Fallout3 = "fallout3";
		public const string FalloutNV = "falloutnv";
		public const string Oblivion = "oblivion";

		public static string Normalize(string profile)
		{
			if (string.IsNullOrWhiteSpace(profile))
				return None;

			return profile.Trim().ToLowerInvariant();
		}

		public static bool IsEnabled(string profile)
		{
			return Normalize(profile) != None;
		}

		public static bool IsSupported(string profile)
		{
			switch (Normalize(profile))
			{
				case None:
				case Fallout3:
				case FalloutNV:
				case Oblivion:
					return true;

				default:
					return false;
			}
		}
	}
}
