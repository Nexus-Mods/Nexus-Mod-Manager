using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	/// <summary>
	/// Displays an <see cref="XmlScript"/> as a tree.
	/// </summary>
	public class XmlScriptTreeView : TreeView
	{
		private XmlScriptTreeViewVM m_vmlViewModel = null;
		private XmlScript m_xscScript = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public XmlScriptTreeViewVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				if (m_vmlViewModel != value)
				{
					if (m_vmlViewModel != null)
						m_vmlViewModel.PropertyChanged -= ViewModel_PropertyChanged;
					m_vmlViewModel = value;
					if (m_vmlViewModel != null)
						m_vmlViewModel.PropertyChanged += new PropertyChangedEventHandler(ViewModel_PropertyChanged);
					Script = value.Script;
				}
			}
		}

		/// <summary>
		/// Gets or sets the <see cref="XmlScript"/> to be displayed.
		/// </summary>
		/// <value>The <see cref="XmlScript"/> to be displayed.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected XmlScript Script
		{
			get
			{
				return m_xscScript;
			}
			set
			{
				m_xscScript = value;
				Nodes.Clear();

				if (value == null)
					return;

				XmlScriptTreeNode tndRoot = new XmlScriptTreeNode(String.Format("XML Script - v {0}", value.Version));
				Nodes.Add(tndRoot);
				AddScriptNodes();
			}
		}

		/// <summary>
		/// Gets the <see cref="IXmlScriptNodeAdapter"/> used to retrieve data and supported functions
		/// for the various types of Xml Script nodes.
		/// </summary>
		/// <value>The <see cref="IXmlScriptNodeAdapter"/> used to retrieve data and supported functions
		/// for the various types of Xml Script nodes.</value>
		protected IXmlScriptNodeAdapter NodeAdapter
		{
			get
			{
				return ViewModel.NodeAdapter;
			}
		}

		#endregion

		#region Node Addition

		private void AddScriptNodes()
		{
			XmlScriptTreeNode tndRoot = (XmlScriptTreeNode)Nodes[0];

			tndRoot.AddNode(Script.HeaderInfo, NodeAdapter.GetHeaderEditor, FormatHeader);
			tndRoot.AddNode(Script, NodeAdapter.GetPrerequisitesEditor, FormatPrerequisites);
			tndRoot.AddNode(Script.RequiredInstallFiles, NodeAdapter.GetRequiredInstallFilesEditor, FormatRequiredInstallFiles);
			XmlScriptTreeNode<XmlScript> tndInstallSteps = tndRoot.AddNode(Script, NodeAdapter.GetInstallStepOrderEditor, FormatInstallSteps, HandleInstallStepsPropertyChange);
			AddInstallSteps(tndInstallSteps);
			if (Script.Version > new Version(1, 0, 0, 0))
			{
				XmlScriptTreeNode<ThreadSafeObservableList<ConditionallyInstalledFileSet>> tndConditionallyInstalledFileSets = tndRoot.AddNode(Script.ConditionallyInstalledFileSets, NodeAdapter.GetConditionallyInstalledFileSetOrderEditor, FormatConditionallyInstalledFileSets, HandleConditionallyInstalledFileSetsChange);
				AddConditionFileInstalls(tndConditionallyInstalledFileSets);
			}
		}

		private void AddInstallSteps(XmlScriptTreeNode<XmlScript> p_tndScript)
		{
			p_tndScript.Nodes.Clear();
			XmlScriptTreeNode<InstallStep> tndStep = null;
			Int32 intMaxStepIndex = (Script.Version > new Version(3, 0, 0, 0)) ? p_tndScript.Object.InstallSteps.Count : 1;
			InstallStep ispStep = null;
			for (Int32 i = 0; i < intMaxStepIndex; i++)
			{
				ispStep = p_tndScript.Object.InstallSteps[i];
				tndStep = p_tndScript.AddNode(ispStep, NodeAdapter.GetInstallStepEditor, FormatInstallStep, HandleInstallStepPropertyChange);
				AddOptionGroups(tndStep);
			}
		}

		private void AddOptionGroups(XmlScriptTreeNode<InstallStep> p_tndStep)
		{
			p_tndStep.Nodes.Clear();
			foreach (OptionGroup opgGroup in p_tndStep.Object.OptionGroups)
			{
				XmlScriptTreeNode<OptionGroup> tndGroup = p_tndStep.AddNode(opgGroup, NodeAdapter.GetOptionGroupEditor, FormatOptionGroup, HandleOptionGroupPropertyChange);
				AddOptions(tndGroup);
			}
		}

		private void AddOptions(XmlScriptTreeNode<OptionGroup> p_tndGroup)
		{
			p_tndGroup.Nodes.Clear();
			foreach (Option optOption in p_tndGroup.Object.Options)
				p_tndGroup.AddNode(optOption, NodeAdapter.GetOptionEditor, FormatOption);
		}

		private void AddConditionFileInstalls(XmlScriptTreeNode<ThreadSafeObservableList<ConditionallyInstalledFileSet>> p_tndConditionallyInstalledFileSets)
		{
			p_tndConditionallyInstalledFileSets.Nodes.Clear();
			foreach (ConditionallyInstalledFileSet cipPattern in Script.ConditionallyInstalledFileSets)
				p_tndConditionallyInstalledFileSets.AddNode(cipPattern, NodeAdapter.GetConditionallyInstalledFileSetEditor, FormatConditionallyInstalledFileSet);
		}

		#endregion

		#region Node Formatting

		protected void FormatHeader(XmlScriptTreeNode p_stnNode, HeaderInfo p_hdrHeader)
		{
			p_stnNode.Text = String.Format("Mod Name: {0}", p_hdrHeader.Title);
		}

		protected void FormatPrerequisites(XmlScriptTreeNode p_stnNode, XmlScript p_xscScript)
		{
			Int32 intPrerequisiteCount = 0;
			if (p_xscScript.ModPrerequisites != null)
			{
				intPrerequisiteCount = 1;
				if (p_xscScript.ModPrerequisites is CompositeCondition)
					intPrerequisiteCount = ((CompositeCondition)p_xscScript.ModPrerequisites).Conditions.Count;
			}
			p_stnNode.Text = String.Format("Prerequisites ({0})", intPrerequisiteCount);
		}

		protected void FormatRequiredInstallFiles(XmlScriptTreeNode p_stnNode, IList<InstallableFile> p_lstFiles)
		{
			p_stnNode.Text = String.Format("Mandatory Install Files ({0})", p_lstFiles.Count);
		}

		protected void FormatInstallSteps(XmlScriptTreeNode p_stnNode, XmlScript p_xscScript)
		{
			Int32 intStepCount = (Script.Version > new Version(3, 0, 0, 0)) ? p_xscScript.InstallSteps.Count : 1;
			p_stnNode.Text = String.Format("Install Steps ({0})", intStepCount);
		}

		protected void FormatInstallStep(XmlScriptTreeNode p_stnNode, InstallStep p_ispStep)
		{
			p_stnNode.Text = String.Format("Install Step: {0}", p_ispStep.Name);
		}

		protected void FormatOptionGroup(XmlScriptTreeNode p_stnNode, OptionGroup p_opgGroup)
		{
			p_stnNode.Text = String.Format("Option Group: {0}", p_opgGroup.Name);
		}

		protected void FormatOption(XmlScriptTreeNode p_stnNode, Option p_optOption)
		{
			p_stnNode.Text = String.Format("Option: {0}", p_optOption.Name);
		}

		protected void FormatConditionallyInstalledFileSets(XmlScriptTreeNode p_stnNode, IList<ConditionallyInstalledFileSet> p_lstPatterns)
		{
			p_stnNode.Text = String.Format("Conditionnally Installed File Sets ({0})", p_lstPatterns.Count);
		}

		protected void FormatConditionallyInstalledFileSet(XmlScriptTreeNode p_stnNode, ConditionallyInstalledFileSet p_cipPatterns)
		{
			p_stnNode.Text = String.Format("Conditionnally Installed File Set ({0})", p_cipPatterns.Files.Count);
		}

		#endregion

		#region Property Changes

		protected void HandleInstallStepsPropertyChange(XmlScriptTreeNode p_stnNode, XmlScript p_xscScript, string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<XmlScript>(x => x.InstallSteps)))
			{
				SelectedNode = p_stnNode;
				AddInstallSteps((XmlScriptTreeNode<XmlScript>)p_stnNode);
			}
		}

		protected void HandleInstallStepPropertyChange(XmlScriptTreeNode p_stnNode, InstallStep p_ispStep, string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<InstallStep>(x => x.OptionGroups)))
			{
				SelectedNode = p_stnNode;
				AddOptionGroups((XmlScriptTreeNode<InstallStep>)p_stnNode);
			}
		}

		protected void HandleOptionGroupPropertyChange(XmlScriptTreeNode p_stnNode, OptionGroup p_opgGroup, string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName<OptionGroup>(x => x.Options)))
			{
				SelectedNode = p_stnNode;
				AddOptions((XmlScriptTreeNode<OptionGroup>)p_stnNode);
			}
		}

		protected void HandleConditionallyInstalledFileSetsChange(XmlScriptTreeNode p_stnNode, IList<ConditionallyInstalledFileSet> p_xscScript, string p_strPropertyName)
		{
			SelectedNode = p_stnNode;
			AddConditionFileInstalls((XmlScriptTreeNode<ThreadSafeObservableList<ConditionallyInstalledFileSet>>)p_stnNode);
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the view model.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<XmlScriptTreeViewVM>(x => x.Script)))
				Script = ViewModel.Script;
		}

		#endregion
	}
}
