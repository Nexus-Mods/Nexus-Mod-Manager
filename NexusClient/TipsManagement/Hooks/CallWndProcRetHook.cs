// CallWndProcRetHook.cs
// Copyright 2002, Rama Krishna 
//
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Balloon.NET.Hooks
{
	public class CallWndProcRetHookEventArgs : EventArgs
	{
		Message message;
		bool sentFromCurrentProcess;

		[StructLayout(LayoutKind.Sequential)]
		internal class CWPRETSTRUCT
		{
			public IntPtr result;
			public IntPtr lparam;
			public IntPtr wparam;
			public int message;
			public IntPtr hwnd;
		};

		internal CallWndProcRetHookEventArgs(IntPtr wparam, IntPtr lparam)
		{
			sentFromCurrentProcess = wparam != IntPtr.Zero;
	
			CWPRETSTRUCT cwpr = (CWPRETSTRUCT)Marshal.PtrToStructure(lparam, typeof(CWPRETSTRUCT));
			message = Message.Create(cwpr.hwnd, cwpr.message, cwpr.wparam, cwpr.lparam);
			message.Result = cwpr.result;
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
	};
	
	public delegate void CallWndProcRetHookEventHandler(object sender, CallWndProcRetHookEventArgs e);
	
	/// <summary>
	/// Summary description for CallWndRetProcHook.
	/// </summary>
	class CallWndProcRetHook : BaseHook
	{
		internal CallWndProcRetHook()
		{
		}
		
		internal override Hooks.HookType Type
		{
			get
			{
				return HookType.CallWndProcRet;
			}
		}

		protected override void InvokeHookEvent(int code, System.IntPtr wparam, System.IntPtr lparam)
		{
			CallWndProcRetHookEventArgs e = new CallWndProcRetHookEventArgs(wparam, lparam);
			Parent.OnCallWndRetProcHook(this, e);
		}
	}
}
