using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Util;
using System.ComponentModel;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display an <see cref="XmlScript"/>.
	/// </summary>
	public class XmlScriptTreeViewVM : ObservableObject
	{
		private XmlScript m_xscScript = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="XmlScriptType"/> used by the view model.
		/// </summary>
		/// <value>The <see cref="XmlScriptType"/> used by the view model.</value>
		public XmlScriptType ScriptType { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="XmlScript"/> to be displayed.
		/// </summary>
		/// <value>The <see cref="XmlScript"/> to be displayed.</value>
		public XmlScript Script
		{
			get
			{
				return m_xscScript;
			}
			set
			{
				if (m_xscScript != null)
					m_xscScript.PropertyChanged -= Script_PropertyChanged;
				if (CheckIfChanged(m_xscScript, value))
				{
					m_xscScript = value ?? new XmlScript(ScriptType, XmlScriptType.ScriptVersions[XmlScriptType.ScriptVersions.Length - 1]);
					if (m_xscScript != null)
						ChangeEditedScriptVersion();
					OnPropertyChanged(() => Script);
				}
				if (m_xscScript != null)
					m_xscScript.PropertyChanged += new PropertyChangedEventHandler(Script_PropertyChanged);
			}
		}

		/// <summary>
		/// Gets the <see cref="IXmlScriptNodeAdapter"/> used to retrieve data and supported functions
		/// for the various types of Xml Script nodes.
		/// </summary>
		/// <value>The <see cref="IXmlScriptNodeAdapter"/> used to retrieve data and supported functions
		/// for the various types of Xml Script nodes.</value>
		public IXmlScriptNodeAdapter NodeAdapter { get; protected set; }

		#endregion

		#region Constructor

		/// <summary>
		/// A simple constructor that initializes the object with the required dependencies
		/// </summary>
		/// <param name="p_xscScript">The <see cref="XmlScript"/> being viewed.</param>
		public XmlScriptTreeViewVM(XmlScript p_xscScript)
			: this((XmlScriptType)p_xscScript.Type)
		{
			Script = p_xscScript;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the required dependencies
		/// </summary>
		/// <param name="p_xstScriptType">The type of the script being edited.</param>
		public XmlScriptTreeViewVM(XmlScriptType p_xstScriptType)
		{
			ScriptType = p_xstScriptType;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the XML Script
		/// being edited.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Script_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<XmlScript>(x => x.Version)))
				ChangeEditedScriptVersion();
		}

		/// <summary>
		/// This resets the script viewer to use the correct adapter for the new script version.
		/// </summary>
		private void ChangeEditedScriptVersion()
		{
			NodeAdapter = ScriptType.GetXmlScriptNodeAdapter(Script.Version);
		}
	}
}
