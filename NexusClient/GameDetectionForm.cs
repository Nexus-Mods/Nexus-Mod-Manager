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
				List<IGameModeFactory> lstFactories = new List<IGameModeFactory>(m_vmlViewModel.SupportedGameModes.RegisteredGameModeFactories);
				Int32 intWrapIndex = (Int32)Math.Sqrt(lstFactories.Count);
				for (Int32 i = 0; i < lstFactories.Count; i++)
				{
					GameModeListViewItem gliGameModeItem = new GameModeListViewItem(lstFactories[i].GameModeDescriptor);
					gameModeListView1.Controls.Add(gliGameModeItem);
				}
				if (gameModeListView1.HorizontalScroll.Visible)
					for (Int32 i = 0; i < lstFactories.Count; i++)
						gameModeListView1.SetFlowBreak(gameModeListView1.Controls[i], (i + 1) % intWrapIndex == 0);
				if (gameModeListView1.HorizontalScroll.Visible)
				{
					for (Int32 i = 0; i < lstFactories.Count; i++)
						gameModeListView1.SetFlowBreak(gameModeListView1.Controls[i], false);
					gameModeListView1.WrapContents = false;
					gameModeListView1.Padding = new Padding(gameModeListView1.Padding.Left, gameModeListView1.Padding.Top, gameModeListView1.Padding.Right + 17, gameModeListView1.Padding.Bottom);
				}
			}
		}

		/// <summary>
		/// Gets the splash image used to display progress.
		/// </summary>
		/// <value>The splash image used to display progress.</value>
		protected Image GreyscaleImage { get; private set; }

		/// <summary>
		/// Gets the colour image that is incrementally displayed to show progress.
		/// </summary>
		/// <value>The colour image that is incrementally displayed to show progress.</value>
		protected Image ColourImage { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_iniApplicationInitializer">The application initializer.</param>
		public GameDetectionForm(GameDetectionVM p_vmlGameDetection)
		{
			InitializeComponent();
			Size szeMax = Screen.GetWorkingArea(this).Size;
			this.MaximumSize = new Size((Int32)(szeMax.Width * 0.9), (Int32)(szeMax.Height * 0.9));
			ViewModel = p_vmlGameDetection;
		}

		#endregion
	}
}
