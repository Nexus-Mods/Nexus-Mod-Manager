// MouseHook.cs
// Copyright 2002, Rama Krishna 
//
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;

namespace Balloon.NET.Hooks
{
	public enum MouseMessages
	{
		Unknown		  = 0x0000,
		MouseMove     = 0x0200,
		LButtonDown   = 0x0201,
		LButtonUp     = 0x0202,
		LButtonDblClk = 0x0203,
		RButtonDown   = 0x0204,
		RButtonUp     = 0x0205,
		RButtonDblClk = 0x0206,
		MButtonDown   = 0x0207,
		MButtonUp     = 0x0208,
		MButtonDblClk = 0x0209,
		MouseWheel    = 0x020A,
		XButtonDown   = 0x020B,
		XButtonUP     = 0x020C,
		XButtonDblClk = 0x020D,
	};
	
	public class MouseHookEventArgs
	{
		private MouseMessages message;
		private Point point;
		private IntPtr hwnd;
		private HitTestCodes hitTestCode;
		private IntPtr extraInfo;

		[StructLayout(LayoutKind.Sequential)]
		struct POINT
		{
			public int x;
			public int y;
		}

		[StructLayout(LayoutKind.Sequential)]
		class MOUSEHOOKSTRUCT
		{
			public POINT pt;
			public IntPtr hwnd;
			public int hitTestCode;
			public IntPtr extraInfo;
		}

		internal MouseHookEventArgs(IntPtr wparam, IntPtr lparam)
		{
			if (!Enum.IsDefined(typeof(MouseMessages), wparam.ToInt32()))
				message = MouseMessages.Unknown;
			else
				message = (MouseMessages)wparam.ToInt32();
		
			MOUSEHOOKSTRUCT hs = (MOUSEHOOKSTRUCT)Marshal.PtrToStructure(lparam, typeof(MOUSEHOOKSTRUCT));
			
			point = new Point(hs.pt.x, hs.pt.y);
			hwnd = hs.hwnd;
			hitTestCode = (HitTestCodes)hs.hitTestCode;
			extraInfo = hs.extraInfo;
		}
	
		public MouseMessages Message
		{
			get
			{
				return message;
			}
		}

		public Point Point
		{
			get
			{
				return point;
			}
		}

		public IntPtr Hwnd
		{
			get
			{
				return hwnd;
			}
		}

		public HitTestCodes HitTestCode
		{
			get
			{
				return hitTestCode;
			}
		}

		public IntPtr ExtraInfo
		{
			get
			{
				return extraInfo;
			}
		}
	};
	
	public delegate void MouseHookEventHandler(object sender, MouseHookEventArgs e);
	
	/// <summary>
	/// Summary description for MouseHook.
	/// </summary>
	class MouseHook : BaseHook
	{
		public MouseHook()
		{
		}
	
		protected override void InvokeHookEvent(int code, System.IntPtr wparam, System.IntPtr lparam)
		{
			Parent.OnMouseHook(this, new MouseHookEventArgs(wparam, lparam));
		}

		internal override Hooks.HookType Type
		{
			get
			{
				return HookType.Mouse;
			}
		}
	}
}
