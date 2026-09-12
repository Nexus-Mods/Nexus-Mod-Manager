namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes the installation of a file from a mod archive to a logical game-relative destination.
	/// </summary>
	public sealed class InstallModFileOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the path of the source file inside the mod archive.
		/// </summary>
		/// <value>The archive-relative source path.</value>
		public string SourcePath { get; private set; }

		/// <summary>
		/// Gets the logical destination path requested by the scripted installer.
		/// </summary>
		/// <value>The game-relative destination path.</value>
		public string DestinationPath { get; private set; }

		/// <summary>
		/// Gets the method-neutral deployment decision resolved during planning, when available.
		/// </summary>
		public ScriptedFileDeploymentDecision DeploymentDecision { get; private set; }

		/// <summary>
		/// Gets whether the Virtual staging-file overwrite decision was resolved during planning.
		/// </summary>
		public bool HasResolvedStagingOverwrite
		{
			get { return DeploymentDecision != null && DeploymentDecision.Method == ModInstallMethod.Virtual; }
		}

		/// <summary>
		/// Gets whether the archive file should be written to its staging location.
		/// </summary>
		public bool StageFile
		{
			get { return DeploymentDecision != null && DeploymentDecision.WritePayload; }
		}

		/// <summary>
		/// Gets the staging path used when the overwrite decision was resolved.
		/// </summary>
		public string StagingPath
		{
			get { return DeploymentDecision == null ? null : DeploymentDecision.StagingPath; }
		}

		/// <summary>
		/// Gets the virtual-link decision resolved during planning, when available.
		/// </summary>
		public ModLinkInstallDecision LinkDecision
		{
			get { return DeploymentDecision == null ? null : DeploymentDecision.LinkDecision; }
		}

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new file-installation operation.
		/// </summary>
		/// <param name="p_strSourcePath">The path of the source file inside the mod archive.</param>
		/// <param name="p_strDestinationPath">The logical destination path for the installed file.</param>
		public InstallModFileOperation(string p_strSourcePath, string p_strDestinationPath)
		{
			SourcePath = p_strSourcePath;
			DestinationPath = p_strDestinationPath;
		}

		/// <summary>
		/// Initializes a file-installation operation with decisions resolved during deferred planning.
		/// </summary>
		/// <param name="p_strSourcePath">The path of the source file inside the mod archive.</param>
		/// <param name="p_strDestinationPath">The logical destination path for the installed file.</param>
		/// <param name="p_strStagingPath">The physical staging path evaluated during planning.</param>
		/// <param name="p_booStageFile">Whether the archive file should be written to the staging path.</param>
		/// <param name="p_midLinkDecision">The virtual-link decision resolved during planning.</param>
		public InstallModFileOperation(string p_strSourcePath, string p_strDestinationPath, string p_strStagingPath, bool p_booStageFile, ModLinkInstallDecision p_midLinkDecision)
			: this(p_strSourcePath, p_strDestinationPath, ScriptedFileDeploymentDecision.ForVirtual(p_strStagingPath, p_booStageFile, p_midLinkDecision))
		{
		}

		/// <summary>
		/// Initializes a file-installation operation with a method-neutral deployment decision.
		/// </summary>
		public InstallModFileOperation(string p_strSourcePath, string p_strDestinationPath, ScriptedFileDeploymentDecision p_sddDeploymentDecision)
			: this(p_strSourcePath, p_strDestinationPath)
		{
			DeploymentDecision = p_sddDeploymentDecision;
		}

		#endregion
	}
}
