using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Nexus.Client.Games;

namespace Nexus.Client
{
	public partial class GameDetectionForm : Form
	{
		private GameDetectionVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected GameDetectionVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				List<IGameModeDescriptor> lstGameModes = new List<IGameModeDescriptor>(m_vmlViewModel.SupportedGameModes.RegisteredGameModes);
				for (Int32 i = 0; i < lstGameModes.Count; i++)
				{
					GameModeListViewItem gliGameModeItem = new GameModeListViewItem(lstGameModes[i], m_vmlViewModel.GameDetector);
					gameModeListView1.Controls.Add(gliGameModeItem);
				}

				Size szeMax =Screen.GetWorkingArea(this).Size;
				Size = new Size((Int32)(szeMax.Width * .9), (Int32)(szeMax.Height * .9));
				Int32 intWidthOffset = Size.Width - gameModeListView1.ClientSize.Width;
				Int32 intHeightOffset = Size.Height - gameModeListView1.ClientSize.Height;
				szeMax = gameModeListView1.ClientSize;
				Int32 intIdealWidthCount = (Int32)Math.Ceiling(Math.Sqrt(lstGameModes.Count));
				Int32 intWidth = 0;
				Int32 intHeigth = 0;
				do
				{
					intWidth = gameModeListView1.PreferredSize.Width / lstGameModes.Count * intIdealWidthCount;
					intHeigth = gameModeListView1.PreferredSize.Height * lstGameModes.Count / intIdealWidthCount;
				} while ((szeMax.Width < intWidth) && (--intIdealWidthCount > 0));
				if (intHeigth > szeMax.Height)
				{
					intWidthOffset += 17;
					intHeigth = szeMax.Height;
				}
				Size = new Size(intWidth + intWidthOffset, intHeigth + intHeightOffset);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_iniApplicationInitializer">The application initializer.</param>
		public GameDetectionForm(GameDetectionVM p_vmlGameDetection)
		{
			InitializeComponent();
			ViewModel = p_vmlGameDetection;
		}

		#endregion
	}
}
