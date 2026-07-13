namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Drawing;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    using DevExpress.XtraEditors;
    using DevExpress.XtraLayout;

    public sealed class FilePreviewControl : XtraUserControl
    {
        private readonly FilePreviewManager _previewManager = new FilePreviewManager();
        private readonly LayoutControl _layoutControl;
        private readonly LookUpEdit _ownerSelector;
        private readonly PanelControl _previewHost;
        private readonly PictureEdit _pictureEdit;
        private readonly LabelControl _messageLabel;
        private readonly List<FilePreviewOwnerOption> _ownerOptions = new List<FilePreviewOwnerOption>();
        private CancellationTokenSource _previewCancellation;
        private int _previewGeneration;
        private bool _suppressOwnerChange;
        private Bitmap _currentBitmap;
        private FileManagerRow _currentRow;

        public FilePreviewControl()
        {
            Dock = DockStyle.Fill;

            _layoutControl = new LayoutControl
            {
                Dock = DockStyle.Fill,
                AllowCustomization = false
            };

            _ownerSelector = new LookUpEdit
            {
                Properties =
                {
                    DisplayMember = "OwnerName",
                    ValueMember = "OwnerKey",
                    NullText = String.Empty,
                    ShowHeader = false,
                    TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor
                }
            };
            _ownerSelector.Properties.Columns.Add(new DevExpress.XtraEditors.Controls.LookUpColumnInfo("OwnerName", "Owner"));
            _ownerSelector.EditValueChanged += OwnerSelector_EditValueChanged;

            _previewHost = new PanelControl
            {
                BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder
            };

            _pictureEdit = new PictureEdit
            {
                Dock = DockStyle.Fill,
                Visible = false,
                Properties =
                {
                    AllowFocused = false,
                    ShowMenu = false,
                    SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom,
                    NullText = String.Empty
                }
            };

            _messageLabel = new LabelControl
            {
                Dock = DockStyle.Fill,
                AutoSizeMode = LabelAutoSizeMode.None,
                Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center, VAlignment = DevExpress.Utils.VertAlignment.Center } },
                Text = "Select a file to preview."
            };

            _previewHost.Controls.Add(_pictureEdit);
            _previewHost.Controls.Add(_messageLabel);

            _layoutControl.Controls.Add(_ownerSelector);
            _layoutControl.Controls.Add(_previewHost);
            LayoutControlGroup root = _layoutControl.Root;
            root.EnableIndentsWithoutBorders = DevExpress.Utils.DefaultBoolean.False;
            root.GroupBordersVisible = false;
            root.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 0);
            root.TextVisible = false;

            LayoutControlItem ownerItem = root.AddItem(String.Empty, _ownerSelector);
            ownerItem.TextVisible = false;
            ownerItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 4);
            ownerItem.SizeConstraintsType = SizeConstraintsType.Custom;
            ownerItem.MinSize = new Size(100, _ownerSelector.Height + 4);
            ownerItem.MaxSize = new Size(10000, _ownerSelector.Height + 4);

            LayoutControlItem previewItem = root.AddItem(String.Empty, _previewHost);
            previewItem.TextVisible = false;
            previewItem.Padding = new DevExpress.XtraLayout.Utils.Padding(0, 0, 0, 0);

            Controls.Add(_layoutControl);
            SetEmpty("Select a file to preview.");
        }

        public void SetSelectedRow(FileManagerRow row)
        {
            _currentRow = row;
            CancelPreview();
            ClearImage();
            BuildOwnerOptions(row);

            if (row == null || String.IsNullOrWhiteSpace(row.FullPath))
            {
                SetEmpty("Select a file to preview.");
                return;
            }

            FilePreviewOwnerOption selectedOwner = GetSelectedOwnerOption(row);
            string previewPath = GetPreviewPath(row, selectedOwner);

            BeginPreview(previewPath);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelPreview();
                ClearImage();
                _ownerSelector.EditValueChanged -= OwnerSelector_EditValueChanged;
                if (_previewCancellation != null)
                    _previewCancellation.Dispose();
            }

            base.Dispose(disposing);
        }

        private void BuildOwnerOptions(FileManagerRow row)
        {
            _ownerOptions.Clear();
            if (row != null && row.OwnerCandidates != null)
            {
                foreach (FileManagerOwnerCandidate candidate in row.OwnerCandidates)
                {
                    _ownerOptions.Add(new FilePreviewOwnerOption(candidate.OwnerKey, candidate.ModName, candidate.PreviewFilePath));
                }
            }

            _suppressOwnerChange = true;
            _ownerSelector.Properties.DataSource = null;
            _ownerSelector.Properties.DataSource = _ownerOptions;
            _ownerSelector.EditValue = row == null ? null : row.OwnerKey;
            _ownerSelector.Enabled = _ownerOptions.Count > 1;
            _ownerSelector.Visible = _ownerOptions.Count > 1;
            _layoutControl.GetItemByControl(_ownerSelector).Visibility = _ownerOptions.Count > 1 ? DevExpress.XtraLayout.Utils.LayoutVisibility.Always : DevExpress.XtraLayout.Utils.LayoutVisibility.Never;
            _suppressOwnerChange = false;
        }

        private FilePreviewOwnerOption GetSelectedOwnerOption(FileManagerRow row)
        {
            if (row == null || _ownerOptions.Count == 0)
                return null;

            foreach (FilePreviewOwnerOption option in _ownerOptions)
                if (String.Equals(option.OwnerKey, row.OwnerKey, StringComparison.OrdinalIgnoreCase))
                    return option;

            return _ownerOptions[0];
        }

        private static string GetPreviewPath(FileManagerRow row, FilePreviewOwnerOption option)
        {
            if (row == null)
                return String.Empty;

            if (option != null && !String.IsNullOrWhiteSpace(option.FilePath) && File.Exists(option.FilePath))
                return option.FilePath;

            if (option == null || String.Equals(option.OwnerKey, row.OwnerKey, StringComparison.OrdinalIgnoreCase))
                return row.FullPath;

            return option.FilePath;
        }
        private void OwnerSelector_EditValueChanged(object sender, EventArgs e)
        {
            if (_suppressOwnerChange || _currentRow == null)
                return;

            string ownerKey = _ownerSelector.EditValue as string;
            foreach (FilePreviewOwnerOption option in _ownerOptions)
            {
                if (!String.Equals(option.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                BeginPreview(GetPreviewPath(_currentRow, option));
                return;
            }
        }

        private void BeginPreview(string filePath)
        {
            CancelPreview();
            ClearImage();

            if (String.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                SetError("Preview file is missing.");
                return;
            }

            int generation = Interlocked.Increment(ref _previewGeneration);
            CancellationTokenSource cancellation = new CancellationTokenSource();
            _previewCancellation = cancellation;
            SetEmpty("Loading preview...");

            _previewManager.LoadPreviewAsync(new FilePreviewRequest(filePath), cancellation.Token)
                .ContinueWith(task => ApplyPreviewResult(task, generation, cancellation), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void ApplyPreviewResult(Task<FilePreviewResult> task, int generation, CancellationTokenSource cancellation)
        {
            if (!ReferenceEquals(_previewCancellation, cancellation))
            {
                DisposeTaskResult(task);
                cancellation.Dispose();
                return;
            }

            _previewCancellation = null;
            cancellation.Dispose();

            if (generation != _previewGeneration)
            {
                DisposeTaskResult(task);
                return;
            }

            if (task.IsCanceled)
            {
                SetEmpty("Select a file to preview.");
                return;
            }

            if (task.IsFaulted)
            {
                SetError("Preview is not available for this file.");
                return;
            }

            using (FilePreviewResult result = task.Result)
            {
                if (result.State == FilePreviewState.Image && result.Image != null)
                {
                    Bitmap bitmap = result.DetachImage();
                    SetImage(bitmap);
                }
                else if (result.State == FilePreviewState.Unsupported)
                {
                    SetUnsupported(result.Message);
                }
                else if (result.State == FilePreviewState.Error)
                {
                    SetError(result.Message);
                }
                else
                {
                    SetEmpty(result.Message);
                }
            }
        }

        private static void DisposeTaskResult(Task<FilePreviewResult> task)
        {
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                task.Result.Dispose();
        }

        private void CancelPreview()
        {
            Interlocked.Increment(ref _previewGeneration);
            CancellationTokenSource cancellation = _previewCancellation;
            _previewCancellation = null;
            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
        }

        private void SetImage(Bitmap bitmap)
        {
            ClearImage();
            _currentBitmap = bitmap;
            _pictureEdit.Image = _currentBitmap;
            _pictureEdit.Visible = true;
            _messageLabel.Visible = false;
        }

        private void SetEmpty(string message)
        {
            ShowMessage(String.IsNullOrWhiteSpace(message) ? "Select a file to preview." : message);
        }

        private void SetUnsupported(string message)
        {
            ShowMessage(String.IsNullOrWhiteSpace(message) ? "Preview is not available for this file type." : message);
        }

        private void SetError(string message)
        {
            ShowMessage(String.IsNullOrWhiteSpace(message) ? "Preview could not be loaded." : message);
        }

        private void ShowMessage(string message)
        {
            ClearImage();
            _messageLabel.Text = message;
            _messageLabel.Visible = true;
            _pictureEdit.Visible = false;
        }

        private void ClearImage()
        {
            Image oldImage = _pictureEdit.Image;
            _pictureEdit.Image = null;
            _currentBitmap = null;
            if (oldImage != null)
                oldImage.Dispose();
        }
    }
}