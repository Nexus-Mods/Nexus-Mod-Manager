namespace Nexus.Client.ModManagement.Scripting.XmlScript.CPL.Controls
{
	partial class CplFlagConditionEditor
	{
		/// <summary> 
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.label2 = new System.Windows.Forms.Label();
			this.tlpEditFlag = new System.Windows.Forms.TableLayoutPanel();
			this.tbxFlagValue = new System.Windows.Forms.TextBox();
			this.label3 = new System.Windows.Forms.Label();
			this.tbxFlagName = new System.Windows.Forms.TextBox();
			this.erpErrors = new System.Windows.Forms.ErrorProvider(this.components);
			this.tlpEditFlag.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).BeginInit();
			this.SuspendLayout();
			// 
			// label2
			// 
			this.label2.Anchor = System.Windows.Forms.AnchorStyles.None;
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(3, 11);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(61, 13);
			this.label2.TabIndex = 0;
			this.label2.Text = "Flag Name:";
			// 
			// tlpEditFlag
			// 
			this.tlpEditFlag.ColumnCount = 5;
			this.tlpEditFlag.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tlpEditFlag.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
			this.tlpEditFlag.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 20F));
			this.tlpEditFlag.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tlpEditFlag.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
			this.tlpEditFlag.Controls.Add(this.tbxFlagValue, 4, 0);
			this.tlpEditFlag.Controls.Add(this.label3, 3, 0);
			this.tlpEditFlag.Controls.Add(this.tbxFlagName, 1, 0);
			this.tlpEditFlag.Controls.Add(this.label2, 0, 0);
			this.tlpEditFlag.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tlpEditFlag.GrowStyle = System.Windows.Forms.TableLayoutPanelGrowStyle.FixedSize;
			this.tlpEditFlag.Location = new System.Drawing.Point(20, 0);
			this.tlpEditFlag.Name = "tlpEditFlag";
			this.tlpEditFlag.RowCount = 1;
			this.tlpEditFlag.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tlpEditFlag.Size = new System.Drawing.Size(610, 35);
			this.tlpEditFlag.TabIndex = 7;
			// 
			// tbxFlagValue
			// 
			this.tbxFlagValue.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxFlagValue.Location = new System.Drawing.Point(373, 7);
			this.tbxFlagValue.Name = "tbxFlagValue";
			this.tbxFlagValue.Size = new System.Drawing.Size(234, 20);
			this.tbxFlagValue.TabIndex = 1;
			// 
			// label3
			// 
			this.label3.Anchor = System.Windows.Forms.AnchorStyles.None;
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(330, 11);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(37, 13);
			this.label3.TabIndex = 1;
			this.label3.Text = "Value:";
			// 
			// tbxFlagName
			// 
			this.tbxFlagName.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
			this.tbxFlagName.Location = new System.Drawing.Point(70, 7);
			this.tbxFlagName.Name = "tbxFlagName";
			this.tbxFlagName.Size = new System.Drawing.Size(234, 20);
			this.tbxFlagName.TabIndex = 0;
			// 
			// erpErrors
			// 
			this.erpErrors.ContainerControl = this;
			// 
			// CplFlagConditionEditor
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.tlpEditFlag);
			this.Name = "CplFlagConditionEditor";
			this.Size = new System.Drawing.Size(630, 35);
			this.Controls.SetChildIndex(this.tlpEditFlag, 0);
			this.tlpEditFlag.ResumeLayout(false);
			this.tlpEditFlag.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.erpErrors)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TableLayoutPanel tlpEditFlag;
		private System.Windows.Forms.TextBox tbxFlagValue;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.TextBox tbxFlagName;
		private System.Windows.Forms.ErrorProvider erpErrors;
	}
}
