using System;
using System.Collections;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public class InstallStepsVM
	{
		protected XmlScript Script { get; private set; }
		public SortOrder InstallStepSortOrder { get; set; }
		public ThreadSafeObservableList<InstallStep> InstallSteps { get; private set; }

		public InstallStepsVM(XmlScript p_xscScript)
		{
			Script = p_xscScript;
			Reset();
		}

		public XmlScript Commit(string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.InstallStepSortOrder)))
				Script.InstallStepSortOrder = InstallStepSortOrder;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.InstallSteps)))
				if (InstallStepSortOrder == SortOrder.Explicit)
				{
					Script.InstallSteps.Clear();
					for (Int32 i = 0; i < InstallSteps.Count; i++)
					{
						InstallStep stpStep = InstallSteps[i];
						Script.InstallSteps.Add(stpStep);
					}
				}
			return Script;
		}

		public void Reset()
		{
			InstallStepSortOrder = Script.InstallStepSortOrder;
			InstallSteps = new ThreadSafeObservableList<InstallStep>(Script.InstallSteps);
		}

		public void Reset(string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.InstallStepSortOrder)))
				InstallStepSortOrder = Script.InstallStepSortOrder;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.InstallSteps)))
				InstallSteps = new ThreadSafeObservableList<InstallStep>(Script.InstallSteps);
		}
	}

	public class InstallStepsEditorVM : IViewModel
	{
		public IEnumerable SortOrders { get; private set; }

		public XmlScript Script
		{
			set
			{
				InstallStepsVM = new InstallStepsVM(value);
			}
		}
		public InstallStepsVM InstallStepsVM { get; private set; }
		public ErrorContainer Errors { get; private set; }

		public InstallStepsEditorVM(XmlScript p_xscScript)
		{
			Script = p_xscScript;
			
			SortOrders = Enum.GetValues(typeof(SortOrder));
			Errors = new ErrorContainer();
		}

		public void SaveInstallSteps(string p_strPropertyName)
		{
			InstallStepsVM.Commit(p_strPropertyName);
		}

		#region IViewModel Members

		public bool Validate()
		{
			return true;
		}

		#endregion
	}
}
