using System;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Identifies a method-neutral game deployment destination.
	/// </summary>
	public sealed class ModDeploymentTarget : IEquatable<ModDeploymentTarget>
	{
		/// <summary>
		/// Initializes a canonical deployment target. Callers should use <see cref="ModDeploymentTargetResolver"/>.
		/// </summary>
		internal ModDeploymentTarget(ModDeploymentRoot root, string relativePath)
		{
			Root = root;
			RelativePath = relativePath;
		}

		/// <summary>
		/// Gets the physical deployment root.
		/// </summary>
		public ModDeploymentRoot Root { get; }

		/// <summary>
		/// Gets the canonical path relative to <see cref="Root"/>.
		/// </summary>
		public string RelativePath { get; }

		/// <inheritdoc />
		public bool Equals(ModDeploymentTarget other)
		{
			return !ReferenceEquals(other, null) &&
				Root == other.Root &&
				StringComparer.OrdinalIgnoreCase.Equals(RelativePath, other.RelativePath);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as ModDeploymentTarget);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return ((int)Root * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(RelativePath ?? string.Empty);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Root + ":" + RelativePath;
		}
	}
}
