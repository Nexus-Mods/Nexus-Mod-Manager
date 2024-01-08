using System;
using Nexus.UI.Controls;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using System.ComponentModel;
using System.Drawing;
using Nexus.Client.ModRepositories;
using Nexus.Client.Settings;

namespace Nexus.Client.ModAuthoring.UI.Controls
{
	/// <summary>
	/// The view model representing an <see cref="IModInfo"/> being edited.
	/// </summary>
	public class ModInfoVM : ObservableObject
	{
		private string m_strModName = null;
		private string m_strModId = null;
		private string m_strDownloadId = null;
		private string m_strHumanReadableVersion = null;
		private string m_strLastKnownVersion = null;
		private string m_strMachineVersion = null;
		private string m_strAuthor = null;
		private string m_strDescription = null;
		private string m_strInstallDate = null;
		private string m_strWebsite = null;
		private Int32 m_intCategoryID = 0;
		private bool? m_booIsEndorsed = false;
		private ExtendedImage m_eimScreenshot = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="IModInfo"/> being edited.
		/// </summary>
		/// <value>The <see cref="IModInfo"/> being edited.</value>
		protected IModInfo ModInfo { get; private set; }

		/// <summary>
		/// Gets or sets the mod id of the mod file.
		/// </summary>
		/// <value>The mod id of the mod file.</value>
		public string ModId
		{
			get
			{
				return m_strModId;
			}
			set
			{
				SetPropertyIfChanged(ref m_strModId, value, () => ModId);
			}
		}

