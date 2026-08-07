using System.ComponentModel;

using DevExpress.XtraBars;

using Nexus.Client.Commands;

namespace Nexus.Client.UI
{
	/// <summary>
	/// Binds an NMM command to a DevExpress <see cref="BarItem"/> and can be fully detached.
	/// </summary>
	internal sealed class DevExpressBarItemCommandBinding : ICommandBinding
	{
		private readonly BarItem _barItem;
		private readonly Command _command;
		private bool _isBound;

		/// <summary>
		/// Initializes a new instance of the <see cref="DevExpressBarItemCommandBinding"/> class.
		/// </summary>
		/// <param name="barItem">The DevExpress bar item that triggers the command.</param>
		/// <param name="command">The command to bind.</param>
		internal DevExpressBarItemCommandBinding(BarItem barItem, Command command)
		{
			if (barItem == null)
				throw new System.ArgumentNullException(nameof(barItem));
			if (command == null)
				throw new System.ArgumentNullException(nameof(command));

			_barItem = barItem;
			_command = command;
			ApplyCommandState();
			_barItem.ItemClick += BarItem_ItemClick;
			_command.PropertyChanged += Command_PropertyChanged;
			_isBound = true;
		}

		/// <summary>
		/// Gets the DevExpress bar item that triggers the command.
		/// </summary>
		public object Trigger => _barItem;

		/// <summary>
		/// Gets the command bound to the bar item.
		/// </summary>
		public ICommand Command => _command;

		/// <summary>
		/// Executes the bound command.
		/// </summary>
		public void Execute()
		{
			_command.Execute();
		}

		/// <summary>
		/// Removes all event subscriptions between the DevExpress bar item and the command.
		/// </summary>
		public void Unbind()
		{
			if (!_isBound)
				return;

			_barItem.ItemClick -= BarItem_ItemClick;
			_command.PropertyChanged -= Command_PropertyChanged;
			_isBound = false;
		}

		/// <summary>
		/// Applies the command caption, tooltip, image, enabled state, and checked state to the item.
		/// </summary>
		private void ApplyCommandState()
		{
			_barItem.Caption = _command.Name;
			_barItem.Hint = _command.Description;
			_barItem.Enabled = _command.CanExecute;

			if (_command.Image != null)
				_barItem.ImageOptions.Image = _command.Image;

			CheckedCommand checkedCommand = _command as CheckedCommand;
			BarButtonItem buttonItem = _barItem as BarButtonItem;
			if (checkedCommand != null && buttonItem != null)
			{
				buttonItem.ButtonStyle = BarButtonStyle.Check;
				buttonItem.Down = checkedCommand.IsChecked;
			}
		}

		/// <summary>
		/// Updates the bar item when command state changes.
		/// </summary>
		/// <param name="sender">The command that raised the event.</param>
		/// <param name="e">The changed command property.</param>
		private void Command_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case "CanExecute":
					_barItem.Enabled = _command.CanExecute;
					break;
				case "IsChecked":
					CheckedCommand checkedCommand = _command as CheckedCommand;
					BarButtonItem buttonItem = _barItem as BarButtonItem;
					if (checkedCommand != null && buttonItem != null)
						buttonItem.Down = checkedCommand.IsChecked;
					break;
			}
		}

		/// <summary>
		/// Executes the bound command when the bar item is clicked.
		/// </summary>
		/// <param name="sender">The item that raised the event.</param>
		/// <param name="e">The item-click event data.</param>
		private void BarItem_ItemClick(object sender, ItemClickEventArgs e)
		{
			Execute();
		}
	}
}
