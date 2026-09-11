using System;
using Nexus.Client.ModManagement.Scripting.Operations;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Specifies when operations submitted to a scripted installation session are executed.
	/// </summary>
	public enum ScriptedInstallationSessionMode
	{
		/// <summary>
		/// Executes each operation synchronously when it is submitted.
		/// </summary>
		Immediate,

		/// <summary>
		/// Records submitted operations for explicit execution after planning completes.
		/// </summary>
		Deferred,

		/// <summary>
		/// Executes each operation synchronously while explicitly preserving legacy script read-after-write behavior.
		/// </summary>
		ImmediateCompatibility
	}

	/// <summary>
	/// Coordinates scripted installation operations and retains the ordered operation history for the current script execution.
	/// </summary>
	public class ScriptedInstallationSession
	{
		private readonly IScriptedInstallOperationExecutor m_sioExecutor;
		private int m_intExecutedOperationCount;

		#region Properties

		/// <summary>
		/// Gets the ordered installation plan produced by the current scripted installation session.
		/// </summary>
		public ScriptedInstallationPlan Plan { get; private set; }

		/// <summary>
		/// Gets the execution mode used by the current session.
		/// </summary>
		public ScriptedInstallationSessionMode Mode { get; private set; }

		/// <summary>
		/// Gets the optional projected state maintained for operations accepted by this session.
		/// </summary>
		public ScriptedInstallationProjectedState ProjectedState { get; private set; }

		/// <summary>
		/// Gets whether the plan contains operations that have not yet been executed.
		/// </summary>
		public bool HasPendingOperations
		{
			get { return m_intExecutedOperationCount < Plan.Count; }
		}

		/// <summary>
		/// Gets the next operation awaiting execution, or <c>null</c> when the plan is fully executed.
		/// </summary>
		public ScriptedInstallOperation NextPendingOperation
		{
			get { return HasPendingOperations ? Plan.Operations[m_intExecutedOperationCount] : null; }
		}

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a scripted installation session that executes submitted operations immediately.
		/// </summary>
		/// <param name="p_sioExecutor">The executor responsible for applying submitted operations.</param>
		public ScriptedInstallationSession(IScriptedInstallOperationExecutor p_sioExecutor)
			: this(p_sioExecutor, ScriptedInstallationSessionMode.Immediate, null)
		{
		}

		/// <summary>
		/// Initializes a scripted installation session using the requested execution mode.
		/// </summary>
		/// <param name="p_sioExecutor">The executor responsible for applying submitted operations.</param>
		/// <param name="p_simMode">The execution mode used for submitted operations.</param>
		public ScriptedInstallationSession(IScriptedInstallOperationExecutor p_sioExecutor, ScriptedInstallationSessionMode p_simMode)
			: this(p_sioExecutor, p_simMode, null)
		{
		}

		/// <summary>
		/// Initializes a scripted installation session using the requested execution mode and projected-state view.
		/// </summary>
		/// <param name="p_sioExecutor">The executor responsible for applying submitted operations.</param>
		/// <param name="p_simMode">The execution mode used for submitted operations.</param>
		/// <param name="p_spsProjectedState">The optional state overlay updated as operations are accepted.</param>
		public ScriptedInstallationSession(IScriptedInstallOperationExecutor p_sioExecutor, ScriptedInstallationSessionMode p_simMode, ScriptedInstallationProjectedState p_spsProjectedState)
		{
			if (p_sioExecutor == null)
				throw new ArgumentNullException(nameof(p_sioExecutor));
			if (!Enum.IsDefined(typeof(ScriptedInstallationSessionMode), p_simMode))
				throw new ArgumentOutOfRangeException(nameof(p_simMode));

			m_sioExecutor = p_sioExecutor;
			Mode = p_simMode;
			ProjectedState = p_spsProjectedState;
			Plan = new ScriptedInstallationPlan();
		}

		#endregion

		#region Operation Submission

		/// <summary>
		/// Records the specified operation and executes it immediately when the session is configured for immediate execution.
		/// </summary>
		/// <param name="p_sioOperation">The logical installation operation to submit.</param>
		/// <returns><c>true</c> when the operation is accepted and, when applicable, completed successfully; otherwise, <c>false</c>.</returns>
		public bool Submit(ScriptedInstallOperation p_sioOperation)
		{
			if (p_sioOperation == null)
				throw new ArgumentNullException(nameof(p_sioOperation));

			Plan.Add(p_sioOperation);
			if (Mode == ScriptedInstallationSessionMode.Deferred)
			{
				if (ProjectedState != null)
					ProjectedState.Apply(p_sioOperation);
				return true;
			}

			bool booResult = ExecuteNext();
			if (booResult && (ProjectedState != null))
				ProjectedState.Apply(p_sioOperation);
			return booResult;
		}

		/// <summary>
		/// Executes the next pending operation in the installation plan.
		/// </summary>
		/// <returns><c>true</c> when no operation is pending or the next operation completes successfully; otherwise, <c>false</c>.</returns>
		public bool ExecuteNext()
		{
			if (!HasPendingOperations)
				return true;

			ScriptedInstallOperation sioOperation = Plan.Operations[m_intExecutedOperationCount];
			bool booResult = m_sioExecutor.Execute(sioOperation);
			m_intExecutedOperationCount++;
			return booResult;
		}

		/// <summary>
		/// Begins an optional executor-specific batch for all operations that are currently pending.
		/// </summary>
		/// <returns>A batch scope when the executor supports grouped deployment updates; otherwise, <c>null</c>.</returns>
		public IDisposable BeginPendingOperationBatch()
		{
			IScriptedInstallOperationBatchExecutor sibBatchExecutor = m_sioExecutor as IScriptedInstallOperationBatchExecutor;
			if ((sibBatchExecutor == null) || !HasPendingOperations)
				return null;

			return sibBatchExecutor.BeginExecutionBatch(CountPendingFileOperations());
		}

		/// <summary>
		/// Executes all operations that remain pending in the installation plan.
		/// </summary>
		/// <returns><c>true</c> when every pending operation completes successfully; otherwise, <c>false</c>.</returns>
		public bool ExecutePendingOperations()
		{
			using (BeginPendingOperationBatch())
			{
				while (HasPendingOperations)
					if (!ExecuteNext())
						return false;

				return true;
			}
		}

		/// <summary>
		/// Counts pending operations that may add or update a virtual file link.
		/// </summary>
		/// <returns>The number of pending archive-file and generated-file operations.</returns>
		private int CountPendingFileOperations()
		{
			int intFileOperationCount = 0;
			for (int i = m_intExecutedOperationCount; i < Plan.Count; i++)
			{
				ScriptedInstallOperation sioOperation = Plan.Operations[i];
				if ((sioOperation is InstallModFileOperation) || (sioOperation is GenerateDataFileOperation))
					intFileOperationCount++;
			}

			return intFileOperationCount;
		}

		#endregion
	}
}
