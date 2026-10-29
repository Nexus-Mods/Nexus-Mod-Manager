using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.ModManagement.LoadOrder;
using Nexus.Client.ModManagement.LoadOrder.UI;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Regression source for navigation while immutable load-order inspection rows are enriched; no game services or helper are started.</summary>
	[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
	public sealed class LoadOrderGridViewportTests
	{
		/// <summary>Background and final result binding preserve focus and the visible anchor while a user browses a filtered list.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void InspectionUpdatesPreserveFocusedModAndScrolledViewport(bool finalResult)
		{
			string[] ids = Enumerable.Range(1, 100).Select(index => new Guid(index, 0, 0, new byte[8]).ToString("D")).ToArray();
			string records = "<node id=\"ModuleShortDesc\"><attribute id=\"UUID\" value=\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"/><attribute id=\"Folder\" value=\"GustavX\"/></node>"
				+ String.Concat(ids.Select((id, index) => "<node id=\"ModuleShortDesc\"><attribute id=\"UUID\" value=\"" + id + "\"/><attribute id=\"Name\" value=\"Module " + index.ToString("D3")
					+ "\"/><attribute id=\"Folder\" value=\"Folder" + index + "\"/><attribute id=\"Version64\" value=\"1\"/></node>"));
			var document = new LarianLoadOrderDocument(Path.Combine(Path.GetTempPath(), "nmm-viewport-fixture", "modsettings.lsx"), Encoding.UTF8.GetBytes(
				"<save><version major=\"4\" minor=\"8\"/><region id=\"ModuleSettings\"><node id=\"root\"><children><node id=\"Mods\"><children>" + records + "</children></node></children></node></region></save>"));
			using (var control = new LoadOrderControl { Size = new Size(1200, 900), ShowInTaskbar = false, Opacity = 0.01 })
			{
				Type type = typeof(LoadOrderControl);
				FieldInfo session = type.GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic);
				FieldInfo busy = type.GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic);
				MethodInfo bind = type.GetMethod("BindRows", BindingFlags.Instance | BindingFlags.NonPublic);
				GridView view = (GridView)type.GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(control);
				session.SetValue(control, LoadOrderEditorSession.Preview(document)); bind.Invoke(control, new object[] { null });
				control.Show(); Application.DoEvents();
				view.FindFilterText = "Module"; Application.DoEvents();
				view.FocusedRowHandle = view.LocateByValue("Key", ids[70]); view.TopRowIndex = 40;
				Application.DoEvents();
				string topKey = ((LoadOrderModuleRow)view.GetRow(view.GetVisibleRowHandle(view.TopRowIndex))).Key;
				busy.SetValue(control, true);
				try
				{
					for (int update = 0; update < 5; update++)
					{
						var inventory = new LoadOrderInventory(null, new LoadOrderPackageObservation[0], true, new LoadOrderDiagnostic[0]);
						session.SetValue(control, finalResult ? new LoadOrderEditorSession(LarianLoadOrderAnalyzer.Analyze(document, inventory)) : LoadOrderEditorSession.Preview(document));
						bind.Invoke(control, new object[] { null }); Application.DoEvents();
						Assert.That(((LoadOrderModuleRow)view.GetFocusedRow()).Key, Is.EqualTo(ids[70]));
						Assert.That(((LoadOrderModuleRow)view.GetRow(view.GetVisibleRowHandle(view.TopRowIndex))).Key, Is.EqualTo(topKey));
						Assert.That(view.FindFilterText, Is.EqualTo("Module"));
					}
				}
				finally { busy.SetValue(control, false); }
			}
		}
	}
}
