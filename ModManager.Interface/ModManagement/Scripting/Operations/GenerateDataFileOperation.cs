namespace Nexus.Client.ModManagement.Scripting.Operations
{
	/// <summary>
	/// Describes the creation or replacement of a game data file from generated content.
	/// </summary>
	public sealed class GenerateDataFileOperation : ScriptedInstallOperation
	{
		#region Properties

		/// <summary>
		/// Gets the logical destination path of the generated file.
		/// </summary>
		/// <value>The game-relative destination path.</value>
		public string DestinationPath { get; private set; }

		/// <summary>
		/// Gets the data to write to the generated file.
		/// </summary>
		/// <value>The generated file content.</value>
		/// <remarks>The operation owns a snapshot of the supplied buffer so later script-side mutations cannot alter an already planned operation.</remarks>
		public byte[] Data { get; private set; }

		/// <summary>
		/// Gets whether the staging-file overwrite decision was resolved during planning.
		/// </summary>
		public bool HasResolvedStagingOverwrite { get; private set; }

		/// <summary>
		/// Gets whether the generated content should be written to its staging location.
		/// </summary>
		public bool StageFile { get; private set; }

		/// <summary>
		/// Gets the staging path used when the overwrite decision was resolved.
		/// </summary>
		public string StagingPath { get; private set; }

		/// <summary>
		/// Gets the virtual-link decision resolved during planning, when available.
		/// </summary>
		public ModLinkInstallDecision LinkDecision { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new generated-file operation.
		/// </summary>
		/// <param name="p_strDestinationPath">The logical destination path of the generated file.</param>
		/// <param name="p_bteData">The data to write to the generated file.</param>
		public GenerateDataFileOperation(string p_strDestinationPath, byte[] p_bteData)
		{
			DestinationPath = p_strDestinationPath;
			Data = p_bteData == null ? null : (byte[])p_bteData.Clone();
		}

		/// <summary>
		/// Initializes a generated-file operation with decisions resolved during deferred planning.
		/// </summary>
		/// <param name="p_strDestinationPath">The logical destination path of the generated file.</param>
		/// <param name="p_bteData">The generated file contents.</param>
		/// <param name="p_strStagingPath">The physical staging path evaluated during planning.</param>
		/// <param name="p_booStageFile">Whether the generated content should be written to the staging path.</param>
		/// <param name="p_midLinkDecision">The virtual-link decision resolved during planning.</param>
		public GenerateDataFileOperation(string p_strDestinationPath, byte[] p_bteData, string p_strStagingPath, bool p_booStageFile, ModLinkInstallDecision p_midLinkDecision)
			: this(p_strDestinationPath, p_bteData)
		{
			HasResolvedStagingOverwrite = true;
			StageFile = p_booStageFile;
			StagingPath = p_strStagingPath;
			LinkDecision = p_midLinkDecision;
		}

		#endregion
	}
}
