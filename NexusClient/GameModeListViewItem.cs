using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Util;

namespace Nexus.Client
{
	/// <summary>
	/// A list view item that displays a game mode whose install path is being searched for.
	/// </summary>
	public partial class GameModeSearchListViewItem : UserControl
	{
		[DllImport("gdi32.dll")]
		private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

		private PrivateFontCollection pfcFonts = new PrivateFontCollection();

		#region Properties

		/// <summary>
		/// Gets the descriptor for the game modewhose install path is to be found.
		/// </summary>
		/// <value>The descriptor for the game modewhose install path is to be found.</value>
		protected IGameModeDescriptor GameMode { get; private set; }

		/// <summary>
		/// Gets the discoverer to use to find the game installation path.
		/// </summary>
		/// <value>The discoverer to use to find the game installation path.</value>
		protected GameDiscoverer Discoverer { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameModeInfo">The descriptor for the game modewhose install path is to be found.</param>
		/// <param name="p_gdtDetector">The discoverer to use to find the game installation path.</param>
		public GameModeSearchListViewItem(IGameModeDescriptor p_gmdGameModeInfo, GameDiscoverer p_gdtDetector)
		{
			GameMode = p_gmdGameModeInfo;
			Discoverer = p_gdtDetector;
			InitializeComponent();
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			SetVisiblePanel(pnlSearching);
			p_gdtDetector.PropertyChanged += new PropertyChangedEventHandler(Detector_PropertyChanged);
			p_gdtDetector.PathFound += new EventHandler<GameModeDiscoveredEventArgs>(Detector_PathFound);
			p_gdtDetector.TaskEnded += new EventHandler<TaskEndedEventArgs>(Detector_TaskEnded);

			IntPtr pbyt = IntPtr.Zero;
			try
			{
				byte[][] rgbyt = new byte[2][];
				rgbyt[0] = Properties.Resources.LinBiolinum_RI;
				rgbyt[1] = Properties.Resources.LinBiolinum_RB;
				for (Int32 i = 0; i <= rgbyt.GetUpperBound(0); i++)
				{
					pbyt = Marshal.AllocCoTaskMem(rgbyt[i].Length);
					Marshal.Copy(rgbyt[i], 0, pbyt, rgbyt[i].Length);
					pfcFonts.AddMemoryFont(pbyt, rgbyt[i].Length);
					uint dummy = 0;
					AddFontMemResourceEx(pbyt, (uint)rgbyt[i].Length, IntPtr.Zero, ref dummy);
				}
			}
			finally
			{
				Marshal.FreeCoTaskMem(pbyt);
			}
			//these lines are an alternate method to load the font, but require
			// the fonts to be files, instead of embedded resources
			//pfcFonts.AddFontFile(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), @"data\fonts\LinBiolinum_RI.ttf"));
			//pfcFonts.AddFontFile(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), @"data\fonts\LinBiolinum_RB.ttf"));


			lblNotFoundTitle.Font = new Font(pfcFonts.Families[0], lblNotFoundTitle.Font.Size, lblNotFoundTitle.Font.Style, lblNotFoundTitle.Font.Unit);
			lblFoundTitle.Font = new Font(pfcFonts.Families[0], lblFoundTitle.Font.Size, lblFoundTitle.Font.Style, lblFoundTitle.Font.Unit);

			lblGameModeName.Text = GameMode.Name;
			lblGameModeName.ForeColor = GameMode.ModeTheme.PrimaryColour;

			pbxGameLogo.Image = new Icon(GameMode.ModeTheme.Icon, 96, 96).ToBitmap();
		}

		#endregion

		#region Game Discoverer Event Handling

		/// <summary>
		/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of the game discoverer.
		/// </summary>
		/// <remarks>
		/// If the game being searched for has not been found, this displays the not found UI.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		private void Detector_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, TaskEndedEventArgs>)Detector_TaskEnded, sender, e);
				return;
			}
			if (!Discoverer.IsFound(GameMode.ModeId) && !Discoverer.HasCandidates(GameMode.ModeId))
				SetVisiblePanel(pnlNotFound);
		}

