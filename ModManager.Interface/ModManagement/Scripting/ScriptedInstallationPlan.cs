using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Stores the ordered sequence of logical operations produced by a scripted installer.
	/// </summary>
	/// <remarks>
	/// Operation order is significant and is preserved exactly as submitted. The plan does not
	/// perform, normalize, reorder, or otherwise interpret the operations it contains.
	/// </remarks>
	public class ScriptedInstallationPlan
	{
		private readonly List<ScriptedInstallOperation> m_lstOperations;
		private readonly ReadOnlyCollection<ScriptedInstallOperation> m_rocOperations;

		#region Properties

		/// <summary>
		/// Gets the operations in the order in which they were added to the plan.
		/// </summary>
		/// <value>The ordered, read-only operation collection.</value>
		public IReadOnlyList<ScriptedInstallOperation> Operations
		{
			get { return m_rocOperations; }
		}

		/// <summary>
		/// Gets the number of operations currently contained in the plan.
		/// </summary>
		/// <value>The number of operations in the plan.</value>
		public int Count
		{
			get { return m_lstOperations.Count; }
		}

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes an empty scripted installation plan.
		/// </summary>
		public ScriptedInstallationPlan()
		{
			m_lstOperations = new List<ScriptedInstallOperation>();
			m_rocOperations = m_lstOperations.AsReadOnly();
		}

		#endregion

		#region Operation Management

		/// <summary>
		/// Adds an operation to the end of the installation plan.
		/// </summary>
		/// <param name="p_sioOperation">The logical installation operation to append.</param>
		public void Add(ScriptedInstallOperation p_sioOperation)
		{
			if (p_sioOperation == null)
				throw new ArgumentNullException(nameof(p_sioOperation));

			m_lstOperations.Add(p_sioOperation);
		}

		#endregion
	}
}
