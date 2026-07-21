using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace Balloon.NET
{
	/// <summary>
	/// Summary description for BalloonWindow.
	/// </summary>
	public class BalloonVM : System.Windows.Forms.Form
	{
		#region Fields
		private Point anchorPoint;
		private GraphicsPath path;
		#endregion

		#region Constants
		public static readonly int TIPMARGIN = SystemInformation.FrameBorderSize.Height;
		public static readonly int TIPTAIL = SystemInformation.ToolWindowCaptionHeight;
		const int WM_NCCALCSIZE = 0x0083;
		const int WM_NCPAINT = 0x0085;
		const int WM_NCHITTEST = 0x0084;
		#endregion
		
		public enum BallonQuadrant
		{
			TopLeft,
			TopRight,
			BottomLeft,
			BottomRight
		}

		public BalloonVM()
		{
			this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
			this.SetStyle(ControlStyles.DoubleBuffer, true);
			this.SetStyle(ControlStyles.ResizeRedraw, true);
			this.TopMost = true;
			this.ShowInTaskbar = false;
			this.ForeColor = System.Drawing.SystemColors.InfoText;
			this.BackColor = System.Drawing.SystemColors.Info;
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Fixed3D;
		}

		#region Public Properties
		public Point AnchorPoint
		{
			get
			{
				return anchorPoint;
			}
			set
			{
				if (!this.DesignMode)
					RepositionWindow(anchorPoint, value);
				
				anchorPoint = value;
			}
		}

		public BallonQuadrant Quadrant
		{
			get
			{
				Rectangle screenBounds = Screen.FromPoint(anchorPoint).Bounds;

				if(anchorPoint.Y < screenBounds.Top + screenBounds.Height/2)
				{
					if(anchorPoint.X < screenBounds.Left + screenBounds.Width/2)
						return BallonQuadrant.TopLeft;
					else
						return BallonQuadrant.TopRight;
				}
				else
				{
					if(anchorPoint.X < screenBounds.Left + screenBounds.Width/2)
						return BallonQuadrant.BottomLeft;
				}

				return BallonQuadrant.BottomRight;
			}
		}
		#endregion

		#region Layout Related
		protected void RecalcLayout()
		{
			if (!this.IsHandleCreated)
				return;

			System.Drawing.Size windowSize = this.Size;
			
			Point[] tailPoints = new Point[3];
			Point topLeftPoint = Point.Empty;
			Point bottomRightPoint = (Point)windowSize;

			switch(this.Quadrant)
			{
				case BallonQuadrant.TopLeft:
					topLeftPoint.Y = TIPTAIL;
					tailPoints[2].X = (windowSize.Width-TIPTAIL)/4 + TIPTAIL;
					tailPoints[2].Y = TIPTAIL;
					tailPoints[0].X = (windowSize.Width-TIPTAIL)/4 + 1;
					tailPoints[0].Y = tailPoints[2].Y;
					tailPoints[1].X = tailPoints[0].X;
					tailPoints[1].Y = 1;
					break;
				case BallonQuadrant.TopRight:
					topLeftPoint.Y = TIPTAIL;
					tailPoints[0].X = (windowSize.Width-TIPTAIL)/4*3;
					tailPoints[0].Y = TIPTAIL;
					tailPoints[2].X = (windowSize.Width-TIPTAIL)/4*3 + TIPTAIL-1;
					tailPoints[2].Y = tailPoints[0].Y;
					tailPoints[1].X = tailPoints[2].X;
					tailPoints[1].Y = 1;
					break;
				case BallonQuadrant.BottomLeft:
					bottomRightPoint.Y = windowSize.Height-TIPTAIL;
					tailPoints[0].X = (windowSize.Width-TIPTAIL)/4 + TIPTAIL - 1;
					tailPoints[0].Y = windowSize.Height-TIPTAIL;
					tailPoints[2].X = (windowSize.Width-TIPTAIL)/4;
					tailPoints[2].Y = tailPoints[0].Y;
					tailPoints[1].X = tailPoints[2].X;
					tailPoints[1].Y = windowSize.Height-1;
					break;
				case BallonQuadrant.BottomRight:
					bottomRightPoint.Y = windowSize.Height-TIPTAIL;
					tailPoints[2].X = (windowSize.Width-TIPTAIL)/4*3;
					tailPoints[2].Y = windowSize.Height-TIPTAIL;
					tailPoints[0].X = (windowSize.Width-TIPTAIL)/4*3 + TIPTAIL - 1;
					tailPoints[0].Y = tailPoints[2].Y;
					tailPoints[1].X = tailPoints[0].X;
					tailPoints[1].Y = windowSize.Height-1;
					break;
			}

			//
			// adjust for very narrow balloons
			//
			if(tailPoints[0].X < TIPMARGIN )
				tailPoints[0].X = TIPMARGIN;

			if(tailPoints[0].X > windowSize.Width - TIPMARGIN)
				tailPoints[0].X = windowSize.Width - TIPMARGIN;

			if(tailPoints[1].X < TIPMARGIN)
				tailPoints[1].X = TIPMARGIN;

			if(tailPoints[1].X > windowSize.Width - TIPMARGIN)
				tailPoints[1].X = windowSize.Width - TIPMARGIN;

			if(tailPoints[2].X < TIPMARGIN)
				tailPoints[2].X = TIPMARGIN;

			if(tailPoints[2].X > windowSize.Width - TIPMARGIN)
				tailPoints[2].X = windowSize.Width - TIPMARGIN;
			
			if (!this.DesignMode)
			{
				// get window position
				Point screenLocation = new Point(anchorPoint.X - tailPoints[1].X, anchorPoint.Y - tailPoints[1].Y);

				// adjust position so all is visible
				Rectangle workArea = Screen.FromPoint(anchorPoint).WorkingArea;

				int adjustX=0;
				int adjustY=0;

				if ( screenLocation.X < workArea.X)
					adjustX = workArea.Left - screenLocation.X;
				else if ( screenLocation.X + windowSize.Width >= workArea.Right )
					adjustX = workArea.Right - (screenLocation.X + windowSize.Width);
				if ( screenLocation.Y + TIPTAIL < workArea.Top )
					adjustY = workArea.Top - (screenLocation.Y + TIPTAIL);
				else if ( screenLocation.Y + windowSize.Height - TIPTAIL >= workArea.Bottom )
					adjustY = workArea.Bottom - (screenLocation.Y + windowSize.Height - TIPTAIL);

				//tailPoints[0].X -= adjustX;
				tailPoints[1].X -= adjustX;
				//tailPoints[2].X -= adjustX;
				screenLocation.X += adjustX;
				screenLocation.Y += adjustY;

				// place window
				this.SetBounds(screenLocation.X, screenLocation.Y, windowSize.Width, windowSize.Height);
			}
			
			if (path != null)
				path.Dispose();

			path = new GraphicsPath(FillMode.Alternate);
			
			int arcRadius = TIPMARGIN*3;
			int arcDia = arcRadius*2;
			int rectX1 = topLeftPoint.X + arcRadius;
			int rectX2 = bottomRightPoint.X - arcRadius;
			int rectY1 = topLeftPoint.Y + arcRadius;
			int rectY2 = bottomRightPoint.Y - arcRadius;
			
			path.StartFigure();

			// apply region
			if ((this.Quadrant == BallonQuadrant.TopLeft) || 
				(this.Quadrant == BallonQuadrant.TopRight))
			{
				path.AddArc(topLeftPoint.X, rectY2 - arcRadius, arcDia, arcDia, 90, 90);
				path.AddLine(topLeftPoint.X, rectY2, topLeftPoint.X, rectY1);
				path.AddArc(topLeftPoint.X, topLeftPoint.Y, arcDia, arcDia, 180, 90);
				path.AddLine(rectX1, topLeftPoint.Y, tailPoints[0].X, topLeftPoint.Y);
				path.AddLines(tailPoints);
				path.AddLine(tailPoints[2].X, topLeftPoint.Y, rectX2, topLeftPoint.Y);
				path.AddArc(rectX2 - arcRadius, topLeftPoint.Y, arcDia, arcDia, 270, 90);
				path.AddLine(bottomRightPoint.X, rectY1, bottomRightPoint.X, rectY2);
				path.AddArc(rectX2 - arcRadius, rectY2 - arcRadius, arcDia, arcDia, 0, 90);
				path.AddLine(rectX2, bottomRightPoint.Y, rectX1, bottomRightPoint.Y);
			}
			else
			{
				path.AddLine(rectX1, topLeftPoint.Y, rectX2, topLeftPoint.Y);
				path.AddArc(rectX2 - arcRadius, topLeftPoint.Y, arcDia, arcDia, 270, 90);
				path.AddLine(bottomRightPoint.X, rectY1, bottomRightPoint.X, rectY2);
				path.AddArc(rectX2 - arcRadius, rectY2 - arcRadius, arcDia, arcDia, 0, 90);
				path.AddLine(rectX2, bottomRightPoint.Y, tailPoints[0].X, bottomRightPoint.Y);
				path.AddLines(tailPoints);
				path.AddLine(tailPoints[2].X, bottomRightPoint.Y, rectX1, bottomRightPoint.Y);
				path.AddArc(topLeftPoint.X, rectY2 - arcRadius, arcDia, arcDia, 90, 90);
				path.AddLine(topLeftPoint.X, rectY2, topLeftPoint.X, rectY1);
				path.AddArc(topLeftPoint.X, topLeftPoint.Y, arcDia, arcDia, 180, 90);
			}

			path.CloseFigure();

			this.Region = new Region(this.path);
		}

		protected void RepositionWindow(Point oldAnchorPoint, Point newAnchorPoint)
		{
			if (!this.IsHandleCreated)
				return;

			this.Location = this.Location + (Size)(newAnchorPoint - (Size)oldAnchorPoint);
		}
		#endregion

		#region Interop Functions
		[DllImport("User32.dll")]
		static extern IntPtr GetWindowDC(IntPtr hwnd);
		
		[StructLayout(LayoutKind.Sequential)]
			struct RECT
		{
			int left;
			int top;
			int right;
			int bottom;
			
			Rectangle ToRectangle()
			{
				return new Rectangle(left, top, right - left, bottom - top);
			}
			
			void FromRectangle(Rectangle rect)
			{
				left = rect.Left;
				right = rect.Right;
				top = rect.Top;
				bottom = rect.Bottom;
			}

			public static implicit operator Rectangle(RECT rc)
			{
				return rc.ToRectangle();
			}
			
			public static implicit operator RECT(Rectangle rect)
			{
				RECT rc = new RECT();
				rc.FromRectangle(rect);

				return rc;
			}
		}

		#endregion
		
		#region NC Messages

		protected virtual Rectangle OnNCCalcSize(Rectangle windowRect)
		{
			windowRect.Inflate(-TIPMARGIN, -TIPMARGIN);

			switch(this.Quadrant)
			{
				case BallonQuadrant.TopLeft:
				case BallonQuadrant.TopRight:
					windowRect.Y += TIPTAIL;
					windowRect.Height -= TIPTAIL;
					break;
				case BallonQuadrant.BottomLeft:
				case BallonQuadrant.BottomRight:
					windowRect.Height -= TIPTAIL;
					break;
			}

			return windowRect;
		}
		
		protected virtual void OnNCPaint(Graphics g)
		{
			Rectangle windowRect = new Rectangle(new Point(0, 0), this.Size);
			Rectangle clientRect = OnNCCalcSize(windowRect);
			
			clientRect.Offset(-windowRect.Left, -windowRect.Top);
			g.ExcludeClip(clientRect);
			windowRect.Offset(-windowRect.Left, -windowRect.Top);
			
			Brush b = new SolidBrush(this.BackColor);
			g.FillRectangle(b, windowRect);
			b.Dispose();
			
			Pen p = new Pen(this.ForeColor, 2);
			g.DrawPath(p, path);
			p.Dispose();
		}

		#endregion	
		
		#region Window messages and crackers
		private void WmNCPaint(ref System.Windows.Forms.Message m)
		{
			using(Graphics g = Graphics.FromHdc(GetWindowDC(this.Handle)))
			{
				OnNCPaint(g);
			}
		}
		
		private void WmNCHitTest(ref System.Windows.Forms.Message m)
		{
			base.WndProc(ref m);
			
			if (m.Result != (IntPtr)(int)HitTestCodes.Client)
				m.Result = (IntPtr)HitTestCodes.Nowhere;
		}

		private void WmNCCalcSize(ref System.Windows.Forms.Message m)
		{
			m.Result = IntPtr.Zero;

			RECT rc = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
			rc = (RECT)OnNCCalcSize(rc);
			Marshal.StructureToPtr(rc, m.LParam, false);
		}

		protected override void WndProc(ref System.Windows.Forms.Message m)
		{
			switch(m.Msg)
			{
				case WM_NCCALCSIZE:
					WmNCCalcSize(ref m);
					break;
				case WM_NCPAINT:
					WmNCPaint(ref m);
					break;
				case WM_NCHITTEST:
					WmNCHitTest(ref m);
					break;
				default:
					base.WndProc(ref m);
					break;
			}
		}
		#endregion

		#region Protected Overrides
		protected override void OnLoad(System.EventArgs e)
		{
			this.RecalcLayout();
			base.OnLoad(e);
		}

		protected override void OnResize(System.EventArgs e)
		{
			this.RecalcLayout();
			this.RepositionWindow(anchorPoint, anchorPoint);
			base.OnResize(e);
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if (path != null)
					path.Dispose();
			}
			base.Dispose( disposing );
		}
		#endregion
		
		#region Public Methods
		public static Point AnchorPointFromControl(int p_X, int p_Y)
		{
			if ((p_X == 0) || (p_Y == 0))
				throw new ArgumentException();

			Point controlLocation = new Point(p_X+30, p_Y+60);
			return controlLocation;
		}

		public void ShowBalloon(int p_X, int p_Y)
		{
			this.AnchorPoint = AnchorPointFromControl(p_X, p_Y);
			this.Show();
		}

		#endregion
	}
}
