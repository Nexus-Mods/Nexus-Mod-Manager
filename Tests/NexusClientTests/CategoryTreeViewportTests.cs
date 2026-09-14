namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.Drawing;
	using System.Linq;
	using System.Reflection;
	using System.Threading;
	using System.Windows.Forms;
	using DevExpress.XtraTreeList;
	using DevExpress.XtraTreeList.Nodes;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using NUnit.Framework;

	/// <summary>
	/// Exercises Category View scrolling and selection using a real TreeList and its UI message queue.
	/// </summary>
	[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
	public class CategoryTreeViewportTests
	{
		private Form _host;
		private Control _view;
		private TreeList _tree;
		private object _surface;
		private List<IMod> _mods;
		private HashSet<IMod> _active;
		private int _userSortNotifications;

		/// <summary>
		/// Creates an off-screen UI host with enough rows to reproduce activation refreshes while scrolled down.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			_mods = Enumerable.Range(0, 240)
				.Select(index => (IMod)new InstallLog.DummyMod("Mod " + index.ToString("D3"), "mod" + index + ".7z"))
				.ToList();
			_active = new HashSet<IMod>(_mods.Where((mod, index) => index % 2 == 0));
			Assembly assembly = typeof(ModManager).Assembly;
			Type viewType = assembly.GetType("Nexus.Client.ModManagement.UI.ModCategoryTreeDXControl", true);
			_view = (Control)Activator.CreateInstance(viewType);
			_tree = (TreeList)viewType.GetProperty("TreeList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_view);
			Type surfaceType = assembly.GetType("Nexus.Client.ModManagement.UI.TreeModListSurface", true);
			_surface = Activator.CreateInstance(surfaceType, new object[]
			{
				_view, _mods,
				new Func<IMod, string>(mod => "Category"),
				new Func<IMod, string>(mod => _active.Contains(mod) ? "Active" : "Uninstalled"),
				new Func<IMod, bool>(mod => false),
				new Func<IMod, bool>(mod => _active.Contains(mod)),
				new Func<int, int, string>((active, total) => active + "/" + total)
			});
			_host = new Form
			{
				ShowInTaskbar = false,
				StartPosition = FormStartPosition.Manual,
				Location = new Point(-30000, -30000),
				ClientSize = new Size(900, 400)
			};
			_view.Dock = DockStyle.Fill;
			_host.Controls.Add(_view);
			_host.Show();
			InvokeSurface("SetMods", _mods);
			_tree.ExpandAll();
			Application.DoEvents();
			EventInfo sorting = viewType.GetEvent("SortingCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
			sorting.GetAddMethod(true).Invoke(_view, new object[] { new EventHandler((sender, args) => _userSortNotifications++) });
		}

		/// <summary>
		/// Releases the UI host and all child handles after each case.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			_host?.Dispose();
			_view?.Dispose();
		}

		/// <summary>
		/// Keeps the same selected mods and scroll offset through repeated install/uninstall status changes.
		/// </summary>
		[TestCase("ModName", false)]
		[TestCase("ModName", true)]
		[TestCase("Status", false)]
		[TestCase("Status", true)]
		public void ActivationRefreshPreservesViewportAndModIdentity(string sortColumn, bool multiSelect)
		{
			PrepareSort(sortColumn);
			TreeListNode focused = _tree.GetNodeByVisibleIndex(80);
			TreeListNode second = _tree.GetNodeByVisibleIndex(82);
			Assert.That(focused.Tag, Is.InstanceOf<IMod>());
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			if (multiSelect) _tree.Selection.Add(second);
			_tree.TopVisibleNodeIndex = 80;
			int topIndex = _tree.TopVisibleNodeIndex;
			Assert.That(topIndex, Is.GreaterThan(0));
			object[] expectedSelection = _tree.Selection.Cast<TreeListNode>().Select(node => node.Tag).ToArray();

			for (int iteration = 0; iteration < 4; iteration++)
			{
				IMod mod = (IMod)focused.Tag;
				if (!_active.Remove(mod)) _active.Add(mod);
				InvokeSurface("RefreshData");
				Application.DoEvents();
				Assert.That(_tree.TopVisibleNodeIndex, Is.EqualTo(topIndex), "Refresh must not follow a sorted mod to the top.");
				Assert.That(_tree.FocusedNode, Is.SameAs(focused));
				Assert.That(_tree.Selection.Cast<TreeListNode>().Select(node => node.Tag), Is.EquivalentTo(expectedSelection));
				Assert.That(_userSortNotifications, Is.Zero, "Internal sorting must not trigger the user jump-after-sorting option.");
			}
		}

		/// <summary>
		/// Preserves the visible anchor and selection when a downloaded or restored archive is inserted above it.
		/// </summary>
		[Test]
		public void AddingModPreservesViewportAnchorAndSelection()
		{
			PrepareSort("ModName");
			TreeListNode focused = _tree.GetNodeByVisibleIndex(83);
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			_tree.TopVisibleNodeIndex = 80;
			TreeListNode topNode = _tree.GetNodeByVisibleIndex(_tree.TopVisibleNodeIndex);
			IMod added = new InstallLog.DummyMod("A newly downloaded mod", "new.7z");
			_mods.Add(added);
			InvokeSurface("AddMods", (object)new[] { added });
			Application.DoEvents();
			Assert.That(_tree.GetNodeByVisibleIndex(_tree.TopVisibleNodeIndex), Is.SameAs(topNode));
			Assert.That(_tree.FocusedNode, Is.SameAs(focused));
			Assert.That(_tree.Selection.Cast<TreeListNode>(), Is.EquivalentTo(new[] { focused }));
			Assert.That(_userSortNotifications, Is.Zero);
		}

		/// <summary>
		/// Preserves category selection as well as mod selection during passive updates.
		/// </summary>
		[Test]
		public void ActivationRefreshPreservesCategorySelection()
		{
			PrepareSort("ModName");
			TreeListNode category = _tree.Nodes[0];
			_tree.FocusedNode = category;
			_tree.Selection.Clear();
			_tree.Selection.Add(category);
			_tree.TopVisibleNodeIndex = 80;
			int topIndex = _tree.TopVisibleNodeIndex;
			_active.Clear();
			InvokeSurface("RefreshData");
			Application.DoEvents();
			Assert.That(_tree.FocusedNode, Is.SameAs(category));
			Assert.That(_tree.Selection.Cast<TreeListNode>(), Is.EquivalentTo(new[] { category }));
			Assert.That(_tree.TopVisibleNodeIndex, Is.EqualTo(topIndex));
			Assert.That(_userSortNotifications, Is.Zero);
		}

		/// <summary>
		/// Applies a user sort before the operation under test and drains initialization callbacks.
		/// </summary>
		private void PrepareSort(string fieldName)
		{
			_tree.ClearSorting();
			_tree.Columns[fieldName].SortOrder = SortOrder.Ascending;
			Application.DoEvents();
			_userSortNotifications = 0;
		}

		/// <summary>
		/// Invokes the production internal surface without exposing application internals publicly for tests.
		/// </summary>
		private void InvokeSurface(string methodName, params object[] arguments)
		{
			_surface.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public).Invoke(_surface, arguments);
		}
	}
}
