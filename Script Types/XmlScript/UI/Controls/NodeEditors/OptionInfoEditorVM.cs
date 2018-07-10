using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public class OptionInfoVM
	{
		protected Option Option { get; private set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string ImagePath { get; set; }

		public OptionInfoVM(Option p_optOption)
		{
			Option = p_optOption;
			Reset();
		}

		public Option Commit()
		{
			Option.Name = Name;
			Option.Description = Description;
			Option.ImagePath = ImagePath;
			return Option;
		}

		public void Reset()
		{
			Name = Option.Name;
			Description = Option.Description;
			ImagePath = Option.ImagePath;
		}

		public void Reset(string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Name)))
				Name = Option.Name;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Description)))
				Description = Option.Description;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.ImagePath)))
				ImagePath = Option.ImagePath;
		}
	}

	public class OptionInfoEditorVM:IViewModel
	{
		public event EventHandler OptionValidated = delegate { };

		public IList<VirtualFileSystemItem> ModFiles { get; private set; }

		public Option Option
		{
			set
			{
				OptionInfoVM = new OptionInfoVM(value);
			}
		}

		public OptionInfoVM OptionInfoVM { get; private set; }
		public ErrorContainer Errors { get; private set; }

		public OptionInfoEditorVM(Option p_optOption, IList<VirtualFileSystemItem> p_lstModFiles)
		{
			Option = p_optOption;
			ModFiles = p_lstModFiles;
			Errors = new ErrorContainer();
		}

		protected void OnOptionValidated()
		{
			OptionValidated(this, new EventArgs());
		}

		public void SaveOptionInfo()
		{
			if (Validate())
				OptionInfoVM.Commit();
		}

		/// <summary>
		/// Ensures that the option name is valid.
		/// </summary>
		/// <returns><c>true</c> if the option name is valid;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateOptionName()
		{
			bool booIsValid = true;
			Errors.Clear<Option>(x => x.Name);
			if (String.IsNullOrEmpty(OptionInfoVM.Name))
			{
				Errors.SetError<Option>(x => x.Name, "Name is required.");
				booIsValid = false;
			}
			OnOptionValidated();
			return booIsValid;
		}

		public Image GetImage(string p_strImagePath)
		{
			if (ModFiles == null)
				return null;
			VirtualFileSystemItem vfiItem = ModFiles.Find((f) => { return f.Path.Equals(p_strImagePath, StringComparison.OrdinalIgnoreCase); });
			byte[] bteImage = null;
			if (vfiItem != null)
			{
				if (Archive.IsArchivePath(vfiItem.Source))
				{
					KeyValuePair<string, string> kvpArchive = Archive.ParseArchivePath(vfiItem.Source);
					using (Archive arcMod = new Archive(kvpArchive.Key))
					{
						bteImage = arcMod.GetFileContents(kvpArchive.Value);
					}
				}
				else if (File.Exists(vfiItem.Source))
				{
					bteImage = File.ReadAllBytes(vfiItem.Source);
				}
			}
			if (bteImage == null)
				return null;
			using (MemoryStream msmImage = new MemoryStream(bteImage))
			{
				try
				{
					Image imgTmp = Image.FromStream(msmImage);
					return new Bitmap(imgTmp);
				}
				catch (ArgumentException)
				{
				}
			}
			return null;
		}

		#region IViewModel Members

		public bool Validate()
		{
			return ValidateOptionName();
		}

		#endregion
	}
}
