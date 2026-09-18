using System;

namespace Nexus.Client.ModManagement.Operations
{
	/// <summary>
	/// Captures the immutable target, install context and recipe identity of one native mod-operation request.
	/// </summary>
	/// <remarks>
	/// Target and recipe tokens are opaque fingerprints produced by their owning layers. This type deliberately
	/// does not define cross-process target authority or interpret Collection recipe data.
	/// </remarks>
	public sealed class ModOperationFingerprint : IEquatable<ModOperationFingerprint>
	{
		/// <summary>
		/// Initializes a native mod-operation fingerprint.
		/// </summary>
		/// <param name="targetFingerprint">The stable target fingerprint supplied by the caller.</param>
		/// <param name="installContext">The immutable native install method and root for the operation.</param>
		/// <param name="recipeFingerprint">The optional normalized recipe identity, or <c>null</c> for an ordinary native operation.</param>
		public ModOperationFingerprint(string targetFingerprint, ModInstallContext installContext, string recipeFingerprint)
		{
			if (string.IsNullOrWhiteSpace(targetFingerprint))
				throw new ArgumentException("A target fingerprint is required.", nameof(targetFingerprint));
			if (installContext == null)
				throw new ArgumentNullException(nameof(installContext));
			if (recipeFingerprint != null && string.IsNullOrWhiteSpace(recipeFingerprint))
				throw new ArgumentException("A recipe fingerprint must be non-empty when supplied.", nameof(recipeFingerprint));

			TargetFingerprint = targetFingerprint;
			InstallMethod = installContext.Method;
			InstallRoot = installContext.InstallRoot;
			RecipeFingerprint = recipeFingerprint;
		}

		/// <summary>
		/// Gets the target fingerprint supplied by the caller.
		/// </summary>
		public string TargetFingerprint { get; }

		/// <summary>
		/// Gets the captured native installation method.
		/// </summary>
		public ModInstallMethod InstallMethod { get; }

		/// <summary>
		/// Gets the captured native installation root.
		/// </summary>
		public ModInstallRoot InstallRoot { get; }

		/// <summary>
		/// Gets the normalized recipe identity, or <c>null</c> when the request has no explicit recipe.
		/// </summary>
		public string RecipeFingerprint { get; }

		/// <inheritdoc />
		public bool Equals(ModOperationFingerprint other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.Ordinal.Equals(TargetFingerprint, other.TargetFingerprint) &&
				InstallMethod == other.InstallMethod &&
				InstallRoot == other.InstallRoot &&
				StringComparer.Ordinal.Equals(RecipeFingerprint, other.RecipeFingerprint);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as ModOperationFingerprint);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = StringComparer.Ordinal.GetHashCode(TargetFingerprint ?? string.Empty);
				hashCode = (hashCode * 397) ^ (int)InstallMethod;
				hashCode = (hashCode * 397) ^ (int)InstallRoot;
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(RecipeFingerprint ?? string.Empty);
				return hashCode;
			}
		}
	}
}