		/// <summary>
		/// Gets or sets the download id of the mod file.
		/// </summary>
		/// <value>The download id of the mod file.</value>
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
		/// <value>The the last known mod version.</value>
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
		/// Gets or sets the version of the mod.
		/// </summary>
		/// <value>The version of the mod.</value>
		public string MachineVersion
		{
			get
			{
				return m_strMachineVersion;
			}
			set
			{
				SetPropertyIfChanged(ref m_strMachineVersion, value, () => MachineVersion);
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
		/// Gets or sets the Install Date of the mod.
		/// </summary>
		/// <value>The Install Date of the mod.</value>
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
		public string Website
		{
			get
			{
				return m_strWebsite;
			}
			set
			{
				SetPropertyIfChanged(ref m_strWebsite, value, () => Website);
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
				return m_eimScreenshot;
			}
			set
			{
				SetPropertyIfChanged(ref m_eimScreenshot, value, () => Screenshot);
			}
		}

		/// <summary>
		/// Gets or sets the CategoryId of the mod.
		/// </summary>
		/// <value>The CategoryId of the mod.</value>
		public Int32 CategoryId 
		{
			get
			{
				return m_intCategoryID;
			}
			set
			{
				SetPropertyIfChanged(ref m_intCategoryID, value, () => CategoryId);
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
		/// Gets the validation errors for the current values.
		/// </summary>
		/// <value>The validation errors for the current values.</value>
		public ErrorContainer Errors { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view model with the <see cref="IModInfo"/>
		/// that it will represent.
		/// </summary>
		/// <param name="p_mifInfo">The <see cref="IModInfo"/> being edited.</param>
		public ModInfoVM(IModInfo p_mifInfo)
		{
			Errors = new ErrorContainer();
			ModInfo = p_mifInfo;
			Reset();
		}

		#endregion

		#region Commit

		/// <summary>
		/// Commits the current values to the <see cref="IModInfo"/>
		/// being edited.
		/// </summary>
		/// <returns>The <see cref="IModInfo"/> being edited with the new
		/// values, if they pass validation. If the current values are invalid, the
		/// original <see cref="IModInfo"/> is returned.</returns>
		public IModInfo Commit()
		{
			if (Validate() && (ModInfo != null))
			{
				ModInfo midInfo = new ModInfo(ModInfo);
				midInfo.Id = ModId;
				midInfo.DownloadId = DownloadId;
				midInfo.Author = Author;
				midInfo.Description = Description;
				midInfo.HumanReadableVersion = HumanReadableVersion;
				midInfo.LastKnownVersion = LastKnownVersion;
				midInfo.MachineVersion = String.IsNullOrEmpty(MachineVersion) ? null : new Version(MachineVersion);
				midInfo.ModName = ModName;
				midInfo.InstallDate = InstallDate;
				midInfo.Website = String.IsNullOrEmpty(Website) ? null : new Uri(Website);
				midInfo.Screenshot = Screenshot;
				midInfo.CategoryId = CategoryId;
				midInfo.IsEndorsed = IsEndorsed;
				ModInfo.UpdateInfo(midInfo, true);
			}
			return ModInfo;
		}

		/// <summary>
		/// This undoes any changes made to the values since the last commit.
		/// </summary>
		public void Reset()
		{
			LoadInfoValues(ModInfo);
		}

		/// <summary>
		/// Loads the values from the given <see cref="IModInfo"/>
		/// into the control.
		/// </summary>
		/// <remarks>
		///  This does not change which <see cref="IModInfo"/>
		/// is being edited, but simply loads the values into the view model.
		/// </remarks>
		/// <param name="p_mifModInfo">The <see cref="IModInfo"/> whose values are to be
		/// loaded into the view model.</param>
		public void LoadInfoValues(IModInfo p_mifModInfo)
		{
			if (p_mifModInfo != null)
			{
				Author = p_mifModInfo.Author;
				ModId = p_mifModInfo.Id;
				DownloadId = p_mifModInfo.DownloadId;
				Description = p_mifModInfo.Description;
				HumanReadableVersion = p_mifModInfo.HumanReadableVersion;
				LastKnownVersion = p_mifModInfo.LastKnownVersion;
				MachineVersion = (p_mifModInfo.MachineVersion == null) ? null : p_mifModInfo.MachineVersion.ToString();
				ModName = p_mifModInfo.ModName;
				InstallDate = p_mifModInfo.InstallDate;
				Website = (p_mifModInfo.Website == null) ? null : p_mifModInfo.Website.ToString();
				Screenshot = p_mifModInfo.Screenshot;
				CategoryId = p_mifModInfo.CategoryId;
				IsEndorsed = p_mifModInfo.IsEndorsed;
			}
		}

		/// <summary>
		/// This undoes any changes made to the specified value since the last commit.
		/// </summary>
		public void Reset(string p_strPropertyName)
		{
			if (ModInfo == null)
				return;
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Author)))
				Author = ModInfo.Author;
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.DownloadId)))
				DownloadId = ModInfo.DownloadId;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Description)))
				Description = ModInfo.Description;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.HumanReadableVersion)))
				HumanReadableVersion = ModInfo.HumanReadableVersion;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.MachineVersion)))
				MachineVersion = (ModInfo.MachineVersion == null) ? null : ModInfo.MachineVersion.ToString();
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.ModName)))
				ModName = ModInfo.ModName;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Website)))
				Website = (ModInfo.Website == null) ? null : ModInfo.Website.ToString();
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Screenshot)))
				Screenshot = ModInfo.Screenshot;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.IsEndorsed)))
				IsEndorsed = ModInfo.IsEndorsed;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.CategoryId)))
				IsEndorsed = ModInfo.IsEndorsed;
		}

		#endregion

		#region Validation

		/// <summary>
		/// Ensures that a mod name has been entered.
		/// </summary>
		/// <returns><c>true</c> if the name is present;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateModName()
		{
			bool booIsValid = true;
			Errors.Clear(() => ModName);
			if (String.IsNullOrEmpty(ModName))
			{
				Errors.SetError(() => ModName, "Name is required.");
				booIsValid = false;
			}
			return booIsValid;
		}

		/// <summary>
		/// Ensures that the version is valid, if present.
		/// </summary>
		/// <returns><c>true</c> if the version is valid or not present;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateMachineVersion()
		{
			bool booIsValid = true;
			Errors.Clear(() => MachineVersion);
			if (!String.IsNullOrEmpty(MachineVersion))
			{
				try
				{
					new Version(MachineVersion);
				}
				catch
				{
					Errors.SetError(() => MachineVersion, "Invalid version. Must be in #.#.#.# format.");
					booIsValid = false;
				}
			}
			return booIsValid;
		}

		/// <summary>
		/// Ensures that the URL is valid, if present.
		/// </summary>
		/// <returns><c>true</c> if the website is valid or not present;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateWebsite()
		{
			bool booIsValid = true;
			Errors.Clear(() => Website);
			if (!String.IsNullOrEmpty(Website))
			{
				Uri uri;
				if (!Uri.TryCreate(Website, UriKind.Absolute, out uri) || uri.IsFile || (uri.Scheme != "http" && uri.Scheme != "https"))
				{
					Errors.SetError(() => Website, "Invalid web address specified.\nDid you miss the 'https://'?");
					booIsValid = false;
				}
			}
			return booIsValid;
		}

		/// <summary>
		/// Validate the <see cref="IModInfo"/>.
		/// </summary>
		/// <returns><c>true</c> if all controls passed validation;
		/// <c>false</c> otherwise.</returns>
		public bool Validate()
		{
			bool booIsValid = ValidateWebsite();
			booIsValid &= ValidateModName();
			return booIsValid;
		}

		#endregion
	}

	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that allow editing of <see cref="IModInfo"/>.
	/// </summary>
	public class ModInfoEditorVM : ObservableObject, IViewModel
	{
		private IModInfo m_mifModInfo = null;

		#region Properties

		/// <summary>
		/// Sets the <see cref="IModInfo"/> being edited.
		/// </summary>
		public IModInfo ModInfo
		{
			private get
			{
				return m_mifModInfo;
			}
			set
			{
				m_mifModInfo = value;
				EditedModInfoVM = new ModInfoVM(m_mifModInfo);
				OnPropertyChanged(() => EditedModInfoVM);
			}
		}

		/// <summary>
		/// Gets the <see cref="ModInfoVM"/> representing the <see cref="IModInfo"/>
		/// being edited.
		/// </summary>
		/// <value>The <see cref="ModInfoVM"/> representing the <see cref="IModInfo"/>
		/// being edited.</value>
		public ModInfoVM EditedModInfoVM { get; protected set; }

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view model with its depenedencies.
		/// </summary>
		/// <param name="p_mifModInfo">The <see cref="IModInfo"/> to edit.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		public ModInfoEditorVM(IModInfo p_mifModInfo, ISettings p_setSettings)
		{
			ModInfo = p_mifModInfo;
			Settings = p_setSettings;
		}

		#endregion

		#region IViewModel Members

		/// <summary>
		/// Validate the <see cref="IModInfo"/>.
		/// </summary>
		/// <returns><c>true</c> if all controls passed validation;
		/// <c>false</c> otherwise.</returns>
		public bool Validate()
		{
			return EditedModInfoVM.Validate();
		}

		#endregion
	}
}
