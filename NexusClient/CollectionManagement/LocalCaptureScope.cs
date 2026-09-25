using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies a declared area of NMM-managed state covered by a Local Collection capture.
	/// </summary>
	public enum LocalCaptureScopeArea
	{
		Unknown = 0,
		ManagedModState = 1,
		ModArchives = 2,
		InstallerReplayAndGeneratedPayloads = 3,
		NativeConfigurationEffects = 4,
		PluginState = 5,
		FileOwnershipAndFallbackPayloads = 6,
		UserMetadata = 7
	}

	/// <summary>
	/// Immutable versioned declaration of the NMM-managed state areas a Local Collection capture promises to describe.
	/// </summary>
	/// <remarks>
	/// Scope is intentionally explicit and versioned. It is not a claim to snapshot the entire game directory, saves,
	/// external tools or arbitrary unmanaged content. Specific known exclusions are recorded separately on the capture.
	/// </remarks>
	public sealed class LocalCaptureScope : IEquatable<LocalCaptureScope>
	{
		/// <summary>Gets the current Local Collection capture-scope contract version.</summary>
		public const int CurrentVersion = 1;

		private readonly ReadOnlyCollection<LocalCaptureScopeArea> _areas;

		/// <summary>
		/// Creates a capture scope.
		/// </summary>
		public LocalCaptureScope(int version, IEnumerable<LocalCaptureScopeArea> areas)
		{
			if (version <= 0)
				throw new ArgumentOutOfRangeException(nameof(version), "A positive capture scope version is required.");
			if (areas == null)
				throw new ArgumentNullException(nameof(areas));

			List<LocalCaptureScopeArea> copied = new List<LocalCaptureScopeArea>();
			HashSet<LocalCaptureScopeArea> unique = new HashSet<LocalCaptureScopeArea>();
			foreach (LocalCaptureScopeArea area in areas)
			{
				if (!Enum.IsDefined(typeof(LocalCaptureScopeArea), area) || area == LocalCaptureScopeArea.Unknown)
					throw new ArgumentOutOfRangeException(nameof(areas), "Capture scope contains an unknown area.");
				if (!unique.Add(area))
					throw new ArgumentException("Capture scope cannot contain the same area more than once.", nameof(areas));

				copied.Add(area);
			}

			if (copied.Count == 0)
				throw new ArgumentException("A Local Collection capture must declare at least one scope area.", nameof(areas));

			copied.Sort();
			Version = version;
			_areas = new ReadOnlyCollection<LocalCaptureScopeArea>(copied);
		}

		/// <summary>
		/// Gets the version of the capture-scope contract.
		/// </summary>
		public int Version { get; }

		/// <summary>
		/// Gets the canonically ordered state areas covered by this scope declaration.
		/// </summary>
		public ReadOnlyCollection<LocalCaptureScopeArea> Areas
		{
			get { return _areas; }
		}

		/// <summary>
		/// Gets whether the scope includes the specified state area.
		/// </summary>
		public bool Contains(LocalCaptureScopeArea area)
		{
			return _areas.Contains(area);
		}

		/// <inheritdoc />
		public bool Equals(LocalCaptureScope other)
		{
			if (ReferenceEquals(other, null) || Version != other.Version || _areas.Count != other._areas.Count)
				return false;

			for (int index = 0; index < _areas.Count; index++)
			{
				if (_areas[index] != other._areas[index])
					return false;
			}

			return true;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as LocalCaptureScope);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = Version;
				foreach (LocalCaptureScopeArea area in _areas)
					hashCode = (hashCode * 397) ^ (int)area;
				return hashCode;
			}
		}
	}
}
