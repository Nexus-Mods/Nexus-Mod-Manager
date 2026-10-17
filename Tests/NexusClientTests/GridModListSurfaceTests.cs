namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.Drawing;
	using System.Linq;
	using System.Threading;
	using System.Windows.Forms;
	using DevExpress.Data;
	using DevExpress.XtraGrid.Columns;
	using DevExpress.XtraGrid.Views.Grid;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.ModManagement.UI;
	using Nexus.Client.Mods;
	using NUnit.Framework;

	/// <summary>Exercises property refreshes against the real flat Mods grid and its cached unbound cell text.</summary>
	[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
	public class GridModListSurfaceTests
	{
		private Form _host;
		private ModGridDXControl _control;
		private GridModListSurface _surface;
		private List<IMod> _mods;
		private readonly Dictionary<IMod, string> _paintedLatest = new Dictionary<IMod, string>();

		/// <summary>Creates a plain-list unbound grid matching the production Mods surface.</summary>
		[SetUp]
		public void SetUp()
		{
			_mods = Enumerable.Range(0, 100).Select(index => (IMod)new InstallLog.DummyMod(
				"Mod " + index.ToString("D3"), "mod" + index + ".zip", new Version(1, 0), "1.0", "1.0", String.Empty)).ToList();
			_control = new ModGridDXControl { Dock = DockStyle.Fill };
			GridView view = _control.GridView;
			view.OptionsView.ShowGroupPanel = false;
			view.OptionsBehavior.Editable = false;
			view.OptionsSelection.MultiSelect = true;
			_control.GridControl.DataSource = _mods;
			view.Columns.Clear();
			view.Columns.Add(new GridColumn { FieldName = "ModName", UnboundType = UnboundColumnType.String, VisibleIndex = 0, Width = 300 });
			view.Columns.Add(new GridColumn { FieldName = "LastKnownVersion", UnboundType = UnboundColumnType.String, VisibleIndex = 1, Width = 150 });
			view.CustomUnboundColumnData += (sender, args) =>
			{
				if (!args.IsGetData) return;
				IMod mod = _mods[args.ListSourceRowIndex];
				args.Value = args.Column.FieldName == "ModName" ? mod.ModName : mod.LastKnownVersion;
			};
			view.CustomDrawCell += (sender, args) =>
			{
				if (args.Column.FieldName == "LastKnownVersion" && args.RowHandle >= 0)
					_paintedLatest[_mods[view.GetDataSourceRowIndex(args.RowHandle)]] = args.DisplayText;
			};
			_surface = new GridModListSurface(_control, _mods, "ModName");
			_host = new Form
			{
				ShowInTaskbar = false,
				StartPosition = FormStartPosition.Manual,
				Location = SystemInformation.WorkingArea.Location,
				ClientSize = new Size(700, 350),
				FormBorderStyle = FormBorderStyle.None,
				Opacity = 0.01
			};
			_host.Controls.Add(_control);
			_host.Show();
			view.Columns["ModName"].SortOrder = ColumnSortOrder.Ascending;
			PaintPendingUpdates();
		}

		/// <summary>Releases UI handles after each regression case.</summary>
		[TearDown]
		public void TearDown()
		{
			_host?.Dispose();
			_paintedLatest.Clear();
		}

		/// <summary>Reproduces updated warning state with stale version text until a cell is clicked.</summary>
		[TestCase("LastKnownVersion")]
		[TestCase(null)]
		public void RefreshMod_UpdatesPaintedLatestWithoutClickingOrMovingTheViewport(string propertyName)
		{
			GridView view = _control.GridView;
			view.FocusedRowHandle = 43;
			view.ClearSelection();
			view.SelectRow(43);
			view.SelectRow(45);
			view.TopRowIndex = 40;
			PaintPendingUpdates();
			int top = view.TopRowIndex;
			IMod focused = _surface.FocusedMod;
			IMod[] selected = _surface.SelectedMods.ToArray();
			IMod[] changed = { _mods[42], _mods[44], _mods[46] };
			foreach (IMod mod in changed)
				Assert.That(_paintedLatest[mod], Is.EqualTo("1.0"));

			foreach (InstallLog.DummyMod mod in changed)
			{
				mod.LastKnownVersion = "2.0";
				_surface.RefreshMod(mod, propertyName);
			}
			PaintPendingUpdates();

			foreach (IMod mod in changed)
				Assert.That(_paintedLatest[mod], Is.EqualTo("2.0"), "A repaint must use refreshed unbound text for " + mod.ModName);
			Assert.That(view.TopRowIndex, Is.EqualTo(top));
			Assert.That(_surface.FocusedMod, Is.SameAs(focused));
			Assert.That(_surface.SelectedMods, Is.EquivalentTo(selected));
		}

		/// <summary>Retains the existing full sort refresh when the changed property is a sort key.</summary>
		[Test]
		public void RefreshMod_LatestSortUsesUpdatedVersionValues()
		{
			GridView view = _control.GridView;
			view.Columns["ModName"].SortOrder = ColumnSortOrder.None;
			view.Columns["LastKnownVersion"].SortOrder = ColumnSortOrder.Ascending;
			InstallLog.DummyMod mod = (InstallLog.DummyMod)_mods[0];
			mod.LastKnownVersion = "2.0";
			_surface.RefreshMod(mod, "LastKnownVersion");
			PaintPendingUpdates();
			Assert.That(view.GetDataSourceRowIndex(view.RowCount - 1), Is.EqualTo(0));
			Assert.That(view.GetRowCellDisplayText(view.RowCount - 1, "LastKnownVersion"), Is.EqualTo("2.0"));
		}

		/// <summary>Drains queued grid paints without simulating cell clicks or refreshing the data source.</summary>
		private void PaintPendingUpdates()
		{
			Application.DoEvents();
			_control.GridControl.Update();
		}
	}
}
