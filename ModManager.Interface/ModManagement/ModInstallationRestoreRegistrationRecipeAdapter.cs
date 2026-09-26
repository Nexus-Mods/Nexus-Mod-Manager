using System;
using System.IO;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.ModManagement
{
	/// <summary>Builds the narrow registration-only native recipe used by Local Collection restore/recovery.</summary>
	public sealed class ModInstallationRestoreRegistrationRecipeAdapter
	{
		public const string AdapterId = "nmm-ce.local-restore-registration";
		public const int AdapterVersion = 1;
		public const string CapabilityId = "local-restore-registration";
		public const int CapabilityVersion = 1;

		/// <summary>Attaches exactly one registration-only native operation after validating the restore-specific contract.</summary>
		public ModInstallationRecipeInput Translate(ModInstallationRecipeInput input)
		{
			if (input == null)
				throw new ArgumentNullException(nameof(input));
			if (input.HasNativePlan)
				throw new InvalidOperationException("A restore-registration recipe cannot replace an existing native plan.");
			if (input.OperationIdentity.Origin != ModOperationOrigin.LocalRestore &&
				input.OperationIdentity.Origin != ModOperationOrigin.Recovery)
				throw new InvalidDataException("Registration-only installation is restricted to LocalRestore or Recovery native origins.");
			if (!StringComparer.Ordinal.Equals(input.Validation.AdapterId, AdapterId) ||
				input.Validation.AdapterVersion != AdapterVersion)
				throw new InvalidDataException("The Local restore registration adapter contract/version does not match this translator.");

			bool capabilityFound = false;
			foreach (ModInstallationRecipeCapability capability in input.Validation.Capabilities)
			{
				if (!StringComparer.Ordinal.Equals(capability.CapabilityId, CapabilityId))
					continue;
				if (capability.Version != CapabilityVersion)
					throw new InvalidDataException("The Local restore registration capability version is unsupported.");
				capabilityFound = true;
			}
			if (!capabilityFound)
				throw new InvalidDataException("The Local restore registration capability was not declared by recipe validation.");
			if (input.Validation.Paths.Count != 0)
				throw new InvalidDataException("A registration-only Local restore recipe cannot declare file paths.");

			return input.WithNativePlan(new ScriptedInstallOperation[] { new RestoreNativeRegistrationOperation() });
		}
	}
}
