using System;
using Nexus.UI.Controls;
using Nexus.Client.Util;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public class HeaderInfoVM
	{
		protected HeaderInfo HeaderInfo { get; private set; }
		public string Title { get; set; }
		public Color TextColour { get; set; }
		public TextPosition TextPosition { get; set; }
		public string ImagePath { get; set; }
		public bool ShowImage { get; set; }
		public bool ShowFade { get; set; }
		public Int32 Height { get; set; }

		public HeaderInfoVM(HeaderInfo p_hdrHeader)
		{
			if (p_hdrHeader.Height < 0)
				p_hdrHeader.Height = 0;
			else if (p_hdrHeader.Height > 1000)
				p_hdrHeader.Height = 1000;
			HeaderInfo = p_hdrHeader;
			Reset();
		}

		public HeaderInfo Commit()
		{
			HeaderInfo.Title = Title;
			HeaderInfo.TextColour = TextColour;
			HeaderInfo.TextPosition = TextPosition;
			HeaderInfo.ImagePath = ImagePath;
			HeaderInfo.ShowImage = ShowImage;
			HeaderInfo.ShowFade = ShowFade;
			HeaderInfo.Height = Height;
			return HeaderInfo;
		}

		public void Reset()
		{
			Title = HeaderInfo.Title;
			TextColour = HeaderInfo.TextColour;
			TextPosition = HeaderInfo.TextPosition;
			ImagePath = HeaderInfo.ImagePath;
			ShowImage = HeaderInfo.ShowImage;
			ShowFade = HeaderInfo.ShowFade;
			Height = HeaderInfo.Height;
		}

		public void Reset(string p_strPropertyName)
		{
			if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Title)))
				Title = HeaderInfo.Title;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.TextColour)))
				TextColour = HeaderInfo.TextColour;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.TextPosition)))
				TextPosition = HeaderInfo.TextPosition;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.ImagePath)))
				ImagePath = HeaderInfo.ImagePath;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.ShowImage)))
				ShowImage = HeaderInfo.ShowImage;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.ShowFade)))
				ShowFade = HeaderInfo.ShowFade;
			else if (p_strPropertyName.Equals(ObjectHelper.GetPropertyName(() => this.Height)))
				Height = HeaderInfo.Height;
		}
	}

	[Flags]
	public enum HeaderProperties
	{
		Title = 1,
		TextColour = 2,
		TextPosition = 4,
		Image = 8,
		Height = 16
	}

	public class HeaderEditorVM : IViewModel
	{
		public event EventHandler HeaderInfoValidated = delegate { };

		public HeaderInfo HeaderInfo
		{
			set
			{
				HeaderInfoVM = new HeaderInfoVM(value);
			}
		}

		public HeaderInfoVM HeaderInfoVM { get; private set; }
		public IEnumerable TextPositions { get; private set; }
		public IList<VirtualFileSystemItem> ModFiles { get; private set; }
		public ErrorContainer Errors { get; private set; }
		public bool TitleVisible { get; private set; }
		public bool TextColourVisible { get; private set; }
		public bool TextPositionVisible { get; private set; }
		public bool ImageVisible { get; private set; }
		public bool HeightVisible { get; private set; }

		public HeaderEditorVM(HeaderInfo p_hdrHeader, IList<VirtualFileSystemItem> p_lstModFiles, HeaderProperties p_hrpEditableProperties)
		{
			HeaderInfo = p_hdrHeader;
			ModFiles = p_lstModFiles;

			TitleVisible = (p_hrpEditableProperties & HeaderProperties.Title) > 0;
			TextColourVisible = (p_hrpEditableProperties & HeaderProperties.TextColour) > 0;
			TextPositionVisible = (p_hrpEditableProperties & HeaderProperties.TextPosition) > 0;
			ImageVisible = (p_hrpEditableProperties & HeaderProperties.Image) > 0;
			HeightVisible = (p_hrpEditableProperties & HeaderProperties.Height) > 0;

			TextPositions = Enum.GetValues(typeof(TextPosition));
			Errors = new ErrorContainer();
		}

		protected void OnHeaderInfoValidated()
		{
			HeaderInfoValidated(this, new EventArgs());
		}

		public void SaveHeaderInfo()
		{
			HeaderInfoVM.Commit();
		}

		#region IViewModel Members

		public bool Validate()
		{
			return true;
		}

		#endregion
	}
}
