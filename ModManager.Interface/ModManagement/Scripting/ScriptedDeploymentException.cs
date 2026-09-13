using System;
using System.Runtime.Serialization;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Represents a failure in Direct or promoted deployment that must abort the owning scripted installation transaction.
	/// </summary>
	[Serializable]
	public sealed class ScriptedDeploymentException : Exception
	{
		/// <summary>
		/// Initializes a deployment failure with a descriptive message.
		/// </summary>
		public ScriptedDeploymentException(string p_strMessage)
			: base(p_strMessage)
		{
		}

		/// <summary>
		/// Initializes a deployment failure with the underlying filesystem or coordinator exception.
		/// </summary>
		public ScriptedDeploymentException(string p_strMessage, Exception p_exInnerException)
			: base(p_strMessage, p_exInnerException)
		{
		}

		/// <summary>
		/// Rehydrates a deployment failure crossing the scripted-installer AppDomain boundary.
		/// </summary>
		private ScriptedDeploymentException(SerializationInfo p_sifInfo, StreamingContext p_sctContext)
			: base(p_sifInfo, p_sctContext)
		{
		}
	}
}
