using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Xml;
using Nexus.UI.Controls;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModAuthoring
{
	/// <summary>
	/// Encapsulates the properties of a mod creation project.
	/// </summary>
	public class Project : ObservableObject, IModInfo, IScriptedMod
	{
		private string m_strModId = null;
		private string m_strDownloadId = null;
		private string m_strModName = null;
		private string m_strFileName = null;
		private string m_strHumanReadableVersion = null;
		private string m_strLastKnownVersion = null;
		private Int32 m_strCategoryId = 0;
		private Int32 m_strCustomCategoryId = 0;
		private bool? m_booIsEndorsed = false;
		private Version m_verMachineVersion = null;
		private string m_strAuthor = null;
		private string m_strDescription = null;
		private string m_strInstallDate = null;
        private int m_intPlaceInModLoadOrder = 0;
		private Uri m_uriWebsite = null;
		private ExtendedImage m_ximScreenshot = null;
		private bool m_booUpdateWarningEnabled = true;
		private IScript m_sctInstallScript = null;
		private Readme m_rmeModReadme = null;
		private IList<VirtualFileSystemItem> m_setFiles = null;
		private bool m_booIsDirty = false;

		#region Properties

		#region IModInfo Members

		/// <summary>
		/// Gets or sets the Id of the mod.
		/// </summary>
		/// <remarks>The id of the mod</remarks>
		public string Id
		{
			get
			{
				return m_strModId;
			}
			set
			{
				SetPropertyIfChanged(ref m_strModId, value, () => Id);
			}
		}

		/// <summary>
		/// Gets or sets the DownloadId of the mod.
		/// </summary>
		/// <remarks>The DownloadId of the mod</remarks>
		public string DownloadId
		{
			get
			{
				return m_strDownloadId;
			}
			set
			{
				SetPropertyIfChanged(ref m_strDownloadId, value, () => DownloadId);
			}
		}

		/// <summary>
		/// Gets or sets the filename of the mod.
		/// </summary>
		/// <value>The filename of the mod.</value>
		public string FileName
		{
			get
			{
				return m_strFileName;
			}
			set
			{
				SetPropertyIfChanged(ref m_strFileName, value, () => FileName);
			}
		}

		/// <summary>
		/// Gets or sets the name of the mod.
		/// </summary>
		/// <value>The name of the mod.</value>
		public string ModName
		{
			get
			{
				return m_strModName;
			}
			set
			{
				SetPropertyIfChanged(ref m_strModName, value, () => ModName);
			}
		}

		/// <summary>
		/// Gets or sets the human readable form of the mod's version.
		/// </summary>
		/// <value>The human readable form of the mod's version.</value>
		public string HumanReadableVersion
		{
			get
			{
				return m_strHumanReadableVersion;
			}
			set
			{
				SetPropertyIfChanged(ref m_strHumanReadableVersion, value, () => HumanReadableVersion);
			}
		}

		/// <summary>
		/// Gets or sets the last known mod version.
		/// </summary>
		/// <value>The last known mod version.</value>
		public string LastKnownVersion
		{
			get
			{
				return m_strLastKnownVersion;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strLastKnownVersion, value, () => LastKnownVersion);
			}
		}

		/// <summary>
		/// Gets or sets the category Id.
		/// </summary>
		/// <value>The category Id.</value>
		public Int32 CategoryId
		{
			get
			{
				return m_strCategoryId;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strCategoryId, value, () => CategoryId);
			}
		}

		/// <summary>
		/// Gets or sets the custom category Id.
		/// </summary>
		/// <value>The custom category Id.</value>
		public Int32 CustomCategoryId
		{
			get
			{
				return m_strCustomCategoryId;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strCustomCategoryId, value, () => CustomCategoryId);
			}
		}

		/// <summary>
		/// Gets or sets the Endorsement state of the mod.
		/// </summary>
		/// <value>The Endorsement state of the mod.</value>
		public bool? IsEndorsed
		{
			get
			{
				return m_booIsEndorsed;
			}
			set
			{
				SetPropertyIfChanged(ref m_booIsEndorsed, value, () => IsEndorsed);
			}
		}

		/// <summary>
		/// Gets or sets the version of the mod.
		/// </summary>
		/// <value>The version of the mod.</value>
		public Version MachineVersion
		{
			get
			{
				return m_verMachineVersion;
			}
			set
			{
				SetPropertyIfChanged(ref m_verMachineVersion, value, () => MachineVersion);
			}
		}

		/// <summary>
		/// Gets or sets the author of the mod.
		/// </summary>
		/// <value>The author of the mod.</value>
		public string Author
		{
			get
			{
				return m_strAuthor;
			}
			set
			{
				SetPropertyIfChanged(ref m_strAuthor, value, () => Author);
			}
		}

		/// <summary>
		/// Gets or sets the description of the mod.
		/// </summary>
		/// <value>The description of the mod.</value>
		public string Description
		{
			get
			{
				return m_strDescription;
			}
			set
			{
				SetPropertyIfChanged(ref m_strDescription, value, () => Description);
			}
		}

		/// <summary>
		/// Gets or sets the install date of the mod.
		/// </summary>
		/// <value>The install date of the mod.</value>
		public string InstallDate
		{
			get
			{
				return m_strInstallDate;
			}
			set
			{
				SetPropertyIfChanged(ref m_strInstallDate, value, () => InstallDate);
			}
		}

		/// <summary>
		/// Gets or sets the website of the mod.
		/// </summary>
		/// <value>The website of the mod.</value>
		public Uri Website
		{
			get
			{
				return m_uriWebsite;
			}
			set
			{
				SetPropertyIfChanged(ref m_uriWebsite, value, () => Website);
			}
		}

		/// <summary>
		/// Gets or sets the mod's screenshot.
		/// </summary>
		/// <value>The mod's screenshot.</value>
		public ExtendedImage Screenshot
		{
			get
			{
				return m_ximScreenshot;
			}
			set
			{
				SetPropertyIfChanged(ref m_ximScreenshot, value, () => Screenshot);
			}
		}

		/// <summary>
		/// Gets or sets whether the user wants to be warned about new versions.
		/// </summary>
		/// <value>Whether the user wants to be warned about new versions</value>
		public bool UpdateWarningEnabled
		{
			get
			{
				return m_booUpdateWarningEnabled;
			}
			set
			{
				SetPropertyIfChanged(ref m_booUpdateWarningEnabled, value, () => UpdateWarningEnabled);
			}
		}

        public int PlaceInModLoadOrder
        {
            get
            {
                return m_intPlaceInModLoadOrder;
            }
            set
            {
                SetPropertyIfChanged(ref m_intPlaceInModLoadOrder, value, () => PlaceInModLoadOrder);
            }
        }

		#endregion

		/// <summary>
		/// Gets whether the mod has a custom install script.
		/// </summary>
		/// <value>Whether the mod has a custom install script.</value>
		public bool HasInstallScript
		{
			get
			{
				return m_sctInstallScript != null;
			}
		}

		/// <summary>
		/// Gets or sets the mod install script.
		/// </summary>
		/// <value>The mod install script.</value>
		public IScript InstallScript
		{
			get
			{
				return m_sctInstallScript;
			}
			set
			{
				if (m_sctInstallScript != null)
					m_sctInstallScript.PropertyChanged -= InstallScript_PropertyChanged;
				SetPropertyIfChanged(ref m_sctInstallScript, value, () => InstallScript);
				if (m_sctInstallScript != null)
					m_sctInstallScript.PropertyChanged += new PropertyChangedEventHandler(InstallScript_PropertyChanged);
			}
		}

		/// <summary>
		/// Gets or sets the mod's readme.
		/// </summary>
		/// <value>The mod's readme.</value>
		public Readme ModReadme
		{
			get
			{
				return m_rmeModReadme;
			}

			set
			{
				SetPropertyIfChanged(ref m_rmeModReadme, value, () => ModReadme);
			}
		}

		/// <summary>
		/// Gets the files in the mod.
		/// </summary>
		/// <value>The files in the mod.</value>
		public IList<VirtualFileSystemItem> ModFiles
		{
			get
			{
				return m_setFiles;
			}
			set
			{
				if (SetPropertyIfChanged(ref m_setFiles, value, () => ModFiles))
				{
					foreach (VirtualFileSystemItem vfiItem in m_setFiles)
					{
						if (String.IsNullOrEmpty(vfiItem.Path))
							continue;
						string strPath = vfiItem.Path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
						if (strPath.Equals(Path.Combine(NexusMod.MetaFolder, "info.xml"), StringComparison.OrdinalIgnoreCase))
						{
							XmlDocument xmlInfo = new XmlDocument();
							xmlInfo.Load(vfiItem.Source);
							this.LoadInfo(xmlInfo.SelectSingleNode("info"), true);
						}
						else if (Path.GetFileNameWithoutExtension(strPath).Equals(Path.Combine(NexusMod.MetaFolder, "screenshot"), StringComparison.OrdinalIgnoreCase))
						{
							if (Screenshot == null)
								Screenshot = new ExtendedImage(File.ReadAllBytes(vfiItem.Source));
						}
						else if (Path.GetFileNameWithoutExtension(strPath).Equals(Path.Combine(NexusMod.MetaFolder, "readme"), StringComparison.OrdinalIgnoreCase))
						{
							if ((ModReadme == null) && Readme.ValidExtensions.Contains(x => x.Equals(Path.GetExtension(strPath))))
								ModReadme = new Readme(vfiItem.Source, File.ReadAllText(vfiItem.Source));
						}
						else
						{
							foreach (IScriptType stpType in ScriptTypeRegistry.Types)
							{
								foreach (string strScriptName in stpType.FileNames)
								{
									if (strPath.Equals(Path.Combine(NexusMod.MetaFolder, strScriptName), StringComparison.OrdinalIgnoreCase))
										InstallScript = stpType.LoadScript(File.ReadAllText(vfiItem.Source));
									break;
								}
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Gets the path where the project file is located.
		/// </summary>
		/// <value>The path where the project file is located.</value>
		public string FilePath { get; private set; }

		/// <summary>
		/// Gets whether the project has changed since the last time it was saved.
		/// </summary>
		/// <value>Whether the project has changed since the last time it was saved.</value>
		public bool IsDirty
		{
			get
			{
				return m_booIsDirty;
			}
			protected set
			{
				SetPropertyIfChanged(ref m_booIsDirty, value, () => IsDirty);
			}
		}

		/// <summary>
		/// Gets or sets the <see cref="IScriptTypeRegistry"/>.
		/// </summary>
		/// <value>The <see cref="IScriptTypeRegistry"/> contianing the list of available script types.</value>
		protected IScriptTypeRegistry ScriptTypeRegistry { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the required dependencies.
        /// </summary>
        /// <param name="p_strScriptTypeRegistry">The <see cref="IScriptTypeRegistry"/> contianing the list of available script types.</param>
        public Project(IScriptTypeRegistry p_strScriptTypeRegistry)
		{
			m_setFiles = new ThreadSafeObservableList<VirtualFileSystemItem>();
			ModReadme = new Readme(ReadmeFormat.PlainText, null);
			ScriptTypeRegistry = p_strScriptTypeRegistry;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the required dependencies.
		/// </summary>
		/// <param name="p_strPath">The path from which to load saved project data.</param>
		/// <param name="p_strScriptTypeRegistry">The <see cref="IScriptTypeRegistry"/> contianing the list of available script types.</param>
		public Project(string p_strPath, IScriptTypeRegistry p_strScriptTypeRegistry)
			: this(p_strScriptTypeRegistry)
		{
			Load(p_strPath);
		}

		#endregion

		#region Serialization

		/// <summary>
		/// Saves the project to a file.
		/// </summary>
		/// <remarks>
		/// The specified file will be overwriiten.
		/// </remarks>
		/// <param name="p_strPath">The path to the file in which to save the project.</param>
		public void Save(string p_strPath)
		{
			XmlDocument xmlProject = new XmlDocument();
			XmlNode xndRoot = xmlProject.AppendChild(xmlProject.CreateElement("modProject"));

			xndRoot.AppendChild(this.SaveInfo(xmlProject, true));

			if (InstallScript != null)
			{
				XmlNode xndScript = xndRoot.AppendChild(xmlProject.CreateElement("InstallScript"));
				xndScript.Attributes.Append(xmlProject.CreateAttribute("type")).Value = InstallScript.Type.TypeId;
				string strScript = InstallScript.Type.SaveScript(InstallScript);
				xndScript.InnerXml = strScript;
			}

			if (ModReadme != null)
			{
				XmlNode xndReadme = xndRoot.AppendChild(xmlProject.CreateElement("ModReadme"));
				xndReadme.Attributes.Append(xmlProject.CreateAttribute("format")).Value = ModReadme.Format.ToString();
				xndReadme.InnerText = ModReadme.Text;
			}

			if (!ModFiles.IsNullOrEmpty())
			{
				XmlNode xndFiles = xndRoot.AppendChild(xmlProject.CreateElement("ModFiles"));
				foreach (VirtualFileSystemItem vfiFile in ModFiles)
				{
					XmlNode xndFile = xndFiles.AppendChild(xmlProject.CreateElement("file"));
					xndFile.Attributes.Append(xmlProject.CreateAttribute("IsDirectory")).Value = vfiFile.IsDirectory.ToString();
					xndFile.Attributes.Append(xmlProject.CreateAttribute("Path")).Value = vfiFile.Path;
					xndFile.Attributes.Append(xmlProject.CreateAttribute("Source")).Value = vfiFile.Source;
				}
			}

			xmlProject.Save(p_strPath);
			FilePath = p_strPath;
			IsDirty = false;
		}

		/// <summary>
		/// Loads the project from a file.
		/// </summary>
		/// <param name="p_strPath">The path to the file from which to load the project.</param>
		protected void Load(string p_strPath)
		{
			XmlDocument xmlProject = new XmlDocument();
			xmlProject.Load(p_strPath);

			XmlNode xndRoot = xmlProject.SelectSingleNode("modProject");

			XmlNode xndInfo = xndRoot.SelectSingleNode("info");
			this.LoadInfo(xndInfo);

			XmlNode xndScript = xndRoot.SelectSingleNode("InstallScript");
			if (xndScript != null)
			{
				IScriptType stpType = ScriptTypeRegistry.GetType(xndScript.Attributes["type"].Value);
				if (stpType == null)
					throw new Exception("Unrecognized script type: " + xndScript.Attributes["type"].Value);
				InstallScript = stpType.LoadScript(xndScript.InnerXml);
			}

			XmlNode xndReadme = xndRoot.SelectSingleNode("ModReadme");
			if (xndReadme != null)
			{
				string strFormat = xndReadme.Attributes["format"].Value;
				ReadmeFormat fmtFormat = ReadmeFormat.PlainText;
				if (Enum.GetNames(typeof(ReadmeFormat)).Contains(x => x.Equals(strFormat, StringComparison.InvariantCultureIgnoreCase)))
					fmtFormat = (ReadmeFormat)Enum.Parse(typeof(ReadmeFormat), strFormat);
				ModReadme = new Readme(fmtFormat, xndReadme.InnerText);
			}

			XmlNode xndFiles = xndRoot.SelectSingleNode("ModFiles");
			if (xndFiles != null)
			{
				foreach (XmlNode xndFile in xndFiles.ChildNodes)
				{
					bool booIsDirectory = false;
					if (!bool.TryParse(xndFile.Attributes["IsDirectory"].Value, out booIsDirectory))
						booIsDirectory = false;
					ModFiles.Add(new VirtualFileSystemItem(xndFile.Attributes["Source"].Value, xndFile.Attributes["Path"].Value, booIsDirectory));
				}
			}
			FilePath = p_strPath;
			IsDirty = false;
		}

		#endregion

		#region Mod Info Management

		/// <summary>
		/// Updates the object's proerties to the values of the
		/// given <see cref="IModInfo"/>.
		/// </summary>
		/// <param name="p_mifInfo">The <see cref="IModInfo"/> whose values
		/// are to be used to update this object's properties.</param>
		/// <param name="p_booOverwriteAllValues">Whether to overwrite the current info values,
		/// or just the empty ones.</param>
		public void UpdateInfo(IModInfo p_mifInfo, bool? p_booOverwriteAllValues)
		{
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(Id))
				Id = p_mifInfo.Id;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(DownloadId))
				DownloadId = p_mifInfo.DownloadId;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(ModName))
				ModName = p_mifInfo.ModName;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(DownloadId))
				FileName = p_mifInfo.FileName;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(HumanReadableVersion))
				HumanReadableVersion = p_mifInfo.HumanReadableVersion;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(LastKnownVersion))
				LastKnownVersion = p_mifInfo.LastKnownVersion;
			if ((p_booOverwriteAllValues == true) || (IsEndorsed != p_mifInfo.IsEndorsed))
				IsEndorsed = p_mifInfo.IsEndorsed;
			if ((p_booOverwriteAllValues == true) || (MachineVersion == null))
				MachineVersion = p_mifInfo.MachineVersion;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(Author))
				Author = p_mifInfo.Author;
			if ((p_booOverwriteAllValues == true) || (CategoryId != p_mifInfo.CategoryId))
				CategoryId = p_mifInfo.CategoryId;
			if ((p_booOverwriteAllValues == true) || (CustomCategoryId != p_mifInfo.CustomCategoryId))
				CustomCategoryId = p_mifInfo.CustomCategoryId;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(Description))
				Description = p_mifInfo.Description;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(InstallDate))
				InstallDate = p_mifInfo.InstallDate;
			if ((p_booOverwriteAllValues == true) || (Website == null))
				Website = p_mifInfo.Website;
			if ((p_booOverwriteAllValues == true) || (Screenshot == null))
				Screenshot = p_mifInfo.Screenshot;
		}

		/// <summary>
		/// Serializes the given <see cref="IModInfo"/> to an XML fragment.
		/// </summary>
		/// <param name="p_xmlDocument">The <see cref="XmlDocument"/> to use to create the XML elements
		/// created during the unparsing.</param>
		/// <param name="p_booEncodeScreenshot">Whether or not to encode the <see cref="IModInfo.Screenshot"/>
		/// into the XML fragment.</param>
		/// <returns>The <see cref="XmlNode"/> that is the root of the XML fragement
		/// that represents the given <see cref="IModInfo"/></returns>
		public XmlNode SaveInfo(XmlDocument p_xmlDocument, bool p_booEncodeScreenshot)
		{
			XmlNode xndInfo = p_xmlDocument.CreateElement("info");
			xndInfo.AppendChild(p_xmlDocument.CreateElement("ModName")).InnerText = ModName;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("HumanReadableVersion")).InnerText = HumanReadableVersion;
			if (MachineVersion != null)
				xndInfo.AppendChild(p_xmlDocument.CreateElement("MachineVersion")).InnerText = MachineVersion.ToString();
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Author")).InnerText = Author;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Description")).InnerText = Description;
			if (Website != null)
				xndInfo.AppendChild(p_xmlDocument.CreateElement("Website")).InnerText = Website.ToString();
			if (p_booEncodeScreenshot && (Screenshot != null))
				xndInfo.AppendChild(p_xmlDocument.CreateElement("Screenshot")).InnerText = Convert.ToBase64String(Screenshot.Data, Base64FormattingOptions.InsertLineBreaks);
			return xndInfo;
		}

		/// <summary>
		/// Deserializes an <see cref="IModInfo"/> from the given XML fragment into the
		/// given <see cref="IModInfo"/>.
		/// </summary>
		/// <param name="p_xndInfo">The XML fragment from which to deserialize the <see cref="IModInfo"/>.</param>
		protected void LoadInfo(XmlNode p_xndInfo)
		{
			LoadInfo(p_xndInfo, false);
		}

		/// <summary>
		/// Deserializes an <see cref="IModInfo"/> from the given XML fragment into the
		/// given <see cref="IModInfo"/>.
		/// </summary>
		/// <param name="p_xndInfo">The XML fragment from which to deserialize the <see cref="IModInfo"/>.</param>
		/// <param name="p_booFillOnlyEmptyValues">Whether to only overwrite <c>null</c> or empty values.</param>
		protected void LoadInfo(XmlNode p_xndInfo, bool p_booFillOnlyEmptyValues)
		{
			XmlNode xndModName = p_xndInfo.SelectSingleNode("ModName");
			if ((xndModName != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(ModName)))
				ModName = xndModName.InnerText;

			XmlNode xndHumanReadableVersion = p_xndInfo.SelectSingleNode("HumanReadableVersion");
			if ((xndHumanReadableVersion != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(HumanReadableVersion)))
				HumanReadableVersion = xndHumanReadableVersion.InnerText;

			XmlNode xndMachineVersion = p_xndInfo.SelectSingleNode("MachineVersion");
			if ((xndMachineVersion != null) && (!p_booFillOnlyEmptyValues || (MachineVersion == null)))
				MachineVersion = new Version(xndMachineVersion.InnerText);

			XmlNode xndAuthor = p_xndInfo.SelectSingleNode("Author");
			if ((xndAuthor != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(Author)))
				Author = xndAuthor.InnerText;

			XmlNode xndDescription = p_xndInfo.SelectSingleNode("Description");
			if ((xndDescription != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(Description)))
				Description = xndDescription.InnerText;

			XmlNode xndWebsite = p_xndInfo.SelectSingleNode("Website");
			if ((xndWebsite != null) && (!p_booFillOnlyEmptyValues || (Website == null)))
				Website = new Uri(xndWebsite.InnerText);

			XmlNode xndScreenshot = p_xndInfo.SelectSingleNode("Screenshot");
			if ((xndScreenshot != null) && (!p_booFillOnlyEmptyValues || (Screenshot == null)))
				Screenshot = new ExtendedImage(Convert.FromBase64String(xndScreenshot.InnerText));
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the project.
		/// </summary>
		/// <remarks>
		/// This marks the project as dirty.
		/// </remarks>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		protected override void OnPropertyChanged(PropertyChangedEventArgs e)
		{
			if (!e.PropertyName.Equals(ObjectHelper.GetPropertyName(() => IsDirty)))
				IsDirty = true;
			base.OnPropertyChanged(e);
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the <see cref="InstallScript"/>.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		protected void InstallScript_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			OnPropertyChanged(() => InstallScript);
		}
	}
}
