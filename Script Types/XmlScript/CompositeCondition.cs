﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// The possible relations of conditions.
	/// </summary>
	[Flags]
	public enum ConditionOperator
	{
		/// <summary>
		/// Indicates all contained conditions must be satisfied in order for this condition to be satisfied.
		/// </summary>
		And=1,

		/// <summary>
		/// Indicates at least one listed condition must be satisfied in order for this condition to be satisfied.
		/// </summary>
		Or=2
	}

	/// <summary>
	/// A condition that requires a combination of sub-conditions to be fulfilled.
	/// </summary>
	/// <remarks>
	/// The combination of sub-conditions that must be fulfilled is determined by an
	/// operator (e.g., and, or).
	/// </remarks>
	public class CompositeCondition : ObservableObject, ICondition
	{
		private ThreadSafeObservableList<ICondition> m_lstConditions = new ThreadSafeObservableList<ICondition>();
		private ConditionOperator m_dopOperator = ConditionOperator.And;

		#region Properties

		/// <summary>
		/// Gets the <see cref="ConditionOperator"/> specifying which of the sub-conditions
		/// must be fulfilled in order for this condition to be fulfilled.
		/// </summary>
		/// <value>The <see cref="ConditionOperator"/> specifying which of the sub-conditions
		/// must be fulfilled in order for this condition to be fulfilled.</value>
		public ConditionOperator Operator
		{
			get
			{
				return m_dopOperator;
			}
		}

		/// <summary>
		/// Gets the sub-conditions.
		/// </summary>
		/// <value>The sub-conditions.</value>
		public IList<ICondition> Conditions
		{
			get
			{
				return m_lstConditions;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_dopOperator">The operator that specifies what combination of sub-conditions
		/// must be fulfilled in order for this dependancy to be fulfilled.</param>
		public CompositeCondition(ConditionOperator p_dopOperator)
		{
			m_dopOperator = p_dopOperator;
			m_lstConditions.CollectionChanged += new NotifyCollectionChangedEventHandler(m_lstConditions_CollectionChanged);
		}

		void m_lstConditions_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			OnPropertyChanged(() => Conditions);
		}

		#endregion

		#region ICondition Members

		/// <summary>
		/// Determines if the given composite condition if fulfilled.
		/// </summary>
		/// <remarks>
		/// A composite condition is fulfilled if and only if its contained conditions
		/// are fulfilled in the combination specified by the <see cref="Operator"/>.
		/// </remarks>
		/// <param name="p_csmStateManager">The manager that tracks the currect install state.</param>
		/// <returns><c>true</c> if the condition is fulfilled;
		/// <c>false</c> otherwise.</returns>
		/// <seealso cref="ICondition.GetIsFulfilled(ConditionStateManager)"/>
		public bool GetIsFulfilled(ConditionStateManager p_csmStateManager)
		{
			bool booAllFulfilled = (m_dopOperator == ConditionOperator.And) ? true : false;
			bool booThisFulfilled = true;
			foreach (ICondition conCondition in m_lstConditions)
			{
				booThisFulfilled = conCondition.GetIsFulfilled(p_csmStateManager);
				switch (m_dopOperator)
				{
					case ConditionOperator.And:
						booAllFulfilled &= booThisFulfilled;
						break;
					case ConditionOperator.Or:
						booAllFulfilled |= booThisFulfilled;
						break;
				}
			}
			return booAllFulfilled;
		}

		/// <summary>
		/// Gets a message describing whether or not the condition is fulfilled.
		/// </summary>
		/// <remarks>
		/// If the condition is fulfilled the message is "Passed." If the condition is not fulfilled the
		/// message is a list of the sub-dependecies' messages.
		/// </remarks>
		/// <param name="p_csmStateManager">The manager that tracks the currect install state.</param>
		/// <returns>A message describing whether or not the condition is fulfilled.</returns>
		/// <seealso cref="ICondition.GetMessage(ConditionStateManager)"/>
		public string GetMessage(ConditionStateManager p_csmStateManager)
		{
			StringBuilder stbMessage = new StringBuilder();
			if (m_dopOperator == ConditionOperator.Or)
				stbMessage.Append("(");

			bool booAllFulfilled = (m_dopOperator == ConditionOperator.And) ? true : false;
			bool booThisFulfilled = true;
			ICondition conCondition = null;
			for (Int32 i = 0; i < m_lstConditions.Count; i++)
			{
				conCondition = m_lstConditions[i];
				booThisFulfilled = conCondition.GetIsFulfilled(p_csmStateManager);
				if (!booThisFulfilled)
					stbMessage.Append(conCondition.GetMessage(p_csmStateManager));
				switch (m_dopOperator)
				{
					case ConditionOperator.And:
						if (i < m_lstConditions.Count - 1)
							stbMessage.AppendLine();
						booAllFulfilled &= booThisFulfilled;
						break;
					case ConditionOperator.Or:
						if (i < m_lstConditions.Count - 1)
							stbMessage.AppendLine(" OR");
						booAllFulfilled |= booThisFulfilled;
						break;
				}
			}
			if (m_dopOperator == ConditionOperator.Or)
				stbMessage.Append(")");
			return booAllFulfilled ? "Passed" : stbMessage.ToString();
		}

		#endregion
	}
}
