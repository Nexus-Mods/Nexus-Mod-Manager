using System;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Production Collection acquisition adapter which delegates to the existing <see cref="ModManager"/> AddMod queue.
	/// </summary>
	public sealed class ModManagerCollectionAddModQueue : ICollectionAddModQueue
	{
		private readonly ModManager _modManager;

		/// <summary>
		/// Creates an adapter for the supplied native mod manager.
		/// </summary>
		public ModManagerCollectionAddModQueue(ModManager modManager)
		{
			_modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
		}

		/// <inheritdoc />
		public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
		{
			return _modManager.AddModWithQueueOperationId(sourceUri, confirmOverwriteCallback, null, queueOperationId);
		}
	}
}
