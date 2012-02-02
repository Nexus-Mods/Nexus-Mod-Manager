using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Nexus.Client
{
	public partial class GameModeListViewItem : UserControl
	{
		public GameModeListViewItem()
		{
			InitializeComponent();
			
			PrivateFontCollection pfcFonts = new PrivateFontCollection();
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
				}
			}
			finally
			{
				Marshal.FreeCoTaskMem(pbyt);
			}
			lblNotFoundTitle.Font = new Font(pfcFonts.Families[0], lblNotFoundTitle.Font.Size, lblNotFoundTitle.Font.Style, lblNotFoundTitle.Font.Unit);
		}

		private void butSelectPath_Click(object sender, EventArgs e)
		{
			fbdSelectPath.SelectedPath = tbxInstallPath.Text;
			if (fbdSelectPath.ShowDialog(this) == DialogResult.OK)
				tbxInstallPath.Text = fbdSelectPath.SelectedPath;
		}
	}
}
