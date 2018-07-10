using System;
using System.Collections;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public class InstallStepVM
	{
		protected InstallStep InstallStep { get; private set; }
		public string Name { get; set; }
		public SortOrder GroupSortOrder { get; set; }
		public ThreadSafeObservableList<OptionGroup> OptionGroups { get; private set; }

		public InstallStepVM(InstallStep p_stpStep)
		{
			InstallStep = p_stpStep;
			Reset();
		}

		public InstallStep Commit(string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Name)))
				InstallStep.Name = Name;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.GroupSortOrder)))
				InstallStep.GroupSortOrder = GroupSortOrder;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.OptionGroups)))
				if (GroupSortOrder == SortOrder.Explicit)
				{
					InstallStep.OptionGroups.Clear();
					for (Int32 i = 0; i < OptionGroups.Count; i++)
					{
						OptionGroup grpGroup = OptionGroups[i];
						InstallStep.OptionGroups.Add(grpGroup);
					}
				}
			return InstallStep;
		}

		public void Reset()
		{
			Name = InstallStep.Name;
			GroupSortOrder = InstallStep.GroupSortOrder;
			OptionGroups = new ThreadSafeObservableList<OptionGroup>(InstallStep.OptionGroups);
		}

		public void Reset(string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Name)))
				Name = InstallStep.Name;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.GroupSortOrder)))
				GroupSortOrder = InstallStep.GroupSortOrder;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.OptionGroups)))
				OptionGroups = new ThreadSafeObservableList<OptionGroup>(InstallStep.OptionGroups);
		}
	}

	[Flags]
	public enum InstallStepProperties
	{
		Name = 1,
		GroupSortOrder = 2,
		Visibility = 4
	}

	public class InstallStepEditorVM : IViewModel
	{
		public event EventHandler InstallStepValidated = delegate { };

		private InstallStep m_ispInstallStep = null;

		public ConditionEditorVM ConditionEditorVM { get; private set; }

		public IEnumerable SortOrders { get; private set; }

		public bool NameVisible { get; private set; }
		public bool GroupSortOrderVisible { get; private set; }
		public bool VisibilityVisible { get; private set; }

		public InstallStep InstallStep
		{
			get
			{
				return m_ispInstallStep;
			}
			set
			{
				m_ispInstallStep = value;
				InstallStepVM = new InstallStepVM(value);
				if (VisibilityVisible)
					ConditionEditorVM.Condition = value.VisibilityCondition;
			}
		}

		public InstallStepVM InstallStepVM { get; private set; }
		public ErrorContainer Errors { get; private set; }

		public InstallStepEditorVM(ConditionEditorVM p_vmlConditionEditor, InstallStep p_stpStep, InstallStepProperties p_ispEditableProperties)
		{
			NameVisible = (p_ispEditableProperties & InstallStepProperties.Name) > 0;
			GroupSortOrderVisible = (p_ispEditableProperties & InstallStepProperties.GroupSortOrder) > 0;
			VisibilityVisible = (p_ispEditableProperties & InstallStepProperties.Visibility) > 0;
			ConditionEditorVM = p_vmlConditionEditor;
			InstallStep = p_stpStep;

			SortOrders = Enum.GetValues(typeof(SortOrder));
			Errors = new ErrorContainer();

			ConditionEditorVM.ConditionSaved += new EventHandler(ConditionSaved);
		}

		private void ConditionSaved(object sender, EventArgs e)
		{
			InstallStep.VisibilityCondition = ConditionEditorVM.Condition;
		}

		protected void OnInstallStepValidated()
		{
			InstallStepValidated(this, new EventArgs());
		}

		public void SaveInstallStep(string p_strPropertyName)
		{
			bool booValid = true;
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<OptionGroup>(x => x.Name)))
				booValid = ValidateInstallStepName();
			if (booValid)
				InstallStepVM.Commit(p_strPropertyName);
		}

		/// <summary>
		/// Ensures that the install step name is valid.
		/// </summary>
		/// <remarks>
		/// An install step name is valid if it is not empty.
		/// </remarks>
		/// <returns><c>true</c> if the install step name is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateInstallStepName()
		{
			bool booIsValid = true;
			Errors.Clear<InstallStep>(x => x.Name);
			if (String.IsNullOrEmpty(InstallStepVM.Name) && NameVisible)
			{
				Errors.SetError<InstallStep>(x => x.Name, "Name is required.");
				booIsValid = false;
			}
			OnInstallStepValidated();
			return booIsValid;
		}

		/// <summary>
		/// Ensures that the install step name is valid.
		/// </summary>
		/// <remarks>
		/// An install step name is valid if it is not empty.
		/// </remarks>
		/// <returns><c>true</c> if the install step name is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateVisibilityCondition()
		{
			bool booIsValid = ConditionEditorVM.Validate();
			OnInstallStepValidated();
			return booIsValid;
		}

		#region IViewModel Members

		public bool Validate()
		{
			return ValidateInstallStepName() && ValidateVisibilityCondition();
		}

		#endregion
	}
}