		/// <summary>
		/// Handles the <see cref="GameDiscoverer.PathFound"/> event of the game discoverer.
		/// </summary>
		/// <remarks>
		/// This displays the path that was found.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="GameModeDiscoveredEventArgs"/> describing the event arguments.</param>
		private void Detector_PathFound(object sender, GameModeDiscoveredEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, GameModeDiscoveredEventArgs>)Detector_PathFound, sender, e);
				return;
			}
			if (e.GameMode.ModeId.Equals(GameMode.ModeId))
			{
				lblPath.Text = e.InstallationPath;
				SetVisiblePanel(pnlCandidate);
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the game discoverer.
		/// </summary>
		/// <remarks>
		/// This updates the search progress display.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Detector_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (lblProgressMessage.InvokeRequired)
				lblProgressMessage.Invoke((Action<object, PropertyChangedEventArgs>)Detector_PropertyChanged, sender, e);
			else if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.OverallMessage)))
				lblProgressMessage.Text = Discoverer.OverallMessage;
		}

		#endregion

		#region Path Candidate Management

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the accept button.
		/// </summary>
		/// <remarks>
		/// This marks the found path as being accepted as the installation path for the game mode.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butAccept_Click(object sender, EventArgs e)
		{
			Discoverer.Accept(GameMode.ModeId);
			DisplayFinalUI();
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the reject button.
		/// </summary>
		/// <remarks>
		/// This indicate thes found path is incorrect, and another path should be found.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butReject_Click(object sender, EventArgs e)
		{
			if (Discoverer.Status == TaskStatus.Complete)
				SetVisiblePanel(pnlNotFound);
			else
				SetVisiblePanel(pnlSearching);
			Discoverer.Reject(GameMode.ModeId);
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the override button.
		/// </summary>
		/// <remarks>
		/// This marks the entered path as the path to use as the installation path for the game mode,
		/// override any found paths.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butOverride_Click(object sender, EventArgs e)
		{
			if (Discoverer.Verify(GameMode.ModeId, tbxInstallPath.Text) ||
				MessageBox.Show(this, "The selected path does not contain the game's EXE file. Are you sure you want to use the selected path?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
			{
				Discoverer.Override(GameMode.ModeId, tbxInstallPath.Text);
				DisplayFinalUI();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select path button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog, so an override path can be selected.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectPath_Click(object sender, EventArgs e)
		{
			fbdSelectPath.SelectedPath = tbxInstallPath.Text;
			if (fbdSelectPath.ShowDialog(this) == DialogResult.OK)
				tbxInstallPath.Text = fbdSelectPath.SelectedPath;
		}

		/// <summary>
		/// Changes the UI to indicate that an installation path for the game mode has been selected.
		/// </summary>
		protected void DisplayFinalUI()
		{
			SetVisiblePanel(pnlSet);
			lblFinalPath.Text = Discoverer.GetFinalPath(GameMode.ModeId);
		}

		#endregion

		/// <summary>
		/// Sets the panel that should be displayed in the item.
		/// </summary>
		/// <param name="p_pnlPanel">The panel to make visible.</param>
		private void SetVisiblePanel(Panel p_pnlPanel)
		{
			pnlCandidate.Visible = (p_pnlPanel == pnlCandidate);
			pnlNotFound.Visible = (p_pnlPanel == pnlNotFound);
			pnlSearching.Visible = (p_pnlPanel == pnlSearching);
			pnlSet.Visible = (p_pnlPanel == pnlSet);
		}

		/// <summary>
		/// Handles the <see cref="Control.TextChanged"/> event of the install path text box.
		/// </summary>
		/// <remarks>
		/// This verifies that the entered path contains the game being looked for, dislpaying
		/// an error if required.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tbxInstallPath_TextChanged(object sender, EventArgs e)
		{
			if (!Discoverer.Verify(GameMode.ModeId, tbxInstallPath.Text))
				erpErrors.SetError(butSelectPath, "Path does not contain game EXE.");
			else
				erpErrors.SetError(butSelectPath, null);
		}


	}
}
