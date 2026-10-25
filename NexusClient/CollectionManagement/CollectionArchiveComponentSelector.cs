using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Reviews numbered archive components instead of treating an optional patch directory as the mod root.</summary>
	internal static class CollectionArchiveComponentSelector
	{
		/// <summary>Finds numbered components using original archive paths, preserving legacy FOMOD aliases.</summary>
		internal static IReadOnlyList<string> GetComponents(IMod mod)
		{
			IModArchiveSourcePathProvider paths = mod as IModArchiveSourcePathProvider;
			return mod.GetFileList().Select(path => paths == null ? path : paths.GetArchiveSourcePath(path))
				.Select(path => path.Replace('/', '\\').Split('\\')[0])
				.Where(folder => Regex.IsMatch(folder, @"^\d{2}\s+\S"))
				.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>Collects the component selection through the application's existing DevExpress skin.</summary>
		internal static IReadOnlyList<ScriptedInstallOperation> Select(IMod mod, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<string> components = GetComponents(mod);
			using (var form = new XtraForm())
			using (var choices = new CheckedListBoxControl { Dock = DockStyle.Fill, CheckOnClick = true })
			{
				form.Text = LanguageManager.Format("Collections.Installer.ComponentsTitle", "Choose components: {0}", mod.ModName);
				form.ClientSize = new Size(620, 360);
				form.MinimumSize = new Size(480, 280);
				form.StartPosition = FormStartPosition.CenterParent;
				form.AutoScaleMode = AutoScaleMode.Font;
				form.Padding = new Padding(12);
				var help = new LabelControl { Dock = DockStyle.Top, AutoSizeMode = LabelAutoSizeMode.Vertical, Padding = new Padding(0, 0, 0, 12) };
				help.Text = LanguageManager.Get("Collections.Installer.ComponentsHelp",
					"This archive contains separate components. Select the main files and any optional patches you need. NMM will retain these choices for this Collection.");
				var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
				var cancel = new SimpleButton { Text = LanguageManager.Get("Common.Cancel", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
				var accept = new SimpleButton { Text = LanguageManager.Get("Collections.Installer.UseComponents", "Use selected components"), DialogResult = DialogResult.OK, AutoSize = true };
				buttons.Controls.Add(cancel); buttons.Controls.Add(accept);
				form.Controls.Add(choices); form.Controls.Add(help); form.Controls.Add(buttons);
				form.AcceptButton = accept; form.CancelButton = cancel;
				foreach (string component in components)
					choices.Items.Add(component, component.StartsWith("00", StringComparison.Ordinal) ? CheckState.Checked : CheckState.Unchecked);
				if (form.ShowDialog() != DialogResult.OK)
					throw new OperationCanceledException("Collection component selection was cancelled.", cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				return CreateOperations(mod, Enumerable.Range(0, choices.Items.Count).Where(index => choices.GetItemChecked(index)).Select(index => components[index]));
			}
		}

		/// <summary>Strips only explicitly selected component prefixes and retains their native archive source aliases.</summary>
		internal static IReadOnlyList<ScriptedInstallOperation> CreateOperations(IMod mod, IEnumerable<string> selected)
		{
			var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
			var operations = new List<ScriptedInstallOperation>();
			IModArchiveSourcePathProvider paths = mod as IModArchiveSourcePathProvider;
			foreach (string source in mod.GetFileList().OrderBy(path => paths == null ? path : paths.GetArchiveSourcePath(path), StringComparer.OrdinalIgnoreCase))
			{
				string original = (paths == null ? source : paths.GetArchiveSourcePath(source)).Replace('/', '\\');
				int separator = original.IndexOf('\\');
				if (separator < 0 || !selectedSet.Contains(original.Substring(0, separator))) continue;
				string destination = original.Substring(separator + 1);
				if (!ModInstallFileFilter.IsIgnored(source) && !ModInstallFileFilter.IsIgnored(destination))
					operations.Add(new InstallModFileOperation(source, destination));
			}
			return operations.AsReadOnly();
		}
	}
}
