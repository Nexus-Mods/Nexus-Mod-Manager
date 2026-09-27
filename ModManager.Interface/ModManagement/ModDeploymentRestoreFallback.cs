using System;

namespace Nexus.Client.ModManagement
{
	/// <summary>Classifies the unmanaged/original fallback to establish beneath a pure-Virtual restore stack.</summary>
	public enum ModDeploymentRestoreFallbackKind
	{
		PreserveCurrent = 0,
		ExplicitlyAbsent = 1,
		Retained = 2
	}

	/// <summary>Describes the exact pure-Virtual fallback required by one native captured-owner restore.</summary>
	public sealed class ModDeploymentRestoreFallback
	{
		/// <summary>Creates one ephemeral fallback input. Retained payload paths are consumed synchronously.</summary>
		public ModDeploymentRestoreFallback(ModDeploymentRestoreFallbackKind kind, string payloadPath)
		{
			if (!Enum.IsDefined(typeof(ModDeploymentRestoreFallbackKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (kind == ModDeploymentRestoreFallbackKind.Retained && String.IsNullOrWhiteSpace(payloadPath))
				throw new ArgumentException("A retained pure-Virtual fallback requires a materialized payload path.", nameof(payloadPath));
			if (kind != ModDeploymentRestoreFallbackKind.Retained && !String.IsNullOrWhiteSpace(payloadPath))
				throw new ArgumentException("Only a retained pure-Virtual fallback may reference payload bytes.", nameof(payloadPath));

			Kind = kind;
			PayloadPath = payloadPath ?? String.Empty;
		}

		public ModDeploymentRestoreFallbackKind Kind { get; }
		public string PayloadPath { get; }
	}
}
