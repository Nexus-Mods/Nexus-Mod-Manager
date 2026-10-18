using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.ManagedContent;

namespace Nexus.Client.ModManagement.ManagedContent
{
	/// <summary>One detached read-only component and optional game-order observation; it never authorizes mutations.</summary>
	public sealed class ManagedContentComponentViewSnapshot
	{
		/// <summary>Groups observations obtained from the same configured root and rules.</summary>
		internal ManagedContentComponentViewSnapshot(string physicalRoot, ManagedContentNativeComponentIndex index,
			ManagedContentComponentDiskObservation disk)
			: this(physicalRoot, index, disk, null, null)
		{
		}

		/// <summary>Joins an optional order-file observation without conflating it with native ownership.</summary>
		internal ManagedContentComponentViewSnapshot(string physicalRoot, ManagedContentNativeComponentIndex index,
			ManagedContentComponentDiskObservation disk, IniOrderDocumentRule orderRule, IniOrderFileObservation order)
		{
			PhysicalRoot = physicalRoot;
			Index = index;
			Disk = disk;
			OrderRule = orderRule;
			Order = order;
			Rows = ManagedContentOrderProjector.Project(index, orderRule, order);
		}

		public string PhysicalRoot { get; }
		public ManagedContentNativeComponentIndex Index { get; }
		public ManagedContentComponentDiskObservation Disk { get; }
		public IniOrderDocumentRule OrderRule { get; }
		public IniOrderFileObservation Order { get; }
		public IReadOnlyList<ManagedContentComponentViewRow> Rows { get; }
	}

	/// <summary>Joins fresh native ownership with a bounded disk scan using one immutable configuration.</summary>
	public sealed class ManagedContentComponentViewService
	{
		private readonly IGameMode _gameMode;
		private readonly Func<NativeStateCaptureSnapshot> _capture;
		private readonly IReadOnlyList<ManagedContentLayoutRule> _rules;
		private readonly IniOrderDocumentRule _orderRule;
		private readonly Func<string> _documentsRoot;

		/// <summary>Captures the optional component capability; the caller schedules reads outside UI painting.</summary>
		public ManagedContentComponentViewService(IGameMode gameMode, Func<NativeStateCaptureSnapshot> capture)
			: this(gameMode, capture, () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
		{
		}

		/// <summary>Uses the existing environment's resolved Documents path, including redirected user profiles.</summary>
		public ManagedContentComponentViewService(IGameMode gameMode, Func<NativeStateCaptureSnapshot> capture, Func<string> documentsRoot)
		{
			_gameMode = gameMode ?? throw new ArgumentNullException(nameof(gameMode));
			_capture = capture ?? throw new ArgumentNullException(nameof(capture));
			IManagedContentComponentProvider provider = gameMode as IManagedContentComponentProvider;
			if (provider == null) throw new ArgumentException("The game mode has no managed component capability.", nameof(gameMode));
			if (provider.ComponentRules == null) throw new ArgumentException("Component rules are unavailable.", nameof(gameMode));
			ManagedContentLayoutRule[] rules = provider.ComponentRules.Take(33).ToArray();
			if (rules.Length == 0 || rules.Length > 32 || rules.Any(rule => rule == null))
				throw new ArgumentException("Component viewing requires 1 to 32 configured layout rules.", nameof(gameMode));
			_rules = Array.AsReadOnly(rules);
			_orderRule = (gameMode as IManagedContentOrderDocumentProvider)?.OrderDocument;
			_documentsRoot = documentsRoot ?? throw new ArgumentNullException(nameof(documentsRoot));
		}

		/// <summary>Whether this mode explicitly declares a read-only order-document capability.</summary>
		public bool HasOrderDocument => _orderRule != null;

		/// <summary>Reads detached observations; the caller must reject results invalidated by concurrent native work.</summary>
		public ManagedContentComponentViewSnapshot Read()
		{
			string root = GetRoot();
			NativeStateCaptureSnapshot native = _capture();
			ManagedContentComponentDiskObservation disk = ManagedContentComponentDiskScan.Scan(root, _rules);
			ManagedContentNativeComponentIndex index = ManagedContentNativeComponentIndexer.Build(native, root, _rules, disk);
			if (!String.Equals(root, GetRoot(), StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The configured game root changed during the component read.");
			IniOrderFileObservation order = ReadOrderDocument();
			if (!String.Equals(root, GetRoot(), StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The configured game root changed during the order-file read.");
			return new ManagedContentComponentViewSnapshot(root, index, disk, _orderRule, order);
		}

		/// <summary>Observes configured settings without letting missing/unreadable settings suppress the component inventory.</summary>
		private IniOrderFileObservation ReadOrderDocument()
		{
			if (_orderRule == null) return null;
			try
			{
				string path = _orderRule.ResolvePath(_gameMode.GameModeEnvironmentInfo.InstallationPath,
					_orderRule.FileRoot == "Documents" ? _documentsRoot() : null);
				return IniOrderFileReader.Read(path, _orderRule.Format);
			}
			catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException ||
				error is System.Security.SecurityException || error is NotSupportedException)
			{
				return new IniOrderFileObservation(null, IniOrderReadStatus.ReadFailed, null, null, null, null, null, error.Message);
			}
		}

		/// <summary>Resolves the current native Data root without creating or changing any directories.</summary>
		private string GetRoot()
		{
			string root = ModDeploymentTargetResolver.GetPhysicalRootPath(_gameMode, ModDeploymentRoot.Data);
			if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
				throw new InvalidOperationException("Component viewing requires an absolute configured Data root.");
			return Path.GetFullPath(root);
		}
	}
}
