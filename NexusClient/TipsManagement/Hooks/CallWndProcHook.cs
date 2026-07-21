// CallWndProcHook.cs
// Copyright 2002, Rama Krishna 
//
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Balloon.NET.Hooks
{
	/// <summary>
	/// Summary description for CallWndProcHook.
	/// </summary>
	public class CallWndProcHookEventArgs : EventArgs
	{
		Message message;
		bool sentFromCurrentProcess;

		[StructLayout(LayoutKind.Sequential)]
		internal class CWPSTRUCT
		{
			public IntPtr lparam;
			public IntPtr wparam;
			public int message;
			public IntPtr hwnd;
		};
		
		internal CallWndProcHookEventArgs(IntPtr wparam, IntPtr lparam)
		{
			sentFromCurrentProcess = wparam != IntPtr.Zero;
	
			CWPSTRUCT cwp = (CWPSTRUCT)Marshal.PtrToStructure(lparam, typeof(CWPSTRUCT));
			message = Message.Create(cwp.hwnd, cwp.message, cwp.wparam, cwp.lparam);
		}
	
		public System.Windows.Forms.Message Message
		{
			get
			{
				return message;
			}
		}
	
		public bool SentFromCurrentProcess
		{
			get
			{
				return this.sentFromCurrentProcess;
			}
		}
	}

	public delegate void CallWndProcHookEventHandler(object sender, CallWndProcHookEventArgs e);
	
	/// <summary>
	/// Summary description for CallWndRetProcHook.
	/// </summary>
	class CallWndProcHook : BaseHook
	{
		internal CallWndProcHook()
		{
		}
		
		internal override Hooks.HookType Type
		{
			get
			{
				return HookType.CallWndProc;
			}
		}

		protected override void InvokeHookEvent(int code, System.IntPtr wparam, System.IntPtr lparam)
		{
			CallWndProcHookEventArgs e = new CallWndProcHookEventArgs(wparam, lparam);
			Parent.OnCallWndProcHook(this, e);
		}
	}	
}
