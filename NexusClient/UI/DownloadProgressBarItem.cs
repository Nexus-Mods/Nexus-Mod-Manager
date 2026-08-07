using System;

using DevExpress.XtraBars;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors.Repository;

namespace Nexus.Client.UI
{
	/// <summary>
	/// DevExpress status-bar progress item used for aggregate download progress and speed.
	/// </summary>
	internal sealed class DownloadProgressBarItem : BarEditItem
	{
		private readonly RepositoryItemProgressBar _repositoryItem;
		private int _optionalValue;
		private bool _showOptionalProgress = true;

		/// <summary>
		/// Defines how the download progress value is interpreted by the update logic.
		/// </summary>
		internal enum FillType
		{
			/// <summary>
			/// Higher values represent better progress.
			/// </summary>
			Ascending,

			/// <summary>
			/// Lower values represent better progress.
			/// </summary>
			Descending,

			/// <summary>
			/// The progress value has no directional interpretation.
			/// </summary>
			Fixed
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DownloadProgressBarItem"/> class.
		/// </summary>
		/// <param name="manager">The bar manager that owns the item.</param>
		internal DownloadProgressBarItem(BarManager manager) : base(manager)
		{
			_repositoryItem = new RepositoryItemProgressBar
			{
				Minimum = 0,
				Maximum = 100,
				PercentView = false,
				ShowTitle = true,
				ReadOnly = true,
				ProgressViewStyle = ProgressViewStyle.Solid
			};
			_repositoryItem.CustomDisplayText += RepositoryItem_CustomDisplayText;
			manager.RepositoryItems.Add(_repositoryItem);
			Edit = _repositoryItem;
			EditWidth = 200;
			EditValue = 0;
		}

		/// <summary>
		/// Gets or sets the current progress value.
		/// </summary>
		internal int Value
		{
			get => EditValue == null ? 0 : Convert.ToInt32(EditValue);
			set
			{
				int clamped = Math.Max(_repositoryItem.Minimum, Math.Min(value, _repositoryItem.Maximum));
				EditValue = clamped;
				Refresh();
			}
		}

		/// <summary>
		/// Gets or sets the maximum progress value.
		/// </summary>
		internal int Maximum
		{
			get => _repositoryItem.Maximum;
			set
			{
				_repositoryItem.Maximum = Math.Max(1, value);
				if (Value > _repositoryItem.Maximum)
					Value = _repositoryItem.Maximum;
				Refresh();
			}
		}

		/// <summary>
		/// Gets or sets the additional value displayed alongside progress, normally KB/s.
		/// </summary>
		internal int OptionalValue
		{
			get => _optionalValue;
			set
			{
				_optionalValue = Math.Max(0, value);
				Refresh();
			}
		}

		/// <summary>
		/// Gets or sets whether the additional download-speed value is displayed.
		/// </summary>
		internal bool ShowOptionalProgress
		{
			get => _showOptionalProgress;
			set
			{
				_showOptionalProgress = value;
				Refresh();
			}
		}

		/// <summary>
		/// Gets or sets the logical fill mode used by the download-status update code.
		/// </summary>
		internal FillType ColorFillMode { get; set; } = FillType.Fixed;

		/// <summary>
		/// Gets whether the progress item has a valid repository editor.
		/// </summary>
		internal bool IsValid => Edit != null;

		/// <summary>
		/// Gets or sets whether the progress item is visible in the status bar.
		/// </summary>
		internal bool Visible
		{
			get => Visibility != BarItemVisibility.Never;
			set => Visibility = value ? BarItemVisibility.Always : BarItemVisibility.Never;
		}

		/// <summary>
		/// Supplies the status text rendered inside the progress bar.
		/// </summary>
		/// <param name="sender">The repository item raising the event.</param>
		/// <param name="e">The display-text event data.</param>
		private void RepositoryItem_CustomDisplayText(object sender, CustomDisplayTextEventArgs e)
		{
			int value = e.Value == null ? Value : Convert.ToInt32(e.Value);
			if (_showOptionalProgress)
			{
				int percentage = Maximum <= 1 ? 0 : (int)Math.Round(value * 100d / Maximum);
				e.DisplayText = String.Format("{0}% ({1}KB/s)", percentage, _optionalValue);
				return;
			}

			e.DisplayText = String.Format("{0}KB/{1}KB", value, Maximum);
		}
	}
}
