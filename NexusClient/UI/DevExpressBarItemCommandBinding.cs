using System.ComponentModel;
using System.Drawing;

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
		private readonly bool _hideWhenDisabled;
		private bool _isBound;

		/// <summary>
		/// Initializes a new instance of the <see cref="DevExpressBarItemCommandBinding"/> class.
		/// </summary>
		/// <param name="barItem">The DevExpress bar item that triggers the command.</param>
		/// <param name="command">The command to bind.</param>
		internal DevExpressBarItemCommandBinding(BarItem barItem, Command command, bool hideWhenDisabled = false)
		{
			if (barItem == null)
				throw new System.ArgumentNullException(nameof(barItem));
			if (command == null)
				throw new System.ArgumentNullException(nameof(command));

			_barItem = barItem;
			_command = command;
			_hideWhenDisabled = hideWhenDisabled;
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
			ApplyEnabledState();

			if (_barItem.ImageOptions.Image == null && _barItem.ImageOptions.SvgImage == null && _command.Image != null)
				_barItem.ImageOptions.Image = DevExpressDisplaySettingsApplier.ResizeBarItemImage(_command.Image, new Size(16, 16));

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
					ApplyEnabledState();
					break;
				case "IsChecked":
					CheckedCommand checkedCommand = _command as CheckedCommand;
					BarButtonItem buttonItem = _barItem as BarButtonItem;
					if (checkedCommand != null && buttonItem != null)
						buttonItem.Down = checkedCommand.IsChecked;
					break;
			}
		}

		private void ApplyEnabledState()
		{
			_barItem.Enabled = _command.CanExecute;
			if (_hideWhenDisabled)
				_barItem.Visibility = _command.CanExecute ? BarItemVisibility.Always : BarItemVisibility.Never;
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

	/// <summary>
	/// Binds an argument-taking NMM command to a DevExpress <see cref="BarItem"/> and can be fully detached.
	/// </summary>
	/// <typeparam name="T">The type of argument supplied to the command.</typeparam>
	internal sealed class DevExpressBarItemCommandBinding<T> : ICommandBinding
	{
		private readonly BarItem _barItem;
		private readonly Nexus.Client.Commands.Generic.Command<T> _command;
		private readonly System.Func<T> _getArgument;
		private readonly bool _hideWhenDisabled;
		private bool _isBound;

		/// <summary>
		/// Initializes a new instance of the <see cref="DevExpressBarItemCommandBinding{T}"/> class.
		/// </summary>
		/// <param name="barItem">The DevExpress bar item that triggers the command.</param>
		/// <param name="command">The command to bind.</param>
		/// <param name="getArgument">Returns the argument to pass when the command executes.</param>
		internal DevExpressBarItemCommandBinding(BarItem barItem, Nexus.Client.Commands.Generic.Command<T> command, System.Func<T> getArgument, bool hideWhenDisabled = false)
		{
			if (barItem == null)
				throw new System.ArgumentNullException(nameof(barItem));
			if (command == null)
				throw new System.ArgumentNullException(nameof(command));

			_barItem = barItem;
			_command = command;
			_getArgument = getArgument;
			_hideWhenDisabled = hideWhenDisabled;
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
		/// Executes the bound command with the current argument.
		/// </summary>
		public void Execute()
		{
			_command.Execute(_getArgument == null ? default(T) : _getArgument());
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
		/// Applies the command caption, tooltip, image, and enabled state to the item.
		/// </summary>
		private void ApplyCommandState()
		{
			_barItem.Caption = _command.Name;
			_barItem.Hint = _command.Description;
			ApplyEnabledState();

			if (_barItem.ImageOptions.Image == null && _barItem.ImageOptions.SvgImage == null && _command.Image != null)
				_barItem.ImageOptions.Image = DevExpressDisplaySettingsApplier.ResizeBarItemImage(_command.Image, new Size(16, 16));
		}

		/// <summary>
		/// Updates the bar item when command state changes.
		/// </summary>
		/// <param name="sender">The command that raised the event.</param>
		/// <param name="e">The changed command property.</param>
		private void Command_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != "CanExecute")
				return;

			ApplyEnabledState();
			if (_command.CanExecute)
			{
				_barItem.Caption = _command.Name;
				_barItem.Hint = _command.Description;
			}
		}

		private void ApplyEnabledState()
		{
			_barItem.Enabled = _command.CanExecute;
			if (_hideWhenDisabled)
				_barItem.Visibility = _command.CanExecute ? BarItemVisibility.Always : BarItemVisibility.Never;
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
