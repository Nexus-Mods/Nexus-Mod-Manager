using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement;
using Nexus.Client.Settings;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reads persisted AddMod descriptors from NMM settings without automatically resuming Collection-owned work.
	/// </summary>
	public sealed class SettingsCollectionPersistedAddModStateSource : ICollectionPersistedAddModStateSource
	{
		private readonly IEnvironmentInfo _environmentInfo;
		private readonly string _gameModeId;

		/// <summary>Creates a restart-state reader for one game mode.</summary>
		public SettingsCollectionPersistedAddModStateSource(IEnvironmentInfo environmentInfo, string gameModeId)
		{
			_environmentInfo = environmentInfo ?? throw new ArgumentNullException(nameof(environmentInfo));
			if (String.IsNullOrWhiteSpace(gameModeId))
				throw new ArgumentException("A game mode ID is required.", nameof(gameModeId));
			_gameModeId = gameModeId;
		}

		/// <inheritdoc />
		public CollectionPersistedAddModState Find(Guid queueOperationId)
		{
			if (queueOperationId == Guid.Empty)
				throw new ArgumentException("A non-empty AddMod queue-operation identifier is required.", nameof(queueOperationId));
			if (!_environmentInfo.Settings.QueuedModsToAdd.ContainsKey(_gameModeId))
				return null;
			KeyedSettings<AddModDescriptor> queued = _environmentInfo.Settings.QueuedModsToAdd[_gameModeId];
			if (queued == null)
				return null;

			AddModDescriptor match = null;
			foreach (KeyValuePair<string, AddModDescriptor> pair in queued)
			{
				AddModDescriptor descriptor = pair.Value;
				if (descriptor == null || !descriptor.QueueOperationId.HasValue || descriptor.QueueOperationId.Value != queueOperationId)
					continue;
				if (match != null)
					throw new InvalidDataException("Multiple persisted AddMod descriptors claim the same queue-operation identity.");
				match = descriptor;
			}
			return match == null ? null : CreateSnapshot(match);
		}

		private static CollectionPersistedAddModState CreateSnapshot(AddModDescriptor descriptor)
		{
			bool isNexus = descriptor.SourceUri != null && StringComparer.OrdinalIgnoreCase.Equals(descriptor.SourceUri.Scheme, "nxm");
			string gameDomain = null;
			long modId = 0;
			long fileId = 0;
			bool temporaryAuthorization = false;
			DateTime? expiresUtc = null;
			if (isNexus)
			{
				NexusUrl url = new NexusUrl(descriptor.SourceUri);
				gameDomain = url.Host == null ? null : url.Host.Trim().ToLowerInvariant();
				Int64.TryParse(url.ModId, out modId);
				Int64.TryParse(url.FileId, out fileId);
				temporaryAuthorization = !String.IsNullOrWhiteSpace(url.Key) || url.Expiry > 0 || url.UserId > 0;
				if (url.Expiry > 0)
				{
					DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
					try { expiresUtc = epoch.AddSeconds(url.Expiry); }
					catch (ArgumentOutOfRangeException) { expiresUtc = DateTime.MinValue; }
				}
			}

			bool hasPartialData = false;
			if (!String.IsNullOrWhiteSpace(descriptor.DefaultSourcePath))
			{
				hasPartialData = File.Exists(descriptor.DefaultSourcePath + ".parts") ||
					((descriptor.Status == TaskStatus.Paused || descriptor.Status == TaskStatus.Incomplete || descriptor.Status == TaskStatus.Queued) &&
					 File.Exists(descriptor.DefaultSourcePath));
			}
			return new CollectionPersistedAddModState(descriptor.QueueOperationId.Value, descriptor.Status, isNexus,
				gameDomain, modId, fileId, temporaryAuthorization, expiresUtc, hasPartialData);
		}
	}
}
