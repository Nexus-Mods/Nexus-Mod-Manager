using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Permissions;
using System.Threading;
using System.Windows.Forms;
using Nexus.UI.Controls;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.Scripting.UI;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// This class displays UI elements on another thread.
	/// </summary>
	/// <remarks>
	/// This class is useful for marshalling UI interaction to the UI thread.
	/// </remarks>
	public class UIUtil
	{
		#region Properties

		/// <summary>
		/// Gets the <see cref="SynchronizationContext"/> to use to marshall
		/// UI calls to the UI thread.
		/// </summary>
		/// <value>The <see cref="SynchronizationContext"/> to use to marshall
		/// UI calls to the UI thread.</value>
		protected SynchronizationContext SyncContext { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_scxSyncContext">The synchronization context to use to marshall calls to the UI thread.</param>
		public UIUtil(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, SynchronizationContext p_scxSyncContext)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			SyncContext = p_scxSyncContext;
		}

		#endregion

		/// <summary>Associates a script's own dialog with NMM without moving its sandboxed controls onto NMM's UI thread.</summary>
		public void AttachScriptWindow(IntPtr window)
		{
			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();
				IntPtr owner = IntPtr.Zero;
				SyncContext.Send(ignored =>
				{
					Form form = Form.ActiveForm;
					if (form == null)
						foreach (Form candidate in Application.OpenForms)
							if (candidate.Visible) { form = candidate; break; }
					if (form != null) owner = form.Handle;
				}, null);
				if (owner == IntPtr.Zero || window == IntPtr.Zero || owner == window)
					return;

				// Change ownership on the script thread after the UI callback returns, avoiding a cross-thread window-message deadlock.
				if (IntPtr.Size == 8) SetWindowLongPtr(window, -8, owner);
				else SetWindowLong(window, -8, owner.ToInt32());
				SetForegroundWindow(window);
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Could not associate the scripted installer window with NMM: {0}", ex.Message);
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
		}

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
		private static extern int SetWindowLong(IntPtr window, int index, int value);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetForegroundWindow(IntPtr window);

		#region Message Box

		/// <summary>
		/// Displays an extended message box.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The caption of the message box.</param>
		/// <param name="p_strDetails">The details to display.</param>
		/// <param name="p_mbbButtons">The buttons to show on the message box.</param>
		/// <param name="p_mbiIcon">The icon to show on the message box.</param>
		/// <returns>The <see cref="DialogResult"/> corressponding to the button pushed on the message box.</returns>
		public DialogResult ShowExtendedMessageBox(string p_strMessage, string p_strCaption, string p_strDetails, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon)
		{
			DialogResult drsResult = DialogResult.None;
			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();
				SyncContext.Send(x => drsResult = ExtendedMessageBox.Show(null, p_strMessage, p_strCaption, p_strDetails, p_mbbButtons, p_mbiIcon), null);
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
			return drsResult;
		}

		/// <summary>
		/// Displays a message box.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The caption of the message box.</param>
		/// <param name="p_mbbButtons">The buttons to show on the message box.</param>
		/// <param name="p_mbiIcon">The icon to show on the message box.</param>
		/// <returns>The <see cref="DialogResult"/> corressponding to the button pushed on the message box.</returns>
		public DialogResult ShowMessageBox(string p_strMessage, string p_strCaption, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mbiIcon)
		{
			DialogResult drsResult = DialogResult.None;
			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();
				SyncContext.Send(x => drsResult = MessageBox.Show(p_strMessage, p_strCaption, p_mbbButtons, p_mbiIcon, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly), null);
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
			return drsResult;
		}

		/// <summary>
		/// Displays a message box.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <returns>The <see cref="DialogResult"/> corressponding to the button pushed on the message box.</returns>
		public DialogResult ShowMessageBox(string p_strMessage)
		{
			return ShowMessageBox(p_strMessage, string.Empty, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		/// <summary>
		/// Displays a message box.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The caption of the message box.</param>
		/// <returns>The <see cref="DialogResult"/> corressponding to the button pushed on the message box.</returns>
		public DialogResult ShowMessageBox(string p_strMessage, string p_strCaption)
		{
			return ShowMessageBox(p_strMessage, p_strCaption, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		/// <summary>
		/// Displays a message box.
		/// </summary>
		/// <param name="p_strMessage">The message to display.</param>
		/// <param name="p_strCaption">The caption of the message box.</param>
		/// <param name="p_mbbButtons">The buttons to show on the message box.</param>
		/// <returns>The <see cref="DialogResult"/> corressponding to the button pushed on the message box.</returns>
		public DialogResult ShowMessageBox(string p_strMessage, string p_strCaption, MessageBoxButtons p_mbbButtons)
		{
			return ShowMessageBox(p_strMessage, p_strCaption, p_mbbButtons, MessageBoxIcon.Information);
		}

		#endregion

		#region Select Box

		/// <summary>
		/// Displays a selection form to the user.
		/// </summary>
		/// <param name="p_lstOptions">The options from which to select.</param>
		/// <param name="p_strTitle">The title of the selection form.</param>
		/// <param name="p_booSelectMany">Whether more than one items can be selected.</param>
		/// <returns>The selected option names.</returns>
		public string[] Select(IList<SelectOption> p_lstOptions, string p_strTitle, bool p_booSelectMany)
		{
			string[] strResult = null;
			SyncContext.Send(x => strResult = ShowSelect(p_lstOptions, p_strTitle, p_booSelectMany), null);
			return strResult;
		}

		/// <summary>
		/// Displays a selection form to the user.
		/// </summary>
		/// <remarks>
		/// This method is called by the <see cref="SynchronizationContext"/> in order to create and execute the
		/// form on the appropriate thread.
		/// </remarks>
		/// <param name="p_lstOptions">The options from which to select.</param>
		/// <param name="p_strTitle">The title of the selection form.</param>
		/// <param name="p_booSelectMany">Whether more than one items can be selected.</param>
		/// <returns>The selected option names.</returns>
		private string[] ShowSelect(IList<SelectOption> p_lstOptions, string p_strTitle, bool p_booSelectMany)
		{
			SelectForm sfmSelectForm = new SelectForm(p_lstOptions, p_strTitle, p_booSelectMany);
			sfmSelectForm.ShowDialog();
			return sfmSelectForm.SelectedOptionNames;
		}

		#endregion
	}
}
