namespace Nexus.Client.Games.UI
{
	using System;
	using System.Windows.Forms;
	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.Controls;
	using Nexus.Client.Util.Localization;

	/// <summary>A no-game document, never bound to mod/profile/collection services.</summary>
	internal sealed class GameRequiredDocument : XtraForm
	{
		internal GameRequiredDocument(string caption, Action navigateToDiscovery)
		{
			Text = caption;
			ControlBox = false;
			FormBorderStyle = FormBorderStyle.None;
			var panel = new PanelControl { Dock = DockStyle.Fill, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
			var layout = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				Padding = new Padding(32, 28, 0, 0)
			};
			var message = new LabelControl
			{
				Text = LanguageManager.Get("GameDiscovery.SelectGame", "Select a game to manage"),
				AutoSizeMode = LabelAutoSizeMode.Vertical,
				Width = 360,
				Height = 36
			};
			var navigate = new SimpleButton
			{
				Text = LanguageManager.Get("GameDiscovery.Open", "Open Game Discovery"),
				Width = 180
			};
			navigate.Click += (sender, args) => navigateToDiscovery?.Invoke();
			layout.Controls.Add(message);
			layout.Controls.Add(navigate);
			panel.Controls.Add(layout);
			Controls.Add(panel);
		}
	}
}
