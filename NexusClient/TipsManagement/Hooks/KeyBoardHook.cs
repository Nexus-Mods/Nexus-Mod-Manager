using System;
using System.Windows.Forms;

namespace Balloon.NET.Hooks
{
	public class KeyBoardHookEventArgs : EventArgs
	{
		const int KF_EXTENDED       = 0x0100;
		const int KF_DLGMODE        = 0x0800;
		const int KF_MENUMODE       = 0x1000;
		const int KF_ALTDOWN        = 0x2000;
		const int KF_REPEAT         = 0x4000;
		const int KF_UP             = 0x8000;
		
		private Keys virtKey;
		private int keyFlags;

		internal KeyBoardHookEventArgs(IntPtr wParam, IntPtr lParam)
		{
			int virtKeyInt = (int)wParam;

			if (!Enum.IsDefined(typeof(Keys), virtKeyInt))
				virtKey = Keys.None;
			else
				virtKey = (Keys)virtKeyInt;
		
			keyFlags = (int)lParam;
		}
	
		public Keys VirtKey
		{
			get
			{
				return virtKey;
			}
		}

		public short Repeat
		{
			get
			{
				return (short)(keyFlags & KF_EXTENDED);
			}
		}

		public bool AltDown
		{
			get
			{
				return (keyFlags & KF_ALTDOWN) != 0;
			}
		}
	
		public bool IsDialogActive
		{
			get
			{
				return (keyFlags & KF_DLGMODE) != 0;
			}
		}
	
		public bool IsMenuActive
		{
			get
			{
				return (keyFlags & KF_MENUMODE) != 0;
			}
		}
	
		public bool IsKeyUp
		{
			get
			{
				return (keyFlags & KF_UP) != 0;
			}
		}
	};
	
	public delegate void KeyBoardHookEventHandler(object sender, KeyBoardHookEventArgs e);

	/// <summary>
	/// Summary description for KeyBoardHook.
	/// </summary>
	class KeyBoardHook : BaseHook
	{
		public KeyBoardHook()
		{
		}

		protected override void InvokeHookEvent(int code, System.IntPtr wparam, System.IntPtr lparam)
		{
			Parent.OnKeyBoardHook(this, new KeyBoardHookEventArgs(wparam, lparam));
		}

		internal override Hooks.HookType Type
		{
			get
			{
				return HookType.Keyboard;
			}
		}
	}
}
