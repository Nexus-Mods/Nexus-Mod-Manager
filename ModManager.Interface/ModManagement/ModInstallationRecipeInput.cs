using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Carries the immutable native operation context for one explicit mod-installation recipe request.
	/// </summary>
	/// <remarks>
	/// The recipe fingerprint is an opaque normalized identity supplied by the recipe-owning layer. C5.4 may attach an
	/// immutable snapshot of translated native operation intent, but the contract never carries execution handles, mutable
	/// installer services or Collection persistence types. Execution remains a later native-installer responsibility.
	/// </remarks>
	public sealed class ModInstallationRecipeInput
	{
		private readonly ReadOnlyCollection<ScriptedInstallOperation> m_rocNativeOperations;
		private readonly bool m_booOperationIdentityRebound;

		/// <summary>
		/// Initializes an explicit recipe input from the already-captured native operation identity and validation metadata.
		/// </summary>
		/// <param name="operationIdentity">The native operation/attempt, target, install context and recipe identity.</param>
		/// <param name="validation">The validated expected content, adapter capabilities and relative paths.</param>
		public ModInstallationRecipeInput(ModOperationIdentity operationIdentity, ModInstallationRecipeValidation validation)
			: this(operationIdentity, validation, null, false)
		{
		}

		/// <summary>
		/// Initializes one immutable recipe input, optionally with a translated native-operation snapshot.
		/// </summary>
		private ModInstallationRecipeInput(ModOperationIdentity operationIdentity, ModInstallationRecipeValidation validation,
			IEnumerable<ScriptedInstallOperation> nativeOperations, bool operationIdentityRebound)
		{
			if (operationIdentity == null)
				throw new ArgumentNullException(nameof(operationIdentity));
			if (validation == null)
				throw new ArgumentNullException(nameof(validation));
			if (operationIdentity.Origin != ModOperationOrigin.Collection &&
				operationIdentity.Origin != ModOperationOrigin.LocalRestore &&
				operationIdentity.Origin != ModOperationOrigin.Recovery)
			{
				throw new ArgumentException("Explicit recipe input is limited to Collection, LocalRestore and Recovery operation scopes.", nameof(operationIdentity));
			}

			string recipeFingerprint = operationIdentity.Fingerprint.RecipeFingerprint;
			if (string.IsNullOrWhiteSpace(recipeFingerprint))
				throw new ArgumentException("Explicit recipe input requires a recipe fingerprint.", nameof(operationIdentity));
			if (!StringComparer.Ordinal.Equals(recipeFingerprint, recipeFingerprint.Trim()))
				throw new ArgumentException("Recipe fingerprints must not contain leading or trailing whitespace.", nameof(operationIdentity));
			if (validation.InstallContext.Method != operationIdentity.Fingerprint.InstallMethod ||
				validation.InstallContext.InstallRoot != operationIdentity.Fingerprint.InstallRoot)
			{
				throw new ArgumentException("Recipe validation method/root must match the native operation fingerprint.", nameof(validation));
			}

			OperationIdentity = operationIdentity;
			TargetFingerprint = operationIdentity.Fingerprint.TargetFingerprint;
			InstallContext = new ModInstallContext(operationIdentity.Fingerprint.InstallMethod, operationIdentity.Fingerprint.InstallRoot);
			RecipeFingerprint = recipeFingerprint;
			Validation = validation;
			m_booOperationIdentityRebound = operationIdentityRebound;

			if (nativeOperations != null)
			{
				var copiedOperations = new List<ScriptedInstallOperation>();
				foreach (ScriptedInstallOperation operation in nativeOperations)
				{
					if (operation == null)
						throw new ArgumentException("A translated native recipe plan cannot contain null operations.", nameof(nativeOperations));
					copiedOperations.Add(operation);
				}
				if (copiedOperations.Count == 0)
					throw new ArgumentException("A translated native recipe plan requires at least one operation.", nameof(nativeOperations));
				m_rocNativeOperations = new ReadOnlyCollection<ScriptedInstallOperation>(copiedOperations);
			}
		}

		/// <summary>
		/// Gets the immutable native operation/attempt identity supplied for the request.
		/// </summary>
		public ModOperationIdentity OperationIdentity { get; }

		/// <summary>
		/// Gets the exact target fingerprint captured by the native operation request.
		/// </summary>
		public string TargetFingerprint { get; }

		/// <summary>
		/// Gets the immutable native install method and root captured by the operation request.
		/// </summary>
		public ModInstallContext InstallContext { get; }

		/// <summary>
		/// Gets the exact normalized recipe fingerprint supplied by the recipe-owning layer.
		/// </summary>
		public string RecipeFingerprint { get; }

		/// <summary>
		/// Gets the validated non-executable metadata that establishes the recipe trust boundary.
		/// </summary>
		public ModInstallationRecipeValidation Validation { get; }

		/// <summary>
		/// Gets whether C5 translation has attached a native typed operation plan to this immutable input.
		/// </summary>
		public bool HasNativePlan
		{
			get { return m_rocNativeOperations != null; }
		}

		/// <summary>
		/// Gets the translated native operation intent, or <c>null</c> until a supported adapter has translated the recipe.
		/// </summary>
		public IReadOnlyList<ScriptedInstallOperation> NativeOperations
		{
			get { return m_rocNativeOperations; }
		}

		/// <summary>
		/// Returns a new immutable recipe input carrying a snapshot of translated native operation intent.
		/// </summary>
		/// <param name="nativeOperations">The ordered native operations produced by a validated recipe adapter.</param>
		/// <returns>A new recipe input preserving the same operation identity and validation metadata.</returns>
		internal ModInstallationRecipeInput WithNativePlan(IEnumerable<ScriptedInstallOperation> nativeOperations)
		{
			if (HasNativePlan)
				throw new InvalidOperationException("A native recipe plan has already been attached to this input.");
			if (nativeOperations == null)
				throw new ArgumentNullException(nameof(nativeOperations));

			return new ModInstallationRecipeInput(OperationIdentity, Validation, nativeOperations, m_booOperationIdentityRebound);
		}

		/// <summary>
		/// Returns this translated recipe plan bound to another exact Collection-family native operation identity.
		/// </summary>
		/// <param name="operationIdentity">The final native operation/attempt identity established by the owning workflow.</param>
		/// <returns>A new immutable recipe input preserving the validation metadata and translated native operations.</returns>
		/// <remarks>
		/// Rebinding changes only the operation/attempt identity. Target, install method/root and recipe fingerprint must remain
		/// byte-for-byte equivalent to the already reviewed translated plan. A rebound result cannot be rebound again.
		/// </remarks>
		public ModInstallationRecipeInput ForOperationIdentity(ModOperationIdentity operationIdentity)
		{
			if (operationIdentity == null)
				throw new ArgumentNullException(nameof(operationIdentity));
			if (!HasNativePlan)
				throw new InvalidOperationException("A recipe input must have a translated native plan before its operation identity can be rebound.");
			if (m_booOperationIdentityRebound)
				throw new InvalidOperationException("A translated recipe input can be rebound to its final native operation identity only once.");
			if (operationIdentity.Origin != ModOperationOrigin.Collection &&
				operationIdentity.Origin != ModOperationOrigin.LocalRestore &&
				operationIdentity.Origin != ModOperationOrigin.Recovery)
			{
				throw new ArgumentException("Recipe operation identity rebinding is limited to Collection, LocalRestore and Recovery operation scopes.", nameof(operationIdentity));
			}

			ModOperationFingerprint fingerprint = operationIdentity.Fingerprint;
			if (!StringComparer.Ordinal.Equals(TargetFingerprint, fingerprint.TargetFingerprint) ||
				InstallContext.Method != fingerprint.InstallMethod ||
				InstallContext.InstallRoot != fingerprint.InstallRoot ||
				!StringComparer.Ordinal.Equals(RecipeFingerprint, fingerprint.RecipeFingerprint))
			{
				throw new ArgumentException("The rebound operation identity must preserve the exact target, install context and recipe fingerprint of the translated recipe.", nameof(operationIdentity));
			}

			return new ModInstallationRecipeInput(operationIdentity, Validation, m_rocNativeOperations, true);
		}
	}
}
