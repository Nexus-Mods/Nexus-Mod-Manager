using System;
using System.Collections.Generic;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Stores method-neutral mod and promoted-target state for a profile.
	/// </summary>
	public sealed class ProfileDeploymentManifest
	{
		/// <summary>
		/// Gets whether the manifest was synthesized from a legacy Virtual-only profile.
		/// </summary>
		public bool IsLegacy { get; internal set; }

		/// <summary>
		/// Gets the mods belonging to the profile.
		/// </summary>
		public List<ProfileDeploymentMod> Mods { get; } = new List<ProfileDeploymentMod>();

		/// <summary>
		/// Gets the promoted deployment targets belonging to the profile.
		/// </summary>
		public List<ProfileDeploymentTargetState> Targets { get; } = new List<ProfileDeploymentTargetState>();
	}

	/// <summary>
	/// Identifies one profile mod independently from an InstallLog runtime key.
	/// </summary>
	public sealed class ProfileDeploymentMod
	{
		public string ProfileModId { get; set; }
		public string FileName { get; set; }
		public string ModId { get; set; }
		public string DownloadId { get; set; }
		public string Version { get; set; }
		public ModInstallMethod Method { get; set; }
		public ModInstallRoot InstallRoot { get; set; }
	}

	/// <summary>
	/// Stores the profile-relative ownership order for one promoted target.
	/// </summary>
	public sealed class ProfileDeploymentTargetState
	{
		public ModDeploymentTarget Target { get; set; }
		public List<ProfileDeploymentOwner> Owners { get; } = new List<ProfileDeploymentOwner>();
	}

	/// <summary>
	/// Refers either to the original loose-file fallback or to a profile mod owner.
	/// </summary>
	public sealed class ProfileDeploymentOwner
	{
		public bool IsOriginal { get; set; }
		public string ProfileModId { get; set; }
	}

	/// <summary>
	/// Describes one explicit install required while switching profiles.
	/// </summary>
	public sealed class ProfileDeploymentInstallRequest
	{
		public ProfileDeploymentInstallRequest(IMod mod, ModInstallContext context)
		{
			Mod = mod ?? throw new ArgumentNullException(nameof(mod));
			Context = context ?? throw new ArgumentNullException(nameof(context));
		}

		public IMod Mod { get; }
		public ModInstallContext Context { get; }
	}

	/// <summary>
	/// Contains the mod-level changes required before applying profile topology.
	/// </summary>
	public sealed class ProfileDeploymentPlan
	{
		public List<IMod> ModsToDeactivate { get; } = new List<IMod>();
		public List<ProfileDeploymentInstallRequest> ModsToInstall { get; } = new List<ProfileDeploymentInstallRequest>();
	}
}
