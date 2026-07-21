using System;
using System.Windows.Forms;
using System.ComponentModel;

namespace Nexus.Client.Commands.Generic
{
	/// <summary>
	/// A class that binds a command to the <see cref="System.Windows.Forms.Control.KeyDown"/> event of a <see cref="Control"/>.
	/// </summary>
	/// <typeparam name="T">The type of the command argument.</typeparam>
	public class KeyDownCommandBinding<T> : CommandBinding<T>
	{
		private Keys[] m_keyKeys = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="Control"/> that is bound to the command.
		/// </summary>
		/// <value>The <see cref="Control"/> that is bound to the command.</value>
		public Control Control
		{
			get
			{
				return (Control)Trigger;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that intializes the object with the given values.
		/// </summary>
		/// <param name="p_ctlControl">The control to bind to the command.</param>
		/// <param name="p_cmdCommand">The command to bind to the trigger.</param>
		/// <param name="p_dlgGetArgument">The method that returns the command argument.</param>
		/// <param name="p_keyKeys">The keys which can trigger the command.</param>
		public KeyDownCommandBinding(Control p_ctlControl, Command<T> p_cmdCommand, GetCommandArgument p_dlgGetArgument, params Keys[] p_keyKeys)
			: base(p_ctlControl, p_cmdCommand, p_dlgGetArgument)
		{
			m_keyKeys = p_keyKeys;
			p_ctlControl.KeyDown += new KeyEventHandler(Control_KeyDown);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="System.Windows.Forms.Control.KeyDown"/> event of the trigger.
		/// </summary>
		/// <remarks>
		/// This executes the command if one of the specified keys has been pressed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		void Control_KeyDown(object sender, KeyEventArgs e)
		{
			if (Command.CanExecute && (Array.FindIndex(m_keyKeys, (k) => { return k == e.KeyCode; }) > -1))
				Execute();
		}

		/// <summary>
		/// Disposes of the binding.
		/// </summary>
		/// <remarks>
		/// After this method is called, the binding between the trigger
		/// and command should no longer exist. In other words, activating the trigger should no
		/// longer execute the command.
		/// </remarks>
		public override void Unbind()
		{
			Control.KeyDown -= Control_KeyDown;
		}
	}
}
