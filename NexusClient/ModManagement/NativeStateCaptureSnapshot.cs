using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Describes how completely one optional native state surface was enumerated for a read-only capture.
	/// </summary>
	public enum NativeStateCaptureCoverage
	{
		Unavailable = 0,
		Complete = 1,
		NotApplicable = 2,
		Partial = 3
	}

	/// <summary>
	/// Classifies a non-fatal limitation observed while creating a generic native-state capture.
	/// </summary>
	public enum NativeStateCaptureIssueKind
	{
		PhysicalRootUnavailable = 1,
		DeploymentStateUnavailable = 2,
		DeploymentPathUnavailable = 3,
		DeploymentOwnerUnresolved = 4,
		DeploymentPayloadSourceUnavailable = 5,
		ReplayReferenceUnavailable = 6,
		PluginStateUnavailable = 7,
		VirtualPayloadSourceUnavailable = 8
	}

	/// <summary>
	/// Records one non-fatal limitation without mutating or repairing native state.
	/// </summary>
	public sealed class NativeStateCaptureIssue
	{
		/// <summary>Creates one immutable native-state capture issue.</summary>
		public NativeStateCaptureIssue(NativeStateCaptureIssueKind kind, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(NativeStateCaptureIssueKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public NativeStateCaptureIssueKind Kind { get; }
		public string ResourceKey { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Captures one native deployment root and its current physical path.
	/// </summary>
	public sealed class NativeStateCaptureRoot
	{
		/// <summary>Creates one detached native deployment-root observation.</summary>
		public NativeStateCaptureRoot(ModDeploymentRoot root, string physicalPath)
		{
			Root = root;
			PhysicalPath = physicalPath ?? String.Empty;
		}

		public ModDeploymentRoot Root { get; }
		public string PhysicalPath { get; }
	}

	/// <summary>
	/// Distinguishes the native owner kind without treating original/fallback state as a mod instance.
	/// </summary>
	public enum NativeStateCaptureDeploymentOwnerKind
	{
		Unresolved = 0,
		OriginalValue = 1,
		Direct = 2,
		Virtual = 3
	}

	/// <summary>
	/// Captures one owner in a promoted method-neutral deployment stack and the live path that currently represents its payload.
	/// </summary>
	/// <remarks>
	/// The payload source is a read-only live reference only. It is not retained content and does not imply that the path will
	/// remain valid after capture.
	/// </remarks>
	public sealed class NativeStateCaptureDeploymentOwner
	{
		/// <summary>Creates one immutable promoted-owner observation.</summary>
		public NativeStateCaptureDeploymentOwner(string ownerKey, NativeStateCaptureDeploymentOwnerKind kind,
			bool currentWinner, string payloadSourcePath)
		{
			if (String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A deployment owner key is required.", nameof(ownerKey));
			if (!Enum.IsDefined(typeof(NativeStateCaptureDeploymentOwnerKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			OwnerKey = ownerKey;
			Kind = kind;
			CurrentWinner = currentWinner;
			PayloadSourcePath = payloadSourcePath ?? String.Empty;
		}

		public string OwnerKey { get; }
		public NativeStateCaptureDeploymentOwnerKind Kind { get; }
		public bool CurrentWinner { get; }
		public string PayloadSourcePath { get; }
	}

	/// <summary>
	/// Captures one promoted method-neutral deployment target with its ordered fallback-to-winner owner stack.
	/// </summary>
	public sealed class NativeStateCaptureDeploymentTarget
	{
		private readonly ReadOnlyCollection<NativeStateCaptureDeploymentOwner> _owners;

		/// <summary>Creates one immutable promoted deployment observation.</summary>
		public NativeStateCaptureDeploymentTarget(ModDeploymentTarget target, string deploymentPath,
			IEnumerable<NativeStateCaptureDeploymentOwner> owners)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			DeploymentPath = deploymentPath ?? String.Empty;
			List<NativeStateCaptureDeploymentOwner> copied = (owners ?? throw new ArgumentNullException(nameof(owners))).ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A deployment owner stack cannot contain null records.", nameof(owners));
			_owners = new ReadOnlyCollection<NativeStateCaptureDeploymentOwner>(copied);
		}

		public ModDeploymentTarget Target { get; }
		public string DeploymentPath { get; }
		public ReadOnlyCollection<NativeStateCaptureDeploymentOwner> Owners { get { return _owners; } }
		public string CurrentOwnerKey { get { return _owners.Count == 0 ? null : _owners[_owners.Count - 1].OwnerKey; } }
	}

	/// <summary>
	/// Captures one resolved live payload source for an active Virtual owner without changing Virtual deployment state.
	/// </summary>
	/// <remarks>The path remains a live reference until a later C7 stage retains and hashes its bytes.</remarks>
	public sealed class NativeStateCaptureVirtualPayloadSource
	{
		/// <summary>Creates one immutable active Virtual payload-source observation.</summary>
		public NativeStateCaptureVirtualPayloadSource(ModDeploymentTarget target, string ownerKey, string ownerReference,
			string payloadSourcePath)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			OwnerKey = ownerKey ?? String.Empty;
			OwnerReference = ownerReference ?? String.Empty;
			PayloadSourcePath = payloadSourcePath ?? String.Empty;
		}

		public ModDeploymentTarget Target { get; }
		public string OwnerKey { get; }
		public string OwnerReference { get; }
		public string PayloadSourcePath { get; }
	}

	/// <summary>
	/// Captures the native live replay XML and generated-payload sidecar locations associated with one scripted mod.
	/// </summary>
	/// <remarks>
	/// These paths are references only. C7 retention must copy/hash the required artifacts before promising local restoration.
	/// </remarks>
	public sealed class NativeStateCaptureReplayReference
	{
		/// <summary>Creates one immutable scripted replay reference.</summary>
		public NativeStateCaptureReplayReference(string modKey, string cachePath, string payloadDirectoryPath)
		{
			if (String.IsNullOrWhiteSpace(modKey))
				throw new ArgumentException("A native mod key is required.", nameof(modKey));
			ModKey = modKey;
			CachePath = cachePath ?? String.Empty;
			PayloadDirectoryPath = payloadDirectoryPath ?? String.Empty;
		}

		public string ModKey { get; }
		public string CachePath { get; }
		public string PayloadDirectoryPath { get; }
	}

	/// <summary>
	/// Detached plugin diagnostic used by the generic native-state capture layer.
	/// </summary>
	public sealed class NativeStateCapturePluginDiagnostic
	{
		/// <summary>Creates one detached plugin diagnostic.</summary>
		public NativeStateCapturePluginDiagnostic(PluginValidationIssueKind kind, PluginValidationSeverity severity)
		{
			Kind = kind;
			Severity = severity;
		}

		public PluginValidationIssueKind Kind { get; }
		public PluginValidationSeverity Severity { get; }
	}

	/// <summary>
	/// Detached plugin state copied from the native plugin manager snapshot.
	/// </summary>
	public sealed class NativeStateCapturePlugin
	{
		/// <summary>Creates one deeply detached plugin-state observation.</summary>
		public NativeStateCapturePlugin(string fileName, bool active, int priority, int? allocatedIndex, string modIndex,
			PluginParseStatus parseStatus, PluginAddressClass addressClass, PluginHeaderFlags headerFlags,
			PluginSpecialFlags specialFlags, bool effectiveMaster, int formVersion, IEnumerable<string> masters,
			IEnumerable<NativeStateCapturePluginDiagnostic> diagnostics)
		{
			FileName = fileName ?? String.Empty;
			Active = active;
			Priority = priority;
			AllocatedIndex = allocatedIndex;
			ModIndex = modIndex ?? String.Empty;
			ParseStatus = parseStatus;
			AddressClass = addressClass;
			HeaderFlags = headerFlags;
			SpecialFlags = specialFlags;
			EffectiveMaster = effectiveMaster;
			FormVersion = formVersion;
			Masters = new ReadOnlyCollection<string>((masters ?? Enumerable.Empty<string>()).Select(x => x ?? String.Empty).ToList());
			Diagnostics = new ReadOnlyCollection<NativeStateCapturePluginDiagnostic>((diagnostics ?? Enumerable.Empty<NativeStateCapturePluginDiagnostic>()).ToList());
		}

		public string FileName { get; }
		public bool Active { get; }
		public int Priority { get; }
		public int? AllocatedIndex { get; }
		public string ModIndex { get; }
		public PluginParseStatus ParseStatus { get; }
		public PluginAddressClass AddressClass { get; }
		public PluginHeaderFlags HeaderFlags { get; }
		public PluginSpecialFlags SpecialFlags { get; }
		public bool EffectiveMaster { get; }
		public int FormVersion { get; }
		public ReadOnlyCollection<string> Masters { get; }
		public ReadOnlyCollection<NativeStateCapturePluginDiagnostic> Diagnostics { get; }
	}

	/// <summary>
	/// Immutable game-instance native read used as the generic input boundary for later Local Collection capture stages.
	/// </summary>
	/// <remarks>
	/// The snapshot contains only detached native observations and live artifact references. It owns no native state, copies no
	/// payload bytes and does not imply that referenced paths are independently retained.
	/// </remarks>
	public sealed class NativeStateCaptureSnapshot
	{
		/// <summary>Creates one immutable generic native-state capture.</summary>
		public NativeStateCaptureSnapshot(InstallLogReadSnapshot installLog, VirtualModReadSnapshot virtualState,
			IEnumerable<NativeStateCaptureRoot> roots, IEnumerable<NativeStateCaptureDeploymentTarget> deploymentTargets,
			NativeStateCaptureCoverage deploymentCoverage, IEnumerable<NativeStateCaptureReplayReference> replayReferences,
			IEnumerable<NativeStateCapturePlugin> plugins, NativeStateCaptureCoverage pluginCoverage,
			IEnumerable<NativeStateCaptureIssue> issues)
			: this(installLog, virtualState, new NativeStateCaptureVirtualPayloadSource[0], roots, deploymentTargets,
				deploymentCoverage, replayReferences, plugins, pluginCoverage, issues)
		{
		}

		/// <summary>Creates one immutable generic native-state capture including resolved active Virtual payload sources.</summary>
		public NativeStateCaptureSnapshot(InstallLogReadSnapshot installLog, VirtualModReadSnapshot virtualState,
			IEnumerable<NativeStateCaptureVirtualPayloadSource> activeVirtualPayloadSources,
			IEnumerable<NativeStateCaptureRoot> roots, IEnumerable<NativeStateCaptureDeploymentTarget> deploymentTargets,
			NativeStateCaptureCoverage deploymentCoverage, IEnumerable<NativeStateCaptureReplayReference> replayReferences,
			IEnumerable<NativeStateCapturePlugin> plugins, NativeStateCaptureCoverage pluginCoverage,
			IEnumerable<NativeStateCaptureIssue> issues)
		{
			InstallLog = installLog ?? throw new ArgumentNullException(nameof(installLog));
			VirtualState = virtualState ?? throw new ArgumentNullException(nameof(virtualState));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), deploymentCoverage))
				throw new ArgumentOutOfRangeException(nameof(deploymentCoverage));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), pluginCoverage))
				throw new ArgumentOutOfRangeException(nameof(pluginCoverage));
			ActiveVirtualPayloadSources = Copy(activeVirtualPayloadSources, nameof(activeVirtualPayloadSources));
			Roots = Copy(roots, nameof(roots));
			DeploymentTargets = Copy(deploymentTargets, nameof(deploymentTargets));
			DeploymentCoverage = deploymentCoverage;
			ReplayReferences = Copy(replayReferences, nameof(replayReferences));
			Plugins = Copy(plugins, nameof(plugins));
			PluginCoverage = pluginCoverage;
			Issues = Copy(issues, nameof(issues));
		}

		public InstallLogReadSnapshot InstallLog { get; }
		public VirtualModReadSnapshot VirtualState { get; }
		public ReadOnlyCollection<NativeStateCaptureVirtualPayloadSource> ActiveVirtualPayloadSources { get; }
		public ReadOnlyCollection<NativeStateCaptureRoot> Roots { get; }
		public ReadOnlyCollection<NativeStateCaptureDeploymentTarget> DeploymentTargets { get; }
		public NativeStateCaptureCoverage DeploymentCoverage { get; }
		public ReadOnlyCollection<NativeStateCaptureReplayReference> ReplayReferences { get; }
		public ReadOnlyCollection<NativeStateCapturePlugin> Plugins { get; }
		public NativeStateCaptureCoverage PluginCoverage { get; }
		public ReadOnlyCollection<NativeStateCaptureIssue> Issues { get; }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A native-state capture cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
