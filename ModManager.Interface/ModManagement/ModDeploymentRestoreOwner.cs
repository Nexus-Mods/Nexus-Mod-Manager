using System;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>Classifies one retained payload while reconstructing a captured deployment owner stack.</summary>
	public enum ModDeploymentRestoreOwnerKind
	{
		OriginalValue = 1,
		Direct = 2,
		Virtual = 3
	}

	/// <summary>
	/// Describes one already-remapped owner and its independently materialized retained payload for native restoration.
	/// </summary>
	public sealed class ModDeploymentRestoreOwner
	{
		/// <summary>Creates one ephemeral native restore input. The payload path is consumed synchronously and is not persisted.</summary>
		public ModDeploymentRestoreOwner(string ownerKey, ModDeploymentRestoreOwnerKind kind, IMod mod,
			ModInstallRoot installRoot, string payloadPath)
		{
			if (String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A deployment restore owner key is required.", nameof(ownerKey));
			if (!Enum.IsDefined(typeof(ModDeploymentRestoreOwnerKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (kind == ModDeploymentRestoreOwnerKind.OriginalValue && mod != null)
				throw new ArgumentException("The unmanaged original deployment owner cannot reference a managed mod.", nameof(mod));
			if (kind != ModDeploymentRestoreOwnerKind.OriginalValue && mod == null)
				throw new ArgumentNullException(nameof(mod), "A managed deployment restore owner requires its live mod registration.");
			if (String.IsNullOrWhiteSpace(payloadPath))
				throw new ArgumentException("A materialized retained payload path is required.", nameof(payloadPath));

			OwnerKey = ownerKey;
			Kind = kind;
			Mod = mod;
			InstallRoot = installRoot;
			PayloadPath = payloadPath;
		}

		public string OwnerKey { get; }
		public ModDeploymentRestoreOwnerKind Kind { get; }
		public IMod Mod { get; }
		public ModInstallRoot InstallRoot { get; }
		public string PayloadPath { get; }
	}
}
