// WindowsHook.cs
// Copyright 2002, Rama Krishna 
//
using System;
using System.ComponentModel;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Balloon.NET.Hooks
{
	internal delegate IntPtr HookProc(int code, IntPtr wparam, IntPtr lparam);
	
	/// <summary>
	/// Summary description for WindowsHook.
	/// </summary>
	public class WindowsHook : System.ComponentModel.Component
	{
		private ArrayList hooks;
		private int threadID;
		
		public WindowsHook(System.ComponentModel.IContainer container)
			: this()
		{
			container.Add(this);
		}

		public WindowsHook()
		{
			//Reserve space for atmost 4 hooks
			hooks = new ArrayList(4);
		}
		
		/// <summary>
		/// The thread which needs to be hooked
		/// </summary>
		public int ThreadID
		{
			get
			{
				return threadID;
			}
			set
			{
				threadID = value;
			
				//Start hooking or change the hooks
				foreach(BaseHook hook in hooks)
				{
					hook.SetHook(this, IntPtr.Zero, value);
				}
			}
		}
		
		[DllImport("kernel32.dll")]
		static extern int GetCurrentThreadId();

		public void HookCurrentThread()
		{
			this.ThreadID = GetCurrentThreadId();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				//dispose individual hooks
				foreach(IDisposable hook in hooks)
				{
					hook.Dispose();
				}
			}
		}

		protected internal virtual void OnMouseHook(object hook, MouseHookEventArgs e)
		{
			MouseHookEventHandler handler = (MouseHookEventHandler)Events[hook];
			handler(this, e);
		}

		protected internal virtual void OnKeyBoardHook(object hook, KeyBoardHookEventArgs e)
		{
			KeyBoardHookEventHandler handler = (KeyBoardHookEventHandler)Events[hook];
			handler(this, e);
		}
		
		protected internal virtual void OnCallWndRetProcHook(object key, CallWndProcRetHookEventArgs e)
		{
			CallWndProcRetHookEventHandler handler = (CallWndProcRetHookEventHandler)Events[key];
			handler(this, e);
		}
	
		protected internal virtual void OnCallWndProcHook(object key, CallWndProcHookEventArgs e)
		{
			CallWndProcHookEventHandler handler = (CallWndProcHookEventHandler)Events[key];
			handler(this, e);
		}

		private BaseHook GetHookObjectForType(HookType type)
		{
			BaseHook ret = null;

			foreach(BaseHook hook in hooks)
			{
				if (hook.Type == type)
				{
					ret = hook;
					break;
				}
			}
			
			return ret;
		}
		
		private void AddHookEventHandler(HookType type, Type classType, Delegate value)
		{
			BaseHook key = GetHookObjectForType(type);

			if (key == null)
			{
				key = (BaseHook)Activator.CreateInstance(classType, true);
				
				if (threadID != 0)
					key.SetHook(this, IntPtr.Zero, threadID);

				hooks.Add(key);
			}
				
			Events.AddHandler(key, value);				
		}

		private void RemoveHookEventHandler(HookType type, Delegate value)
		{
			BaseHook key = GetHookObjectForType(type);
			Events.RemoveHandler(key, value);
				
			if (Events[key] == null)
				key.Dispose();
		
			hooks.Remove(key);	
		}

		public event MouseHookEventHandler MouseHook
		{
			add
			{
				AddHookEventHandler(HookType.Mouse, typeof(MouseHook), value);
			}
			remove
			{
				RemoveHookEventHandler(HookType.Mouse, value);
			}
		}

		public event KeyBoardHookEventHandler KeyBoardHook
		{
			add
			{
				AddHookEventHandler(HookType.Keyboard, typeof(KeyBoardHook), value);
			}
			remove
			{
				RemoveHookEventHandler(HookType.Keyboard, value);
			}
		}

		public event CallWndProcRetHookEventHandler CallWndProcRetHook
		{
			add
			{
				AddHookEventHandler(HookType.CallWndProcRet, typeof(CallWndProcRetHook), value);
			}
			remove
			{
				RemoveHookEventHandler(HookType.CallWndProcRet, value);
			}
		}

		public event CallWndProcHookEventHandler CallWndProcHook
		{
			add
			{
				AddHookEventHandler(HookType.CallWndProc, typeof(CallWndProcHook), value);
			}
			remove
			{
				RemoveHookEventHandler(HookType.CallWndProc, value);
			}
		}
	}
}
