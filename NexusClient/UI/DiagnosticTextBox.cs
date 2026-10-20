using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Nexus.Client.UI
{
	/// <summary>Read-only diagnostic text using the same theme and accessible severity colours as the plugin list.</summary>
	internal sealed class DiagnosticTextBox : RichTextBox
	{
		/// <summary>Creates a selectable, scrollable message panel without rich-text editing or automatic links.</summary>
		internal DiagnosticTextBox()
		{
			ReadOnly = true;
			DetectUrls = false;
			BorderStyle = BorderStyle.FixedSingle;
			Dock = DockStyle.Bottom;
		}

		/// <summary>Replaces messages and highlights only the supplied error and warning passages.</summary>
		internal void SetDiagnosticText(string text, IEnumerable<string> errors, IEnumerable<string> warnings)
		{
			BackColor = DevExpressDisplaySettingsApplier.GetSkinColor("Window", SystemColors.Window);
			ForeColor = DevExpressDisplaySettingsApplier.GetSkinColor("WindowText", SystemColors.WindowText);
			Text = text;
			SelectAll();
			SelectionColor = ForeColor;
			NmmColorPalette palette = NmmColorPalette.Resolve(NmmIconProvider.CurrentColorProfile,
				DevExpressDisplaySettingsApplier.IsDarkSkinSurface());
			Highlight(warnings, palette.PluginWarningColor);
			Highlight(errors, palette.PluginErrorColor);
			Select(0, 0);
			ScrollToCaret();
		}

		/// <summary>Colours exact message passages without treating user-controlled folder names as markup.</summary>
		private void Highlight(IEnumerable<string> messages, Color color)
		{
			if (messages == null) return;
			foreach (string message in messages)
			{
				if (String.IsNullOrEmpty(message)) continue;
				string normalized = message.Replace("\r\n", "\n");
				int start = 0;
				while ((start = Text.IndexOf(normalized, start, StringComparison.Ordinal)) >= 0)
				{
					Select(start, normalized.Length);
					SelectionColor = color;
					start += SelectionLength;
				}
			}
		}
	}
}
