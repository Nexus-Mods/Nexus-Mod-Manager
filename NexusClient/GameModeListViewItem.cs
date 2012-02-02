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
			byte[] rgbyt = Properties.Resources.LinBiolinum_RB;
			IntPtr pbyt = IntPtr.Zero;
			try
			{
				pbyt = Marshal.AllocCoTaskMem(rgbyt.Length);
				pfcFonts.AddMemoryFont(pbyt, rgbyt.Length);
			}
			finally
			{
				Marshal.FreeCoTaskMem(pbyt);
			}
			lblNotFoundTitle.Font = new Font(pfcFonts.Families[0], 48, GraphicsUnit.Point);
			lblNotFoundTitle.Font = new Font(FontFamily.GenericMonospace, 12, GraphicsUnit.Point);
		}
	}
}
