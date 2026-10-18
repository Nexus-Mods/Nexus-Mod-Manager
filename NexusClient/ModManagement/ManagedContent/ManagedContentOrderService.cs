using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.ModManagement.InstallationLog;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>Explicit reviewed order publication through the existing native target reservation and authority reload.</summary>
	public sealed class ManagedContentOrderService
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _storage;
		private readonly IGameMode _mode;
		private readonly IniOrderDocumentRule _rule;
		private readonly string _documentsRoot;

		/// <summary>Restricts the configurable writer to the managed-content host; Witcher retains its compiled edition policy.</summary>
		public static bool Supports(IGameMode mode) => mode is DataDrivenManagedContentGameMode &&
			(mode as IManagedContentOrderDocumentProvider)?.OrderDocument?.Editing == IniOrderDocumentRule.EditProviderId;

		/// <summary>Captures one immutable capability without reading, repairing or creating game files.</summary>
		public ManagedContentOrderService(ServiceManager services, GameStorageService storage, string documentsRoot)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_storage = storage ?? throw new ArgumentNullException(nameof(storage));
			_mode = services.ModManager.GameMode;
			if (!Supports(_mode)) throw new ArgumentException("The current mode does not declare reviewed order editing.");
			_rule = ((IManagedContentOrderDocumentProvider)_mode).OrderDocument;
			_documentsRoot = documentsRoot;
		}

		public string ModeId => _mode.ModeId;

		/// <summary>Observes pending publication without acquiring a writer or changing recovery state.</summary>
		public IniOrderRecoveryInspection InspectRecovery() => Publisher(CurrentRoot()).InspectRecovery();

		/// <summary>Re-resolves the exact target under validated native authority and independently verifies the reviewed preimage.</summary>
		public IniOrderPublishResult Publish(IniOrderChangePreview preview, string expectedPhysicalRoot, bool explicitlyApproved)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (!explicitlyApproved) return new IniOrderPublishResult(IniOrderPublishStatus.Blocked, "Explicit approval is required.", null);
			return WithAuthority(expectedPhysicalRoot, (authority, lease, root) =>
			{
				RequireUnownedOrderFile(root);
				return Publisher(root).Publish(preview, authority, lease, true, () =>
				{
					RequireUnlinkedPath(root);
					return SameRoot(root, CurrentRoot());
				});
			});
		}

		/// <summary>Acknowledges only verified original/published images; conflicts and unknown markers remain intact.</summary>
		public IniOrderRecoveryInspection AcknowledgeRecovery(string expectedPhysicalRoot, bool explicitlyApproved)
		{
			if (!explicitlyApproved) return InspectRecovery();
			return WithAuthority(expectedPhysicalRoot, (authority, lease, root) =>
			{
				RequireUnlinkedPath(root);
				return Publisher(root).AcknowledgeRecovery(authority, lease);
			});
		}

		/// <summary>Uses the same cross-process lock, native reload and storage identity checks as native mod operations.</summary>
		private T WithAuthority<T>(string expectedPhysicalRoot, Func<CollectionTargetAuthority, CollectionTargetMutationLease, string, T> work)
		{
			string root = CurrentRoot();
			if (!SameRoot(root, expectedPhysicalRoot)) throw new InvalidOperationException("The physical game root changed since review.");
			GameStoragePathSet paths = _storage.FromGameMode(_mode);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_storage).Resolve(paths);
			if (!String.Equals(authority.GameId, _mode.ModeId, StringComparison.OrdinalIgnoreCase) ||
				!SameRoot(root, authority.CanonicalGameInstallPath)) throw new InvalidOperationException("The current native target differs from the reviewed game.");
			using (CollectionTargetMutationLease lease = CollectionTargetMutationLeaseManager.Shared.Acquire(authority))
			{
				new CollectionTargetOwnershipAuthorityValidator(_storage, _services).ValidateAndReload(lease, authority, paths);
				if (!SameRoot(root, CurrentRoot())) throw new InvalidOperationException("The current game root changed during native reload.");
				return work(authority, lease, root);
			}
		}

		/// <summary>Retains the captured mode/rule identity across native reloads and modal reviews.</summary>
		private string CurrentRoot()
		{
			if (!ReferenceEquals(_mode, _services.ModManager.GameMode) || !Supports(_mode) ||
				!ReferenceEquals(_rule, ((IManagedContentOrderDocumentProvider)_mode).OrderDocument))
				throw new InvalidOperationException("The configured order capability changed since initialization.");
			return Path.GetFullPath(_mode.GameModeEnvironmentInfo.InstallationPath);
		}

		/// <summary>Uses fixed shared recovery identifiers rather than allowing JSON to rename pending state.</summary>
		private IniOrderPublisher Publisher(string root) => new IniOrderPublisher(_rule.ResolvePath(root, _documentsRoot),
			_rule.Format, _mode.ModeId, ".nmm-order.pending", ".nmm-order.", "nmm-ce.ini-order.pending/1");

		/// <summary>Rejects observed links below the explicitly configured root before any publication mutation.</summary>
		private void RequireUnlinkedPath(string root)
		{
			string stop = Path.GetFullPath(_rule.FileRoot == "GameRoot" ? root : _documentsRoot);
			string current = _rule.ResolvePath(root, _documentsRoot);
			while (!SameRoot(current, stop))
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new IOException("An order-file path segment is a link; publication requires a direct existing file.");
				current = Path.GetDirectoryName(current);
				if (String.IsNullOrEmpty(current)) throw new IOException("The order path is outside its configured root.");
			}
		}

		/// <summary>Does not bypass native payload or scripted INI histories when editing an external game order document.</summary>
		private void RequireUnownedOrderFile(string root)
		{
			InstallLogReadSnapshot native = _services.ModManager.InstallationLog.GetCommittedStateSnapshot();
			if (native == null) throw new InvalidOperationException("Native file ownership could not be inspected.");
			string target = _rule.ResolvePath(root, _documentsRoot);
			string prefix = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
			string relative = target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? target.Substring(prefix.Length) : null;
			Func<ModDeploymentTarget, bool> matches = item => relative != null && item != null &&
				String.Equals(item.RelativePath, relative, StringComparison.OrdinalIgnoreCase);
			if (native.Files.Any(file => matches(file.Target) && file.OwnerKeys.Any(owner => owner != native.OriginalValuesKey)) ||
				native.DeploymentTargets.Any(file => matches(file.Target) && file.OwnerKeys.Any(owner => owner != native.OriginalValuesKey)) ||
				_services.ModManager.VirtualModActivator.GetReadSnapshot().Links.Any(link => matches(link.Target)) ||
				native.IniEdits.Any(edit => String.Equals(Path.GetFileName(edit.File), Path.GetFileName(target), StringComparison.OrdinalIgnoreCase) &&
					edit.Values.Any(value => value.OwnerKey != native.OriginalValuesKey)))
				throw new InvalidOperationException("The order file has native payload or INI ownership history. Use its native workflow before standalone order editing.");
		}

		/// <summary>Compares canonical directory spellings without changing persisted paths.</summary>
		private static bool SameRoot(string left, string right) => !String.IsNullOrWhiteSpace(left) && !String.IsNullOrWhiteSpace(right) &&
			String.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
	}
}
