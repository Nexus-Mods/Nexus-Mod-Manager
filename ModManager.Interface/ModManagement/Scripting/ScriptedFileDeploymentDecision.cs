namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Captures the method-neutral deployment decision for one scripted file operation.
	/// </summary>
	public sealed class ScriptedFileDeploymentDecision
	{
		/// <summary>
		/// Initializes an immutable scripted deployment decision.
		/// </summary>
		private ScriptedFileDeploymentDecision(ModInstallMethod p_mimMethod, bool p_booWritePayload,
			bool p_booUseDeploymentCoordinator, bool p_booActivate, string p_strStagingPath,
			ModLinkInstallDecision p_midLinkDecision)
		{
			Method = p_mimMethod;
			WritePayload = p_booWritePayload;
			UseDeploymentCoordinator = p_booUseDeploymentCoordinator;
			Activate = p_booActivate;
			StagingPath = p_strStagingPath;
			LinkDecision = p_midLinkDecision;
		}

		/// <summary>
		/// Gets the install method captured when the decision was created.
		/// </summary>
		public ModInstallMethod Method { get; private set; }

		/// <summary>
		/// Gets whether the incoming payload should be written by the selected backend.
		/// </summary>
		public bool WritePayload { get; private set; }

		/// <summary>
		/// Gets whether the method-neutral deployment coordinator must own final deployment.
		/// </summary>
		public bool UseDeploymentCoordinator { get; private set; }

		/// <summary>
		/// Gets whether the incoming owner should become the physical winner.
		/// </summary>
		public bool Activate { get; private set; }

		/// <summary>
		/// Gets the Virtual staging path, or <c>null</c> for Direct deployment.
		/// </summary>
		public string StagingPath { get; private set; }

		/// <summary>
		/// Gets the pure-Virtual link decision, when that backend remains authoritative.
		/// </summary>
		public ModLinkInstallDecision LinkDecision { get; private set; }

		/// <summary>
		/// Creates a decision for a pure-Virtual scripted deployment.
		/// </summary>
		public static ScriptedFileDeploymentDecision ForVirtual(string p_strStagingPath, bool p_booStageFile,
			ModLinkInstallDecision p_midLinkDecision)
		{
			bool activate = p_midLinkDecision == null || !p_midLinkDecision.HasLinkOutcome ||
				p_midLinkDecision.LinkOutcome == true;
			return new ScriptedFileDeploymentDecision(ModInstallMethod.Virtual, p_booStageFile, false,
				activate, p_strStagingPath, p_midLinkDecision);
		}

		/// <summary>
		/// Creates a decision for a promoted Virtual scripted deployment.
		/// </summary>
		public static ScriptedFileDeploymentDecision ForPromotedVirtual(string p_strStagingPath,
			bool p_booStageFile, bool p_booActivate)
		{
			return new ScriptedFileDeploymentDecision(ModInstallMethod.Virtual, p_booStageFile, true,
				p_booActivate, p_strStagingPath, null);
		}

		/// <summary>
		/// Creates a decision for a staging-free Direct scripted deployment.
		/// </summary>
		public static ScriptedFileDeploymentDecision ForDirect(bool p_booDeploy)
		{
			return new ScriptedFileDeploymentDecision(ModInstallMethod.Direct, p_booDeploy, true,
				p_booDeploy, null, null);
		}
	}
}
