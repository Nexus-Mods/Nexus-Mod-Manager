using System;
using System.Windows.Forms;
using System.ComponentModel;

namespace Nexus.Client.Commands
{
	/// <summary>
	/// A class that binds a command to a <see cref="ToolStripItem"/>.
	/// </summary>
	public class ToolStripItemCommandBinding : CommandBinding
	{
		#region Properties

		/// <summary>
		/// Gets the <see cref="ToolStripItem"/> that is bound to the command.
		/// </summary>
		/// <value>The <see cref="ToolStripItem"/> that is bound to the command.</value>
		public ToolStripItem ToolStripItem
		{
			get
			{
				return (ToolStripItem)Trigger;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that intializes the object with the given values.
		/// </summary>
		/// <param name="p_tsiMenuItem">The menu item to bind to the command.</param>
		/// <param name="p_cmdCommand">The command to bind to the trigger.</param>
		public ToolStripItemCommandBinding(ToolStripItem p_tsiMenuItem, Command p_cmdCommand)
			: base(p_tsiMenuItem, p_cmdCommand)
		{
			p_tsiMenuItem.Text = p_cmdCommand.Name;
			p_tsiMenuItem.ToolTipText = p_cmdCommand.Description;
			p_tsiMenuItem.Enabled = Command.CanExecute;
			if (p_cmdCommand.Image != null)
				p_tsiMenuItem.Image = p_cmdCommand.Image;
			if ((Command is CheckedCommand) && (p_tsiMenuItem is ToolStripMenuItem))
				((ToolStripMenuItem)p_tsiMenuItem).Checked = ((CheckedCommand)Command).IsChecked;
			p_tsiMenuItem.Click += new EventHandler(ToolStripItem_Click);
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
					ToolStripItem.Enabled = Command.CanExecute;
					break;
				case "IsChecked":
					if ((Command is CheckedCommand) && (ToolStripItem is ToolStripMenuItem))
						((ToolStripMenuItem)ToolStripItem).Checked = ((CheckedCommand)Command).IsChecked;
					break;
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the menu item.
		/// </summary>
		/// <remarks>
		/// This executes the event.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ToolStripItem_Click(object sender, EventArgs e)
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
			ToolStripItem.Click -= ToolStripItem_Click;
		}
	}
}
