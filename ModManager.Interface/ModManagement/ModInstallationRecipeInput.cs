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

		/// <summary>
		/// Initializes an explicit recipe input from the already-captured native operation identity and validation metadata.
		/// </summary>
		/// <param name="operationIdentity">The native operation/attempt, target, install context and recipe identity.</param>
		/// <param name="validation">The validated expected content, adapter capabilities and relative paths.</param>
		public ModInstallationRecipeInput(ModOperationIdentity operationIdentity, ModInstallationRecipeValidation validation)
			: this(operationIdentity, validation, null)
		{
		}

		/// <summary>
		/// Initializes one immutable recipe input, optionally with a translated native-operation snapshot.
		/// </summary>
		private ModInstallationRecipeInput(ModOperationIdentity operationIdentity, ModInstallationRecipeValidation validation,
			IEnumerable<ScriptedInstallOperation> nativeOperations)
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

			return new ModInstallationRecipeInput(OperationIdentity, Validation, nativeOperations);
		}
	}
}
