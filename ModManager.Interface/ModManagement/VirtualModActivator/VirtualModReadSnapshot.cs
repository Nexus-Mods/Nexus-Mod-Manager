using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Immutable read-only observation of the current Virtual Mod Activator link records.
	/// </summary>
	public sealed class VirtualModReadSnapshot
	{
		/// <summary>Creates one immutable Virtual link observation.</summary>
		public VirtualModReadSnapshot(IEnumerable<VirtualModReadLink> links)
		{
			if (links == null)
				throw new ArgumentNullException(nameof(links));
			List<VirtualModReadLink> copied = links.ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A Virtual state snapshot cannot contain null links.", nameof(links));
			Links = new ReadOnlyCollection<VirtualModReadLink>(copied);
		}

		public ReadOnlyCollection<VirtualModReadLink> Links { get; }
	}

	/// <summary>
	/// Detached Virtual owner/link observation preserving target identity, owner, activation and priority.
	/// </summary>
	public sealed class VirtualModReadLink
	{
		/// <summary>Creates one detached Virtual link record.</summary>
		public VirtualModReadLink(ModDeploymentTarget target, string ownerKey, string ownerReference, bool active, int priority,
			string stagedSourcePath)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			OwnerKey = ownerKey;
			OwnerReference = ownerReference ?? String.Empty;
			Active = active;
			Priority = priority;
			StagedSourcePath = stagedSourcePath;
		}

		public ModDeploymentTarget Target { get; }
		public string OwnerKey { get; }
		public string OwnerReference { get; }
		public bool Active { get; }
		public int Priority { get; }
		public string StagedSourcePath { get; }
	}
}
