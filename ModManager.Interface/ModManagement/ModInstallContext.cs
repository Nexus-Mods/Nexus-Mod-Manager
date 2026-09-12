using System;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Captures the immutable deployment choices for a single mod operation.
	/// </summary>
	public sealed class ModInstallContext
	{
		/// <summary>
		/// Initializes a new install context.
		/// </summary>
		/// <param name="method">The deployment strategy.</param>
		/// <param name="installRoot">The operation-level install root.</param>
		public ModInstallContext(ModInstallMethod method, ModInstallRoot installRoot)
		{
			if (!Enum.IsDefined(typeof(ModInstallMethod), method))
				throw new ArgumentOutOfRangeException(nameof(method));
			if (installRoot != ModInstallRoot.Data && installRoot != ModInstallRoot.GameRoot)
				throw new ArgumentOutOfRangeException(nameof(installRoot));

			Method = method;
			InstallRoot = installRoot;
		}

		/// <summary>
		/// Gets the deployment strategy captured for the operation.
		/// </summary>
		public ModInstallMethod Method { get; }

		/// <summary>
		/// Gets the operation-level install root captured for the operation.
		/// </summary>
		public ModInstallRoot InstallRoot { get; }
	}
}
