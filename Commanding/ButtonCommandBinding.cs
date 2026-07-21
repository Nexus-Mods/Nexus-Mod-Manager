using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Commands.Generic;

namespace Nexus.Client.Commands
{
	/// <summary>
	/// A class that binds a command to a <see cref="Button"/>.
	/// </summary>
	/// <typeparam name="T">The type of the command argument.</typeparam>
	public class ButtonCommandBinding<T> : CommandBinding<T>
	{
		#region Properties

		/// <summary>
		/// Gets the <see cref="Button"/> that is bound to the command.
		/// </summary>
		/// <value>The <see cref="Button"/> that is bound to the command.</value>
		public Button Button
		{
			get
			{
				return (Button)Trigger;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that intializes the object with the given values.
		/// </summary>
		/// <param name="p_butButton">The button to bind to the command.</param>
		/// <param name="p_cmdCommand">The command to bind to the trigger.</param>
		/// <param name="p_dlgGetArgument">The method that returns the command argument.</param>
		public ButtonCommandBinding(Button p_butButton, Command<T> p_cmdCommand, GetCommandArgument p_dlgGetArgument)
			: base(p_butButton, p_cmdCommand, p_dlgGetArgument)
		{
			p_butButton.Text = p_cmdCommand.Name;
			p_butButton.Enabled = Command.CanExecute;
			p_butButton.Click += new EventHandler(Button_Click);
		}

		#endregion

		/// <summary>
		/// Alters properties on the Trigger in response to property changes on the command.
		/// </summary>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the changed property.</param>
		protected override void OnCommandPropertyChanged(PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case "CanExecute":
					Button.Enabled = Command.CanExecute;
					break;
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the trigger button.
		/// </summary>
		/// <remarks>
		/// This executes the command.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Button_Click(object sender, EventArgs e)
		{
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
			Button.KeyDown -= Button_Click;
		}
	}
}
