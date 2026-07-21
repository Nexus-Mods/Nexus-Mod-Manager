using System;
using System.Runtime.InteropServices;

namespace Balloon.NET.Hooks
{
	internal enum HookType
	{
		//MsgFilter		= -1,
		//JournalRecord    = 0,
		//JournalPlayback  = 1,
		Keyboard         = 2,
		//GetMessage       = 3,
		CallWndProc      = 4,
		//CBT              = 5,
		//SysMsgFilter     = 6,
		Mouse            = 7,
		//Hardware         = 8,
		//Debug            = 9,
		//Shell           = 10,
		//ForegroundIdle  = 11,
		CallWndProcRet  = 12,
		//KeyboardLL		= 13,
		//MouseLL			= 14,
	};

	/// <summary>
	/// Summary description for HookHandle.
	/// </summary>
	abstract class BaseHook : IDisposable
	{
		IntPtr handle;
		WindowsHook parent;
		GCHandle delegateHandle;

		internal BaseHook()
		{
		}
		
		~BaseHook()
		{
			Dispose(false);
		}

		[DllImport("User32.dll")]
		internal extern static void UnhookWindowsHookEx(IntPtr handle);
		
		[DllImport("User32.dll")]
		internal extern static IntPtr SetWindowsHookEx(int idHook, [MarshalAs(UnmanagedType.FunctionPtr)] HookProc lpfn, IntPtr hinstance, int threadID);
		
		[DllImport("User32.dll")]
		internal extern static IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr wparam, IntPtr lparam);

		IntPtr HookProc(int code, IntPtr wparam, IntPtr lparam)
		{
			if (code >= 0)
			{
				try
				{
					InvokeHookEvent(code, wparam, lparam);
				}
				catch(Exception e)
				{
					System.Diagnostics.Trace.WriteLine(String.Format("Unhandled Exception {0}", e));
				}
			}

			return CallNextHookEx(handle, code, wparam, lparam);
		}

		internal abstract HookType Type
		{
			get;
		}
		
		protected abstract void InvokeHookEvent(int code, IntPtr wparam, IntPtr lparam);

		internal void SetHook(WindowsHook parent, IntPtr hinstance, int threadID)
		{
			if (handle != IntPtr.Zero)
				Dispose(false);
			
			this.parent = parent;
			
			HookProc proc = new HookProc(this.HookProc);

			handle = SetWindowsHookEx((int)Type, proc, hinstance, threadID);
			
			if (handle == IntPtr.Zero)
			{
				GC.SuppressFinalize(this);
				Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
			}
			
			this.delegateHandle = GCHandle.Alloc(proc);
		}
		
		private void Dispose(bool disposing)
		{
			if (handle != IntPtr.Zero)
			{
				UnhookWindowsHookEx(handle);
			}
			
			if (delegateHandle.IsAllocated)
				delegateHandle.Free();

			if (disposing)
				GC.SuppressFinalize(this);
		}

		public void Dispose()
		{
			Dispose(true);			
		}

		protected WindowsHook Parent
		{
			get
			{
				return this.parent;
			}
		}
	}
}
