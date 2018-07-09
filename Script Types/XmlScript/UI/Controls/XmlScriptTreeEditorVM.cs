using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using Nexus.Client.Commands.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	/// <summary>
	/// An enumeration representing the commands used to edit XML Scripts.
	/// </summary>
	[Flags]
	public enum XmlScriptEditCommands
	{
		/// <summary>
		/// Represents no command.
		/// </summary>
		None = 0,

		/// <summary>
		/// Represents the command to add an Install Step.
		/// </summary>
		AddInstallStep = 1,

		/// <summary>
		/// Represents the command to add an Option Group.
		/// </summary>
		AddOptionGroup = 2,

		/// <summary>
		/// Represents the command to add an Option.
		/// </summary>
		AddOption = 4,

		/// <summary>
		/// Represents the command to add a Conditionally Installed File Set.
		/// </summary>
		AddConditionallyInstalledFileSet = 8
	}

	/// <summary>
	/// This class encaapsulates the data and operations presented by UI
	/// elements that allow the editing of an <see cref="XmlScript"/>.
	/// </summary>
	public class XmlScriptTreeEditorVM : XmlScriptTreeViewVM
	{
		private IList<VirtualFileSystemItem> m_lstModFiles = new List<VirtualFileSystemItem>();

		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to delete an <see cref="InstallStep"/> from the XML Script.
		/// </summary>
		/// <value>The command to delete an <see cref="InstallStep"/> from the XML Script.</value>
		public Command<InstallStep> DeleteInstallStepCommand { get; private set; }

		/// Gets the command to delete an <see cref="OptionGroup"/> from the XML Script.
		/// </summary>
		/// <value>The command to delete an <see cref="OptionGroup"/> from the XML Script.</value>
		public Command<OptionGroup> DeleteOptionGroupCommand { get; private set; }

		/// Gets the command to delete an <see cref="Option"/> from the XML Script.
		/// </summary>
		/// <value>The command to delete an <see cref="Option"/> from the XML Script.</value>
		public Command<Option> DeleteOptionCommand { get; private set; }

		/// Gets the command to delete an <see cref="ConditionallyInstalledFileSet"/> from the XML Script.
		/// </summary>
		/// <value>The command to delete an <see cref="ConditionallyInstalledFileSet"/> from the XML Script.</value>
		public Command<ConditionallyInstalledFileSet> DeleteConditionallyInstalledFileSetCommand { get; private set; }

		/// <summary>
		/// Gets the command to add an <see cref="InstallStep"/> to the XML Script.
		/// </summary>
		/// <value>The command to add an <see cref="InstallStep"/> to the XML Script.</value>
		public Command<XmlScript> AddInstallStepCommand { get; private set; }

		/// <summary>
		/// Gets the command to add an <see cref="OptionGroup"/> to the XML Script.
		/// </summary>
		/// <value>The command to add an <see cref="OptionGroup"/> to the XML Script.</value>
		public Command<InstallStep> AddOptionGroupCommand { get; private set; }

		/// <summary>
		/// Gets the command to add an <see cref="Option"/> to the XML Script.
		/// </summary>
		/// <value>The command to add an <see cref="Option"/> to the XML Script.</value>
		public Command<OptionGroup> AddOptionCommand { get; private set; }

		/// <summary>
		/// Gets the command to add a <see cref="ConditionallyInstalledFileSet"/> to the XML Script.
		/// </summary>
		/// <value>The command to add a <see cref="ConditionallyInstalledFileSet"/> to the XML Script.</value>
		public Command<IList<ConditionallyInstalledFileSet>> AddConditionallyInstalledFileSetCommand { get; private set; }

		#endregion

		#region Command Visibility

		/// <summary>
		/// Gets whether the <see cref="AddInstallStepCommand"/> is supported.
		/// </summary>
		/// <value>Whether the <see cref="AddInstallStepCommand"/> is supported.</value>
		public bool IsAddInstallStepCommandSupported { get; private set; }

		/// <summary>
		/// Gets whether the <see cref="AddOptionGroupCommand"/> is supported.
		/// </summary>
		/// <value>Whether the <see cref="AddOptionGroupCommand"/> is supported.</value>
		public bool IsAddOptionGroupCommandSupported { get; private set; }

		/// <summary>
		/// Gets whether the <see cref="AddOptionCommand"/> is supported.
		/// </summary>
		/// <value>Whether the <see cref="AddOptionCommand"/> is supported.</value>
		public bool IsAddOptionCommandSupported { get; private set; }

		/// <summary>
		/// Gets whether the <see cref="AddConditionallyInstalledFileSetCommand"/> is supported.
		/// </summary>
		/// <value>Whether the <see cref="AddConditionallyInstalledFileSetCommand"/> is supported.</value>
		public bool IsAddConditionallyInstalledFileSetCommandSupported { get; private set; }

		#endregion

		/// <summary>
		/// Gets or sets the list of files that are in the <see cref="XmlScript"/>'s mod.
		/// </summary>
		/// <value>The list of files that are in the <see cref="XmlScript"/>'s mod.</value>
		public IList<VirtualFileSystemItem> ModFiles
		{
			get
			{
				return m_lstModFiles;
			}
			set
			{
				SetPropertyIfChanged(ref m_lstModFiles, value, () => ModFiles);
			}
		}

		/// <summary>
		/// Gets the available XML Script versions.
		/// </summary>
		/// <value>The available XML Script versions.</value>
		public IEnumerable ScriptVersions
		{
			get
			{
				return XmlScriptType.ScriptVersions;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_xstScriptType">The type of the script being edited.</param>
		/// <param name="p_lstModFiles">The list of files that are in the <see cref="XmlScript"/>'s mod.</param>
		public XmlScriptTreeEditorVM(XmlScriptType p_xstScriptType, IList<VirtualFileSystemItem> p_lstModFiles)
			: base(p_xstScriptType)
		{
			ModFiles = p_lstModFiles;

			SetSupportedCommands();

			AddInstallStepCommand = new Command<XmlScript>("Add Install Step", "Adds a new Install Step.", AddInstallStep, false);
			AddOptionGroupCommand = new Command<InstallStep>("Add Option Group", "Adds a new Option Group.", AddOptionGroup, false);
			AddOptionCommand = new Command<OptionGroup>("Add Option", "Add a new Option.", AddOption, false);
			AddConditionallyInstalledFileSetCommand = new Command<IList<ConditionallyInstalledFileSet>>("Add Conditionally Installed File Set", "Add a new Conditionally Installed File Set.", AddConditionallyInstalledFileSet, false);

			DeleteInstallStepCommand = new Command<InstallStep>("Delete Install Step", "Deletes the selected Install Step.", DeleteInstallStep, false);
			DeleteOptionGroupCommand = new Command<OptionGroup>("Delete Option Group", "Deletes the selected Option Group.", DeleteOptionGroup, false);
			DeleteOptionCommand = new Command<Option>("Delete Option", "Deletes the selected Option.", DeleteOption, false);
			DeleteConditionallyInstalledFileSetCommand = new Command<ConditionallyInstalledFileSet>("Delete Conditionally Installed File Set", "Deletes the selected Conditionally Installed File Set.", DeleteConditionallyInstalledFileSet, false);
		}

		#endregion

		#region Commands

		#region Addition

		/// <summary>
		/// Adds a new <see cref="InstallStep"/> to the script.
		/// </summary>
		/// <param name="p_xscScript">The script to which to add the <see cref="InstallStep"/>.</param>
		protected void AddInstallStep(XmlScript p_xscScript)
		{
			string strStepName = null;
			for (Int32 i = 1; i < Int32.MaxValue; i++)
			{
				strStepName = String.Format("New Install Step {0}", i);
				if (!p_xscScript.InstallSteps.Contains(x => strStepName.Equals(x.Name)))
					break;
				strStepName = null;
			}
			if (strStepName == null)
				throw new Exception("Unable to new new Install Step: too many steps exist.");
			p_xscScript.InstallSteps.Add(new InstallStep(strStepName, null, SortOrder.Ascending));
		}

		/// <summary>
		/// Adds a new <see cref="OptionGroup"/> to the script.
		/// </summary>
		/// <param name="p_ispParent">The step to which to add the <see cref="OptionGroup"/>.</param>
		protected void AddOptionGroup(InstallStep p_ispParent)
		{
			if (p_ispParent == null)
				return;
			string strGroupName = null;
			for (Int32 i = 1; i < Int32.MaxValue; i++)
			{
				strGroupName = String.Format("New Option Group {0}", i);
				if (!p_ispParent.OptionGroups.Contains(x => strGroupName.Equals(x.Name)))
					break;
				strGroupName = null;
			}
			if (strGroupName == null)
				throw new Exception("Unable to new new Option Group: too many groups exist.");
			p_ispParent.OptionGroups.Add(new OptionGroup(strGroupName, OptionGroupType.SelectAny, SortOrder.Ascending));
		}

		/// <summary>
		/// Adds a new <see cref="Option"/> to the script.
		/// </summary>
		/// <param name="p_ogpGroup">The step to which to add the <see cref="Option"/>.</param>
		protected void AddOption(OptionGroup p_ogpParent)
		{
			if (p_ogpParent == null)
				return;
			string strOptionName = null;
			for (Int32 i = 1; i < Int32.MaxValue; i++)
			{
				strOptionName = String.Format("New Option {0}", i);
				if (!p_ogpParent.Options.Contains(x => strOptionName.Equals(x.Name)))
					break;
				strOptionName = null;
			}
			if (strOptionName == null)
				throw new Exception("Unable to new new Option: too many options exist.");
			p_ogpParent.Options.Add(new Option(strOptionName, null, null, null));
		}

		/// <summary>
		/// Adds a new <see cref="ConditionallyInstalledFileSet"/> to the script.
		/// </summary>
		/// <param name="p_lstSets">The list of the XML script's <see cref="ConditionallyInstalledFileSet"/>s.</param>
		protected void AddConditionallyInstalledFileSet(IList<ConditionallyInstalledFileSet> p_lstSets)
		{
			if (p_lstSets == null)
				return;
			p_lstSets.Add(new ConditionallyInstalledFileSet(null, null));
		}

		#endregion

		#region Deletion

		/// <summary>
		/// Deletes the given <see cref="InstallStep"/> from the script.
		/// </summary>
		/// <param name="p_ispStep">The <see cref="InstallStep"/> to delete.</param>
		/// <exception cref="Exception">Thrown if the given <see cref="InstallStep"/> is the only one
		/// in the XML script.</exception>
		protected void DeleteInstallStep(InstallStep p_ispStep)
		{
			if (p_ispStep == null)
				return;
			if (Script.InstallSteps.Count == 1)
				throw new Exception("Cannot delete last Install Step.");
			Script.InstallSteps.Remove(p_ispStep);
		}

		/// <summary>
		/// Determines if the given <see cref="InstallStep"/> can be deleted from
		/// the XML Script.
		/// </summary>
		/// <param name="p_ispStep">The <see cref="InstallStep"/> to delete.</param>
		/// <returns><c>true</c> if the given <see cref="InstallStep"/> can be deleted;
		/// <c>false</c> otherwise.</returns>
		public bool CanDelete(InstallStep p_ispStep)
		{
			return (Script.InstallSteps.Count > 1);
		}

		/// <summary>
		/// Deletes the given <see cref="OptionGroup"/> from the script.
		/// </summary>
		/// <param name="p_ogpGroup">The <see cref="OptionGroup"/> to delete.</param>
		protected void DeleteOptionGroup(OptionGroup p_ogpGroup)
		{
			if (p_ogpGroup == null)
				return;
			foreach (InstallStep ispStep in Script.InstallSteps)
				if (ispStep.OptionGroups.Remove(p_ogpGroup))
					return;
		}

		/// <summary>
		/// Deletes the given <see cref="Option"/> from the script.
		/// </summary>
		/// <param name="p_optOption">The <see cref="Option"/> to delete.</param>
		protected void DeleteOption(Option p_optOption)
		{
			if (p_optOption == null)
				return;
			foreach (InstallStep ispStep in Script.InstallSteps)
				foreach (OptionGroup opgGroup in ispStep.OptionGroups)
					if (opgGroup.Options.Remove(p_optOption))
						return;
		}

		/// <summary>
		/// Deletes the given <see cref="ConditionallyInstalledFileSet"/> from the script.
		/// </summary>
		/// <param name="p_cisFileSet">The <see cref="ConditionallyInstalledFileSet"/> to delete.</param>
		protected void DeleteConditionallyInstalledFileSet(ConditionallyInstalledFileSet p_cisFileSet)
		{
			if (p_cisFileSet == null)
				return;
			Script.ConditionallyInstalledFileSets.Remove(p_cisFileSet);
		}

		#endregion

		#endregion

		/// <summary>
		/// Raises the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the view model
		/// </summary>
		/// <remarks>
		/// This method hooks into the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of
		/// the <see cref="Script"/> whenever the <see cref="Script"/> changes.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		protected override void OnPropertyChanged(PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName(() => Script)))
				SetSupportedCommands();
			base.OnPropertyChanged(e);
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName(() => Script)))
			{
				if (Script != null)
				{
					Script.PropertyChanged -= Script_PropertyChanged;
					Script.PropertyChanged += new PropertyChangedEventHandler(Script_PropertyChanged);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the XML Script
		/// being edited.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Script_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<XmlScript>(x => x.Version)))
			{
				SetSupportedCommands();
				OnPropertyChanged(() => Script);
			}
		}

		/// <summary>
		/// This resets the editor to respond to changes in the script's version.
		/// </summary>
		private void SetSupportedCommands()
		{
			XmlScriptEditCommands secSupportedCommands = XmlScriptEditCommands.None;
			if (Script != null)
				secSupportedCommands = ScriptType.GetSupportedXmlScriptEditCommands(Script.Version);

			IsAddInstallStepCommandSupported = (secSupportedCommands & XmlScriptEditCommands.AddInstallStep) > 0;
			IsAddOptionGroupCommandSupported = (secSupportedCommands & XmlScriptEditCommands.AddOptionGroup) > 0;
			IsAddOptionCommandSupported = (secSupportedCommands & XmlScriptEditCommands.AddOption) > 0;
			IsAddConditionallyInstalledFileSetCommandSupported = (secSupportedCommands & XmlScriptEditCommands.AddConditionallyInstalledFileSet) > 0;
		}
	}
}
