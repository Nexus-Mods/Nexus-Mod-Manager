namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Drawing;
	using System.Linq;
	using System.Reflection;
	using System.Runtime.InteropServices;
	using System.Threading;
	using System.Windows.Forms;
	using DevExpress.XtraTreeList;
	using DevExpress.XtraTreeList.Nodes;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModManagement.InstallationLog;
	using Nexus.Client.Mods;
	using Nexus.Client.UI.Controls;
	using NUnit.Framework;

	/// <summary>
	/// Exercises Category View scrolling, sorting, editing and mouse targeting using a real TreeList and its UI message queue.
	/// </summary>
	[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
	public class CategoryTreeViewportTests
	{
		private const int WmLButtonDown = 0x0201;
		private const int WmLButtonUp = 0x0202;
		private const int WmLButtonDoubleClick = 0x0203;
		private const int MkLButton = 0x0001;
		private const string StatusFieldName = "ModStatus";
		private const string LatestFieldName = "LastKnownVersion";

		private Form _host;
		private Control _view;
		private TreeList _tree;
		private object _surface;
		private List<IMod> _mods;
		private HashSet<IMod> _active;
		private Dictionary<IMod, string> _categories;
		private Dictionary<IMod, int?> _sortNumbers;
		private int _userSortNotifications;
		private int _nativeSortNotifications;
		private int _userNavigationNotifications;

		/// <summary>
		/// Sends one window message directly to the hosted TreeList so native mouse processing is exercised by regression tests.
		/// </summary>
		[DllImport("user32.dll")]
		private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

		/// <summary>
		/// Creates an almost-transparent UI host with enough rows to reproduce activation refreshes while scrolled down.
		/// </summary>
		[SetUp]
		public void SetUp()
		{
			DateTime baseDate = new DateTime(2020, 1, 1);
			_mods = Enumerable.Range(0, 240)
				.Select(index =>
				{
					var mod = new InstallLog.DummyMod("Mod " + index.ToString("D3"), "mod" + index + ".7z");
					mod.InstallDate = baseDate.AddDays(index).ToString("yyyy-MM-dd");
					mod.DownloadDate = baseDate.AddDays(index);
					mod.LastKnownVersion = "1.0";
					return (IMod)mod;
				})
				.ToList();
			_active = new HashSet<IMod>(_mods.Where((mod, index) => index % 2 == 0));
			_categories = _mods.ToDictionary(mod => mod, mod => "Category");
			_sortNumbers = _mods.ToDictionary(mod => mod, mod => (int?)null);
			Assembly assembly = typeof(ModManager).Assembly;
			Type viewType = assembly.GetType("Nexus.Client.ModManagement.UI.ModCategoryTreeDXControl", true);
			_view = (Control)Activator.CreateInstance(viewType);
			_tree = (TreeList)viewType.GetProperty("TreeList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_view);
			Type surfaceType = assembly.GetType("Nexus.Client.ModManagement.UI.TreeModListSurface", true);
			_surface = Activator.CreateInstance(surfaceType, new object[]
			{
				_view, _mods,
				new Func<IMod, string>(mod => _categories.TryGetValue(mod, out string category) ? category : "Category"),
				new Func<IMod, string>(mod => _active.Contains(mod) ? "Active" : "Uninstalled"),
				new Func<IMod, int?>(mod => _sortNumbers.TryGetValue(mod, out int? number) ? number : null),
				new Func<IMod, bool>(mod => false),
				new Func<IMod, bool>(mod => _active.Contains(mod)),
				new Func<int, int, string>((active, total) => active + "/" + total)
			});
			_host = new Form
			{
				ShowInTaskbar = false,
				StartPosition = FormStartPosition.Manual,
				Location = SystemInformation.WorkingArea.Location,
				ClientSize = new Size(900, 400),
				FormBorderStyle = FormBorderStyle.None,
				Opacity = 0.01
			};
			_view.Dock = DockStyle.Fill;
			_host.Controls.Add(_view);
			_host.Show();
			InvokeSurface("SetMods", _mods);
			_tree.ExpandAll();
			Application.DoEvents();

			EventInfo sorting = viewType.GetEvent("SortingCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
			sorting.GetAddMethod(true).Invoke(_view, new object[] { new EventHandler((sender, args) => _userSortNotifications++) });
			EventInfo navigation = viewType.GetEvent("UserNavigationOccurred", BindingFlags.Instance | BindingFlags.NonPublic);
			navigation.GetAddMethod(true).Invoke(_view, new object[] { new EventHandler((sender, args) => _userNavigationNotifications++) });
			_tree.EndSorting += (sender, args) => _nativeSortNotifications++;
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
			PrepareSort(sortColumn, SortOrder.Ascending);
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
		/// Preserves a non-zero viewport when the final category is collapsed and its hidden children trail the node iterator.
		/// </summary>
		[TestCase(false)]
		[TestCase(true)]
		public void CollapsedTrailingCategoryDoesNotResetPassiveRefreshViewport(bool fullRefresh)
		{
			ConfigureCollapsedTrailingCategory();
			PrepareSort("ModName", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 120;
			Application.DoEvents();
			int topIndex = _tree.TopVisibleNodeIndex;
			Assert.That(topIndex, Is.GreaterThan(0));

			TreeListNode focused = _tree.GetNodeByVisibleIndex(topIndex + 4);
			Assert.That(focused?.Tag, Is.InstanceOf<IMod>());
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);

			ToggleActive(mod);
			if (fullRefresh)
				InvokeSurface("RefreshData");
			else
				InvokeSurface("RefreshMod", mod, "Status");
			Application.DoEvents();

			Assert.That(_tree.TopVisibleNodeIndex, Is.EqualTo(topIndex), "Collapsed descendants must not make viewport clamping resolve to index zero.");
			Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(mod));
		}

		/// <summary>
		/// Keeps structural fallback near the previous viewport when the old top-row identity is removed above a collapsed trailing category.
		/// </summary>
		[Test]
		public void CollapsedTrailingCategoryDoesNotResetStructuralFallbackViewport()
		{
			ConfigureCollapsedTrailingCategory();
			PrepareSort("ModName", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 120;
			Application.DoEvents();
			int topIndex = _tree.TopVisibleNodeIndex;
			TreeListNode topNode = _tree.GetNodeByVisibleIndex(topIndex);
			Assert.That(topNode?.Tag, Is.InstanceOf<IMod>());
			IMod removed = (IMod)topNode.Tag;

			_mods.Remove(removed);
			_active.Remove(removed);
			_categories.Remove(removed);
			InvokeSurface("RemoveMods", (object)new[] { removed });
			Application.DoEvents();

			Assert.That(_tree.TopVisibleNodeIndex, Is.EqualTo(topIndex), "A removed viewport anchor should fall back to the previous displayed-row index, not the top.");
		}

		/// <summary>
		/// Chooses a nearby displayed mod as focus fallback even when the iterator ends on a child hidden by a collapsed category.
		/// </summary>
		[Test]
		public void CollapsedTrailingCategoryUsesNearbyVisibleFocusFallback()
		{
			ConfigureCollapsedTrailingCategory();
			PrepareSort("ModName", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 120;
			Application.DoEvents();
			int topIndex = _tree.TopVisibleNodeIndex;
			TreeListNode focused = _tree.GetNodeByVisibleIndex(topIndex + 5);
			Assert.That(focused?.Tag, Is.InstanceOf<IMod>());
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);

			InvokeSurfaceNonPublic("SetVisibilityPredicate", new Func<IMod, bool>(candidate => !String.Equals(candidate.ModName, "HIDDEN COLLAPSED TEST", StringComparison.Ordinal)));
			((InstallLog.DummyMod)mod).ModName = "HIDDEN COLLAPSED TEST";
			InvokeSurface("RefreshMod", mod, "ModName");
			Application.DoEvents();

			Assert.That(_tree.FocusedNode?.Tag, Is.InstanceOf<IMod>());
			Assert.That(_tree.FocusedNode.Tag, Is.Not.SameAs(mod));
			Assert.That(_tree.GetVisibleIndexByNode(_tree.FocusedNode), Is.GreaterThanOrEqualTo(Math.Max(1, topIndex - 1)), "Fallback focus should remain near the prior viewport rather than collapsing to the first root row.");
		}

		/// <summary>
		/// Verifies that a settled refresh cannot defer another native sort until the user's next focus-changing click.
		/// </summary>
		[TestCase("SortNumber", SortOrder.Ascending)]
		[TestCase("SortNumber", SortOrder.Descending)]
		[TestCase("InstallDate", SortOrder.Ascending)]
		[TestCase("InstallDate", SortOrder.Descending)]
		[TestCase("DownloadDate", SortOrder.Ascending)]
		[TestCase("DownloadDate", SortOrder.Descending)]
		[TestCase("ModName", SortOrder.Ascending)]
		[TestCase("ModName", SortOrder.Descending)]
		[TestCase("Status", SortOrder.Ascending)]
		[TestCase("Status", SortOrder.Descending)]
		public void RefreshThenNativeClickDoesNotTriggerDeferredSort(string sortColumn, SortOrder sortOrder)
		{
			PrepareSort(sortColumn, sortOrder);
			TreeListNode focused = _tree.GetNodeByVisibleIndex(90);
			Assert.That(focused?.Tag, Is.InstanceOf<IMod>());
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			_tree.TopVisibleNodeIndex = 70;
			int preservedTopIndex = _tree.TopVisibleNodeIndex;
			int originalVisibleIndex = _tree.GetVisibleIndexByNode(focused);

			MutateSortedValue(mod, sortColumn, sortOrder);
			InvokeSurface("RefreshMod", mod, sortColumn);
			Application.DoEvents();

			Assert.That(_tree.GetVisibleIndexByNode(focused), Is.Not.EqualTo(originalVisibleIndex), "The changed sorted value must actually move the focused row.");
			Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(mod));
			Assert.That(_tree.TopVisibleNodeIndex, Is.EqualTo(preservedTopIndex), "The passive refresh must restore the numeric viewport after sorting settles.");
			Assert.That(_userSortNotifications, Is.Zero, "A data resort must not be reported as a user sort.");

			_tree.TopVisibleNodeIndex = 130;
			Application.DoEvents();
			int userTopIndex = _tree.TopVisibleNodeIndex;
			TreeListNode clicked = _tree.GetNodeByVisibleIndex(userTopIndex + 4);
			Assert.That(clicked?.Tag, Is.InstanceOf<IMod>());
			Assert.That(clicked.Tag, Is.Not.SameAs(mod));
			Point clickPoint = FindVisiblePoint(clicked, "ModName");

			_nativeSortNotifications = 0;
			_userSortNotifications = 0;
			SendNativeClick(clickPoint);
			Application.DoEvents();

			Assert.That(_nativeSortNotifications, Is.Zero, "The first focus-changing click after refresh must not release a deferred native sort.");
			Assert.That(_userSortNotifications, Is.Zero, "The focus-changing click must not be misclassified as a user sort.");
			Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(clicked.Tag), "Native click focus must remain on the row the user actually clicked.");
			Assert.That(_tree.TopVisibleNodeIndex, Is.EqualTo(userTopIndex), "The click must not undo the user's manual scroll position.");
		}

		/// <summary>
		/// Keeps double-click activation tied to the first native MouseDown target even if the viewport moves before the second click.
		/// </summary>
		[Test]
		public void NativeDoubleClickUsesOriginalMouseDownModAfterViewportMoves()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 80;
			Application.DoEvents();
			TreeListNode firstNode = _tree.GetNodeByVisibleIndex(84);
			Assert.That(firstNode?.Tag, Is.InstanceOf<IMod>());
			IMod expectedMod = (IMod)firstNode.Tag;
			Point firstClickPoint = FindVisiblePoint(firstNode, "ModName");
			IMod requestedMod = null;
			SubscribeModEvent("ModToggleRequested", mod => requestedMod = mod);

			SendNativeClick(firstClickPoint);
			_tree.TopVisibleNodeIndex = 130;
			Application.DoEvents();
			TreeListNode nodeNowUnderPointer = _tree.CalcHitInfo(firstClickPoint).Node;
			Assert.That(nodeNowUnderPointer, Is.Not.SameAs(firstNode), "The forced viewport move must put a different row under the original click point.");
			Assert.That(nodeNowUnderPointer?.Tag, Is.InstanceOf<IMod>());

			SendNativeDoubleClickContinuation(firstClickPoint);
			Application.DoEvents();

			Assert.That(requestedMod, Is.SameAs(expectedMod), "Viewport movement must not retarget activation to the row currently under the pointer.");
		}

		/// <summary>
		/// Keeps Latest-column navigation tied to the initial native MouseDown target even when layout movement occurs before MouseUp.
		/// </summary>
		[Test]
		public void NativeLatestClickUsesOriginalMouseDownModAfterViewportMoves()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 80;
			Application.DoEvents();
			TreeListNode firstNode = _tree.GetNodeByVisibleIndex(84);
			Assert.That(firstNode?.Tag, Is.InstanceOf<IMod>());
			IMod expectedMod = (IMod)firstNode.Tag;
			Point clickPoint = FindVisiblePoint(firstNode, "Latest");
			IMod requestedMod = null;
			SubscribeModEvent("LatestLinkRequested", mod => requestedMod = mod);

			SendNativeMouseDown(clickPoint);
			_tree.TopVisibleNodeIndex = 130;
			Application.DoEvents();
			TreeListNode nodeNowUnderPointer = _tree.CalcHitInfo(clickPoint).Node;
			Assert.That(nodeNowUnderPointer, Is.Not.SameAs(firstNode));
			SendNativeMouseUp(clickPoint);
			Application.DoEvents();

			Assert.That(requestedMod, Is.SameAs(expectedMod), "Latest navigation must use the gesture origin rather than the row under the pointer at MouseUp.");
		}

		/// <summary>
		/// Cancels a captured action target when a structural update removes that target before the gesture completes.
		/// </summary>
		[Test]
		public void RemovedGestureTargetIsNotRetargeted()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 80;
			Application.DoEvents();
			TreeListNode targetNode = _tree.GetNodeByVisibleIndex(84);
			Assert.That(targetNode?.Tag, Is.InstanceOf<IMod>());
			IMod targetMod = (IMod)targetNode.Tag;
			Point clickPoint = FindVisiblePoint(targetNode, "Latest");
			IMod requestedMod = null;
			SubscribeModEvent("LatestLinkRequested", mod => requestedMod = mod);

			SendNativeMouseDown(clickPoint);
			_mods.Remove(targetMod);
			InvokeSurface("RemoveMods", (object)new[] { targetMod });
			Application.DoEvents();
			SendNativeMouseUp(clickPoint);
			Application.DoEvents();

			Assert.That(requestedMod, Is.Null, "A removed gesture target must cancel the action instead of retargeting another row.");
		}

		/// <summary>
		/// Defers disruptive refresh work during inline rename and reconciles the final model state after the editor closes.
		/// </summary>
		[Test]
		public void ActiveRenameDefersRefreshUntilEditorCloses()
		{
			TreeListNode focused = _tree.GetNodeByVisibleIndex(80);
			Assert.That(focused?.Tag, Is.InstanceOf<IMod>());
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			Assert.That((bool)InvokeViewNonPublicResult("BeginInlineRename"), Is.True);
			Assert.That(_tree.ActiveEditor, Is.Not.Null);
			Control editor = _tree.ActiveEditor as Control;
			Assert.That(editor, Is.Not.Null);
			editor.Text = "Pending rename text";
			string pendingText = editor.Text;
			string statusBefore = Convert.ToString(focused.GetValue(StatusFieldName));

			ToggleActive(mod);
			InvokeSurface("RefreshData");
			Application.DoEvents();

			Assert.That(_tree.ActiveEditor, Is.Not.Null, "A programmatic refresh must not close a genuine rename editor.");
			Assert.That(editor.Text, Is.EqualTo(pendingText), "A deferred refresh must not overwrite pending user text.");
			Assert.That(Convert.ToString(focused.GetValue(StatusFieldName)), Is.EqualTo(statusBefore), "The disruptive refresh must remain deferred while the editor is active.");

			_tree.HideEditor();
			DrainPostedCallbacks();
			Assert.That(_tree.ActiveEditor, Is.Null);
			Assert.That(Convert.ToString(focused.GetValue(StatusFieldName)), Is.Not.EqualTo(statusBefore), "The latest model state must be reconciled after editing ends.");
		}

		/// <summary>
		/// Defers disruptive refresh work while the Auto Filter Row owns an editor and reconciles it after editor completion.
		/// </summary>
		[Test]
		public void AutoFilterEditorDefersRefreshUntilEditorCloses()
		{
			IMod mod = _mods[0];
			TreeListNode target = FindVisibleNode(mod);
			Assert.That(target, Is.Not.Null);
			Point filterPoint = FindAutoFilterPoint("ModName");
			SendNativeClick(filterPoint);
			Application.DoEvents();
			if (_tree.ActiveEditor == null)
			{
				_tree.ShowEditor();
				Application.DoEvents();
			}
			Assert.That(_tree.FocusedNode?.Id, Is.EqualTo(TreeList.AutoFilterNodeId));
			Assert.That(_tree.ActiveEditor, Is.Not.Null, "The Auto Filter Row must own an active editor for this regression case.");
			Control editor = _tree.ActiveEditor as Control;
			Assert.That(editor, Is.Not.Null);
			editor.Text = "Mod";
			string pendingText = editor.Text;
			string statusBefore = Convert.ToString(target.GetValue(StatusFieldName));

			ToggleActive(mod);
			InvokeSurface("RefreshData");
			Application.DoEvents();

			Assert.That(_tree.ActiveEditor, Is.Not.Null);
			Assert.That(editor.Text, Is.EqualTo(pendingText), "Filter input must not be overwritten by a background refresh.");
			Assert.That(Convert.ToString(target.GetValue(StatusFieldName)), Is.EqualTo(statusBefore));

			_tree.HideEditor();
			DrainPostedCallbacks();
			TreeListNode current = FindVisibleNode(mod);
			Assert.That(current, Is.Not.Null);
			Assert.That(Convert.ToString(current.GetValue(StatusFieldName)), Is.Not.EqualTo(statusBefore), "Deferred model state must be applied after Auto Filter editing ends.");
		}

		/// <summary>
		/// Restores rows hidden by the previous predicate when the final filter is cleared during an active editor.
		/// </summary>
		[Test]
		public void ClearingLastDeferredFilterRestoresPreviouslyHiddenMods()
		{
			Func<IMod, bool> predicate = mod => mod.ModName.EndsWith("0", StringComparison.Ordinal);
			InvokeSurfaceNonPublic("SetVisibilityPredicate", predicate);
			Application.DoEvents();
			int filteredCount = VisibleModCount();
			Assert.That(filteredCount, Is.GreaterThan(0).And.LessThan(_mods.Count));

			TreeListNode focused = _tree.NodesIterator.Visible.First(node => node?.Tag is IMod);
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			Assert.That((bool)InvokeViewNonPublicResult("BeginInlineRename"), Is.True);
			Assert.That(_tree.ActiveEditor, Is.Not.Null);

			InvokeSurfaceNonPublic("SetVisibilityPredicate", (object)null);
			Application.DoEvents();
			Assert.That(VisibleModCount(), Is.EqualTo(filteredCount), "Visibility changes must stay deferred while editing is active.");

			_tree.HideEditor();
			DrainPostedCallbacks();
			Assert.That(VisibleModCount(), Is.EqualTo(_mods.Count), "Clearing the final deferred filter must explicitly restore every previously hidden mod.");
		}

		/// <summary>
		/// Avoids a recursive TreeList sort when a single refreshed field is not part of the current sort keys.
		/// </summary>
		[Test]
		public void UnsortedSingleModValueRefreshDoesNotSortTree()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			TreeListNode node = _tree.GetNodeByVisibleIndex(80);
			IMod mod = (IMod)node.Tag;
			mod.DownloadDate = new DateTime(2099, 1, 1);
			_nativeSortNotifications = 0;

			InvokeSurface("RefreshMod", mod, "DownloadDate");
			Application.DoEvents();

			Assert.That(_nativeSortNotifications, Is.Zero, "A value change outside the active sort keys must not call BeginSort/EndSort for the whole tree.");
		}

		/// <summary>
		/// Reconciles multiple deferred sorted-value changes in one TreeList sort transaction.
		/// </summary>
		[Test]
		public void DeferredModRefreshesUseSingleSortBatch()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			TreeListNode focused = _tree.GetNodeByVisibleIndex(80);
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			Assert.That((bool)InvokeViewNonPublicResult("BeginInlineRename"), Is.True);

			IMod[] changedMods = _mods.Skip(100).Take(8).ToArray();
			TreeListNode movedNode = FindVisibleNode(changedMods[0]);
			int originalVisibleIndex = _tree.GetVisibleIndexByNode(movedNode);
			for (int index = 0; index < changedMods.Length; index++)
			{
				((InstallLog.DummyMod)changedMods[index]).ModName = "ZZZ Deferred " + index.ToString("D2");
				InvokeSurface("RefreshMod", changedMods[index], "ModName");
			}
			Application.DoEvents();
			Assert.That(_nativeSortNotifications, Is.Zero, "Sorted deferred changes must not sort while the editor is active.");

			_tree.HideEditor();
			_nativeSortNotifications = 0;
			DrainPostedCallbacks();
			Assert.That(_tree.GetVisibleIndexByNode(movedNode), Is.Not.EqualTo(originalVisibleIndex), "The deferred sorted values must actually be reconciled into the visual order.");
			Assert.That(_nativeSortNotifications, Is.EqualTo(1), "All deferred mod changes should share one BeginSort/EndSort transaction.");
		}

		/// <summary>
		/// Keeps install-date focus-top navigation deferred until the active Category View editor and refresh reconciliation are complete.
		/// </summary>
		[TestCase(true)]
		[TestCase(false)]
		public void ManagerInstallDateNavigationWaitsForEditorReconciliation(bool optionEnabled)
		{
			PrepareSort("InstallDate", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 70;
			Application.DoEvents();
			TreeListNode focused = _tree.GetNodeByVisibleIndex(_tree.TopVisibleNodeIndex + 4);
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			Assert.That((bool)InvokeViewNonPublicResult("BeginInlineRename"), Is.True);

			using (Control manager = CreateManagerHarness(optionEnabled, false))
			{
				mod.InstallDate = "2099-12-31";
				InvokeNonPublic(manager, "Mod_PropertyChanged", mod, new PropertyChangedEventArgs("InstallDate"));
				Application.DoEvents();
				Assert.That(_tree.ActiveEditor, Is.Not.Null);
				Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(mod), "Automatic navigation must not interrupt the active editor.");

				_tree.HideEditor();
				DrainPostedCallbacks();
				Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(mod),
					optionEnabled
						? "Enabled install-date navigation should focus the changed mod only after deferred reconciliation."
						: "Disabled install-date navigation must remain disabled after reconciliation.");
			}
		}

		/// <summary>
		/// Reveals an already-focused mod after installation sorting moves it outside the preserved viewport, only when requested.
		/// </summary>
		[TestCase(true, true, SortOrder.Ascending)]
		[TestCase(true, true, SortOrder.Descending)]
		[TestCase(false, true, SortOrder.Descending)]
		[TestCase(true, false, SortOrder.Descending)]
		public void ManagerInstallDateNavigationRevealsAlreadyFocusedMod(bool optionEnabled, bool installDatePrimary, SortOrder sortOrder)
		{
			PrepareSort(installDatePrimary ? "InstallDate" : "ModName", installDatePrimary ? sortOrder : SortOrder.Ascending);
			if (!installDatePrimary)
			{
				_tree.Columns["InstallDate"].SortOrder = sortOrder;
				_tree.Columns["InstallDate"].SortIndex = 1;
			}

			IMod mod = _mods[90];
			TreeListNode node = _tree.NodesIterator.All.First(candidate => ReferenceEquals(candidate.Tag, mod));
			_tree.FocusedNode = node;
			_tree.Selection.Clear();
			_tree.Selection.Add(node);
			_tree.TopVisibleNodeIndex = 130;
			DrainPostedCallbacks();
			int previousTopIndex = _tree.TopVisibleNodeIndex;

			using (Control manager = CreateManagerHarness(optionEnabled, false))
			{
				mod.InstallDate = sortOrder == SortOrder.Ascending ? "1900-01-01" : "2099-12-31";
				InvokeNonPublic(manager, "Mod_PropertyChanged", mod, new PropertyChangedEventArgs("InstallDate"));
				Assert.That(_tree.FocusedNode, Is.SameAs(node));
				Assert.That(_tree.TopVisibleNodeIndex, Is.EqualTo(previousTopIndex), "The refresh must preserve the viewport until explicit navigation runs.");
				DrainPostedCallbacks();

				Assert.That(_tree.FocusedNode, Is.SameAs(node));
				Assert.That(_tree.Selection.Contains(node), Is.True);
				int targetIndex = _tree.GetVisibleIndexByNode(node);
				Assert.That(targetIndex, Is.LessThan(previousTopIndex), "The target must be above the preserved viewport to exercise explicit reveal.");
				Assert.That(_tree.TopVisibleNodeIndex,
					Is.EqualTo(optionEnabled && installDatePrimary ? targetIndex : previousTopIndex),
					"Reveal the changed mod only when the option is enabled and Install Date is the primary sort.");
			}
		}

		/// <summary>
		/// Reveals the changed mod in its own collapsed category instead of searching the first expanded category.
		/// </summary>
		[Test]
		public void ManagerInstallDateNavigationFocusesChangedModInCollapsedCategory()
		{
			for (int index = 0; index < _mods.Count; index++)
				_categories[_mods[index]] = index < 120 ? "A Other" : "Z Target";

			InvokeSurface("SetMods", _mods);
			PrepareSort("InstallDate", SortOrder.Descending);
			IMod mod = _mods[180];
			TreeListNode modNode = _tree.NodesIterator.All.First(node => ReferenceEquals(node?.Tag, mod));
			TreeListNode targetCategory = modNode.ParentNode;
			Assert.That(_tree.Nodes.Count, Is.EqualTo(2));
			TreeListNode otherCategory = ReferenceEquals(_tree.Nodes[0], targetCategory) ? _tree.Nodes[1] : _tree.Nodes[0];
			otherCategory.Expanded = false;
			targetCategory.Expanded = false;
			Application.DoEvents();
			Assert.That(_tree.GetVisibleIndexByNode(modNode), Is.EqualTo(-1), "The target mod must begin inside a collapsed category.");

			using (Control manager = CreateManagerHarness(true, false))
			{
				mod.InstallDate = "2099-12-31";
				InvokeNonPublic(manager, "Mod_PropertyChanged", mod, new PropertyChangedEventArgs("InstallDate"));
				DrainPostedCallbacks();

				Assert.That(targetCategory.Expanded, Is.True, "Install-date navigation must expand the changed mod's category.");
				Assert.That(otherCategory.Expanded, Is.False, "Install-date navigation must not expand an unrelated leading category.");
				Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(mod), "Category View must focus the mod whose install date changed.");
				Assert.That(_tree.GetVisibleIndexByNode(_tree.FocusedNode), Is.GreaterThanOrEqualTo(0));
			}
		}

		/// <summary>
		/// Keeps focus-top-after-sorting navigation deferred until the active Category View editor has closed.
		/// </summary>
		[TestCase(true)]
		[TestCase(false)]
		public void ManagerSortNavigationWaitsForEditorCompletion(bool optionEnabled)
		{
			PrepareSort("ModName", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 70;
			Application.DoEvents();
			TreeListNode focused = _tree.GetNodeByVisibleIndex(_tree.TopVisibleNodeIndex + 4);
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			Assert.That((bool)InvokeViewNonPublicResult("BeginInlineRename"), Is.True);

			using (Control manager = CreateManagerHarness(false, optionEnabled))
			{
				InvokeNonPublic(manager, "ModCategoryTree_SortingCompleted", _view, EventArgs.Empty);
				Application.DoEvents();
				Assert.That(_tree.ActiveEditor, Is.Not.Null);
				Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(mod), "Post-sort navigation must not interrupt the active editor.");

				_tree.HideEditor();
				DrainPostedCallbacks();
				IMod firstVisible = FirstVisibleMod();
				if (optionEnabled)
					Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(firstVisible), "Enabled post-sort navigation should run only after editor completion.");
				else
					Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(mod));
			}
		}

		/// <summary>
		/// Preserves a stable viewport identity and the focused mod when a RefreshMod call reparents that mod.
		/// </summary>
		[Test]
		public void StructuralRefreshReparentsFocusedModWithoutLosingIdentity()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			_tree.TopVisibleNodeIndex = 70;
			Application.DoEvents();
			TreeListNode focused = _tree.GetNodeByVisibleIndex(_tree.TopVisibleNodeIndex + 4);
			Assert.That(focused?.Tag, Is.InstanceOf<IMod>());
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			object topIdentity = _tree.GetNodeByVisibleIndex(_tree.TopVisibleNodeIndex).Tag;
			TreeListNode oldParent = focused.ParentNode;

			_categories[mod] = "Other Category";
			InvokeSurface("RefreshMod", mod, "CategoryId");
			Application.DoEvents();

			TreeListNode current = FindVisibleNode(mod);
			Assert.That(current, Is.Not.Null);
			Assert.That(current.ParentNode, Is.Not.SameAs(oldParent));
			Assert.That(_tree.FocusedNode?.Tag, Is.SameAs(mod));
			Assert.That(_tree.Selection.Cast<TreeListNode>().Select(node => node.Tag), Does.Contain(mod));
			Assert.That(_tree.GetNodeByVisibleIndex(_tree.TopVisibleNodeIndex).Tag, Is.SameAs(topIdentity), "Structural refreshes should keep a stable top identity when it survives.");
			Assert.That(_userSortNotifications, Is.Zero);
		}

		/// <summary>
		/// Chooses a visible deterministic fallback when a value change causes the focused mod to be filtered out.
		/// </summary>
		[Test]
		public void VisibilityChangingRefreshFallsBackWhenFocusedModIsHidden()
		{
			TreeListNode focused = _tree.GetNodeByVisibleIndex(80);
			Assert.That(focused?.Tag, Is.InstanceOf<IMod>());
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			Func<IMod, bool> predicate = candidate => !String.Equals(candidate.ModName, "HIDDEN BY TEST", StringComparison.Ordinal);
			InvokeSurfaceNonPublic("SetVisibilityPredicate", predicate);
			Application.DoEvents();

			((InstallLog.DummyMod)mod).ModName = "HIDDEN BY TEST";
			InvokeSurface("RefreshMod", mod, "ModName");
			Application.DoEvents();

			Assert.That(FindVisibleNode(mod), Is.Null);
			Assert.That(_tree.FocusedNode, Is.Not.Null, "A hidden focused mod should produce a deterministic visible fallback when rows remain.");
			Assert.That(_tree.FocusedNode.Tag, Is.Not.SameAs(mod));
			Assert.That(_tree.GetVisibleIndexByNode(_tree.FocusedNode), Is.GreaterThanOrEqualTo(0));
		}

		/// <summary>
		/// Suppresses programmatic sort changes while still recognizing a later native column-header sort as user intent.
		/// </summary>
		[Test]
		public void SuppressedSortBaselineDoesNotMaskNextNativeUserSort()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			InvokeViewNonPublic("BeginInternalDataUpdate");
			try
			{
				_tree.Columns["DownloadDate"].SortOrder = SortOrder.Descending;
				Application.DoEvents();
			}
			finally
			{
				InvokeViewNonPublic("EndInternalDataUpdate");
			}
			Application.DoEvents();
			Assert.That(_userSortNotifications, Is.Zero, "Suppressed/programmatic sort changes must only synchronize the signature baseline.");

			Point headerPoint = FindColumnHeaderPoint("Status");
			SendNativeClick(headerPoint);
			Application.DoEvents();
			Assert.That(_userSortNotifications, Is.GreaterThan(0), "A later native header sort must still be recognized as user sorting.");
		}

		/// <summary>
		/// Reasserts Category View ownership of sort scrolling after a saved layout has been restored.
		/// </summary>
		[Test]
		public void LayoutRestoreKeepsNativeSortAutoScrollDisabled()
		{
			string layout = (string)InvokeSurfaceNonPublicResult("SaveLayout");
			_tree.OptionsBehavior.AutoScrollOnSorting = true;
			Assert.That(_tree.OptionsBehavior.AutoScrollOnSorting, Is.True);

			InvokeSurfaceNonPublic("RestoreLayout", layout);
			Application.DoEvents();

			Assert.That(_tree.OptionsBehavior.AutoScrollOnSorting, Is.False);
		}

		/// <summary>
		/// Preserves the visible anchor and selection when a downloaded or restored archive is inserted above it.
		/// </summary>
		[Test]
		public void AddingModPreservesViewportAnchorAndSelection()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			TreeListNode focused = _tree.GetNodeByVisibleIndex(83);
			_tree.FocusedNode = focused;
			_tree.Selection.Clear();
			_tree.Selection.Add(focused);
			_tree.TopVisibleNodeIndex = 80;
			TreeListNode topNode = _tree.GetNodeByVisibleIndex(_tree.TopVisibleNodeIndex);
			IMod added = new InstallLog.DummyMod("A newly downloaded mod", "new.7z");
			_mods.Add(added);
			_categories[added] = "Category";
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
			PrepareSort("ModName", SortOrder.Ascending);
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
		/// Suppresses user-navigation notifications during internal viewport restoration while still exposing later external movement.
		/// </summary>
		[Test]
		public void InternalViewportRestoreDoesNotLookLikeUserNavigation()
		{
			PrepareSort("ModName", SortOrder.Ascending);
			TreeListNode focused = _tree.GetNodeByVisibleIndex(90);
			IMod mod = (IMod)focused.Tag;
			_tree.FocusedNode = focused;
			_tree.TopVisibleNodeIndex = 70;
			_userNavigationNotifications = 0;

			((InstallLog.DummyMod)mod).ModName = "ZZZ Navigation Test";
			InvokeSurface("RefreshMod", mod, "ModName");
			Application.DoEvents();
			Assert.That(_userNavigationNotifications, Is.Zero, "NMM's own guarded viewport restore must not invalidate navigation as if it were user input.");

			_tree.TopVisibleNodeIndex = 120;
			Application.DoEvents();
			Assert.That(_userNavigationNotifications, Is.GreaterThan(0), "Viewport movement outside the internal update guard must invalidate stale queued navigation.");
		}

		/// <summary>
		/// Reconciles background changes after a Sort-number editor closes, including a cancelled subsequent editor request.
		/// </summary>
		[TestCase(false)]
		[TestCase(true)]
		public void SortNumberEditorClosureReconcilesDeferredRefresh(bool cancelledNextEditor)
		{
			TreeListNode node = _tree.GetNodeByVisibleIndex(80);
			IMod mod = (IMod)node.Tag;
			_tree.FocusedNode = node;
			_tree.FocusedColumn = _tree.Columns["SortNumber"];
			_tree.ShowEditor();
			Assert.That(_tree.ActiveEditor, Is.Not.Null);
			string oldStatus = Convert.ToString(node.GetValue("ModStatus"));

			ToggleActive(mod);
			InvokeSurface("RefreshData");
			Assert.That(Convert.ToString(node.GetValue("ModStatus")), Is.EqualTo(oldStatus));
			_tree.HideEditor();
			if (cancelledNextEditor)
			{
				_tree.FocusedColumn = _tree.Columns["Author"];
				var args = new CancelEventArgs();
				InvokeViewNonPublic("TreeList_ShowingEditor", _tree, args);
				Assert.That(args.Cancel, Is.True);
			}
			DrainPostedCallbacks();

			Assert.That(Convert.ToString(node.GetValue("ModStatus")), Is.Not.EqualTo(oldStatus));
			Assert.That(_view.GetType().GetProperty("HasActiveEditor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_view), Is.EqualTo(false));
		}

		/// <summary>
		/// Creates a Mod Manager shell wired to this fixture's real Category View surface for navigation coordination tests.
		/// </summary>
		private Control CreateManagerHarness(bool focusAfterInstallDateChange, bool focusAfterSorting)
		{
			Assembly assembly = typeof(ModManager).Assembly;
			Type managerType = assembly.GetType("Nexus.Client.ModManagement.UI.ModManagerDXControl", true);
			Control manager = (Control)Activator.CreateInstance(managerType);
			manager.CreateControl();
			Assert.That(manager.Handle, Is.Not.EqualTo(IntPtr.Zero));

			SetPrivateField(manager, "_modCategoryTreeControl", _view);
			SetPrivateField(manager, "_categoryModListSurface", _surface);
			SetPrivateField(manager, "_activeModListSurface", _surface);
			SetPrivateField(manager, "_focusTopRowAfterInstallDateChange", focusAfterInstallDateChange);
			SetPrivateField(manager, "_focusTopRowAfterSorting", focusAfterSorting);

			FieldInfo modListField = managerType.GetField("_modList", BindingFlags.Instance | BindingFlags.NonPublic);
			List<IMod> managerMods = (List<IMod>)modListField.GetValue(manager);
			managerMods.AddRange(_mods);

			EventInfo reconciled = _surface.GetType().GetEvent("EditReconciliationCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo handlerMethod = managerType.GetMethod("CategoryModListSurface_EditReconciliationCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
			Delegate handler = Delegate.CreateDelegate(reconciled.EventHandlerType, manager, handlerMethod);
			reconciled.GetAddMethod(true).Invoke(_surface, new object[] { handler });
			return manager;
		}

		/// <summary>
		/// Sets one private field on an application control used by a focused integration-style regression test.
		/// </summary>
		private static void SetPrivateField(object target, string fieldName, object value)
		{
			FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, "Missing private test field: " + fieldName);
			field.SetValue(target, value);
		}

		/// <summary>
		/// Invokes a private instance method on an application control.
		/// </summary>
		private static object InvokeNonPublic(object target, string methodName, params object[] args)
		{
			MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(method, Is.Not.Null, "Missing private test method: " + methodName);
			return method.Invoke(target, args);
		}

		/// <summary>
		/// Counts currently visible mod nodes without including category rows.
		/// </summary>
		private int VisibleModCount()
		{
			return _tree.NodesIterator.Visible.Count(node => node?.Tag is IMod);
		}

		/// <summary>
		/// Gets the first visible mod in the TreeList's current visual order.
		/// </summary>
		private IMod FirstVisibleMod()
		{
			return _tree.NodesIterator.Visible.Select(node => node?.Tag as IMod).First(mod => mod != null);
		}

		/// <summary>
		/// Builds two categories with the trailing category collapsed so its child mods are absent from displayed-row indexing.
		/// </summary>
		private void ConfigureCollapsedTrailingCategory()
		{
			for (int index = 0; index < _mods.Count; index++)
				_categories[_mods[index]] = index < 200 ? "A Expanded" : "Z Collapsed";

			InvokeSurface("SetMods", _mods);
			Application.DoEvents();
			Assert.That(_tree.Nodes.Count, Is.EqualTo(2));
			_tree.Nodes[0].Expanded = true;
			_tree.Nodes[1].Expanded = false;
			Application.DoEvents();
			Assert.That(_tree.VisibleNodesCount, Is.GreaterThan(150));

			TreeListNode iteratorLast = _tree.NodesIterator.Visible.LastOrDefault(node => node != null);
			Assert.That(iteratorLast, Is.Not.Null);
			Assert.That(iteratorLast.ParentNode, Is.SameAs(_tree.Nodes[1]), "The regression requires NodesIterator.Visible to trail into the collapsed category.");
			Assert.That(_tree.GetVisibleIndexByNode(iteratorLast), Is.EqualTo(-1), "Collapsed descendants must not have displayed-row indexes.");
		}

		/// <summary>
		/// Applies a sort before the operation under test and drains initialization callbacks.
		/// </summary>
		private void PrepareSort(string fieldName, SortOrder sortOrder)
		{
			_tree.ClearSorting();
			_tree.Columns[ResolveTreeFieldName(fieldName)].SortOrder = sortOrder;
			Application.DoEvents();
			_userSortNotifications = 0;
			_nativeSortNotifications = 0;
			_userNavigationNotifications = 0;
		}

		/// <summary>
		/// Changes the active sort value enough to force the selected mod into a different sorted position.
		/// </summary>
		private void MutateSortedValue(IMod mod, string fieldName, SortOrder sortOrder)
		{
			switch (fieldName)
			{
				case "SortNumber":
					_sortNumbers[mod] = sortOrder == SortOrder.Ascending ? Int32.MinValue : Int32.MaxValue;
					break;
				case "InstallDate":
					mod.InstallDate = sortOrder == SortOrder.Ascending ? "2099-12-31" : "1900-01-01";
					break;
				case "DownloadDate":
					mod.DownloadDate = sortOrder == SortOrder.Ascending ? new DateTime(2099, 12, 31) : new DateTime(1900, 1, 1);
					break;
				case "ModName":
					((InstallLog.DummyMod)mod).ModName = sortOrder == SortOrder.Ascending ? "ZZZ Forced Move" : "AAA Forced Move";
					break;
				case "Status":
					ToggleActive(mod);
					break;
				default:
					Assert.Fail("Unsupported sorted field: " + fieldName);
					break;
			}
		}

		/// <summary>
		/// Toggles the test-only active state used by the Category View status resolver.
		/// </summary>
		private void ToggleActive(IMod mod)
		{
			if (!_active.Remove(mod))
				_active.Add(mod);
		}

		/// <summary>
		/// Finds the visible node currently representing a mod identity.
		/// </summary>
		private TreeListNode FindVisibleNode(IMod mod)
		{
			return _tree.NodesIterator.Visible.FirstOrDefault(node => ReferenceEquals(node?.Tag, mod));
		}

		/// <summary>
		/// Finds a client point whose hit-test resolves to the supplied visible TreeList node and optional column.
		/// </summary>
		private Point FindVisiblePoint(TreeListNode targetNode, string fieldName = null)
		{
			int topVisibleNodeIndex = _tree.TopVisibleNodeIndex;
			_tree.Focus();
			Application.DoEvents();
			_tree.TopVisibleNodeIndex = topVisibleNodeIndex;
			Application.DoEvents();

			string resolvedFieldName = ResolveTreeFieldName(fieldName);
			if (resolvedFieldName != null)
			{
				var column = _tree.Columns[resolvedFieldName];
				Assert.That(column, Is.Not.Null, "Missing TreeList column: " + resolvedFieldName);
				_tree.MakeColumnVisible(column);
			}
			_tree.Refresh();
			Application.DoEvents();

			for (int y = 0; y < _tree.ClientSize.Height; y++)
			{
				for (int x = 0; x < _tree.ClientSize.Width; x += 2)
				{
					Point point = new Point(x, y);
					TreeListHitInfo hitInfo = _tree.CalcHitInfo(point);
					if (IsSameTreeNode(hitInfo.Node, targetNode) &&
						(resolvedFieldName == null || String.Equals(hitInfo.Column?.FieldName, resolvedFieldName, StringComparison.Ordinal)))
						return point;
				}
			}

			Assert.Fail("Could not find a visible hit-test point for the requested TreeList node/column.");
			return Point.Empty;
		}

		/// <summary>
		/// Finds a native column-header point for the requested TreeList field.
		/// </summary>
		private Point FindColumnHeaderPoint(string fieldName)
		{
			string resolvedFieldName = ResolveTreeFieldName(fieldName);
			var column = _tree.Columns[resolvedFieldName];
			Assert.That(column, Is.Not.Null, "Missing TreeList column: " + resolvedFieldName);
			_tree.MakeColumnVisible(column);
			_tree.Update();
			for (int y = 0; y < Math.Min(80, _tree.ClientSize.Height); y++)
			{
				for (int x = 0; x < _tree.ClientSize.Width; x += 2)
				{
					Point point = new Point(x, y);
					TreeListHitInfo hitInfo = _tree.CalcHitInfo(point);
					if (hitInfo.Node == null && String.Equals(hitInfo.Column?.FieldName, resolvedFieldName, StringComparison.Ordinal))
						return point;
				}
			}

			Assert.Fail("Could not find the requested TreeList column header.");
			return Point.Empty;
		}

		/// <summary>
		/// Finds the requested Auto Filter Row cell using DevExpress' well-known AutoFilter node identifier.
		/// </summary>
		private Point FindAutoFilterPoint(string fieldName)
		{
			_tree.Focus();
			Application.DoEvents();

			string resolvedFieldName = ResolveTreeFieldName(fieldName);
			var column = _tree.Columns[resolvedFieldName];
			Assert.That(column, Is.Not.Null, "Missing TreeList column: " + resolvedFieldName);
			_tree.MakeColumnVisible(column);
			_tree.Refresh();
			Application.DoEvents();
			for (int y = 0; y < Math.Min(120, _tree.ClientSize.Height); y++)
			{
				for (int x = 0; x < _tree.ClientSize.Width; x += 2)
				{
					Point point = new Point(x, y);
					TreeListHitInfo hitInfo = _tree.CalcHitInfo(point);
					if (hitInfo.Node != null && hitInfo.Node.Id == TreeList.AutoFilterNodeId &&
						String.Equals(hitInfo.Column?.FieldName, resolvedFieldName, StringComparison.Ordinal))
						return point;
				}
			}

			Assert.Fail("Could not find the requested Auto Filter Row cell.");
			return Point.Empty;
		}

		/// <summary>
		/// Resolves logical test column names to the current unbound Category Tree field names.
		/// </summary>
		private static string ResolveTreeFieldName(string fieldName)
		{
			switch (fieldName)
			{
				case "Status": return StatusFieldName;
				case "Latest": return LatestFieldName;
				default: return fieldName;
			}
		}

		/// <summary>
		/// Compares TreeList nodes by stable row identity so hit testing does not depend on DevExpress returning the same node wrapper instance.
		/// </summary>
		private static bool IsSameTreeNode(TreeListNode first, TreeListNode second)
		{
			if (ReferenceEquals(first, second)) return true;
			if (first == null || second == null) return false;
			if (first.Tag != null || second.Tag != null)
				return ReferenceEquals(first.Tag, second.Tag);
			return first.Id == second.Id;
		}

		/// <summary>
		/// Subscribes to one internal mod-target event without exposing application internals to the test assembly.
		/// </summary>
		private void SubscribeModEvent(string eventName, Action<IMod> callback)
		{
			EventInfo eventInfo = _view.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
			EventHandler<ModEventArgs> handler = (sender, args) => callback(args.Mod);
			eventInfo.GetAddMethod(true).Invoke(_view, new object[] { handler });
		}

		/// <summary>
		/// Sends a complete native left-click sequence to the TreeList.
		/// </summary>
		private void SendNativeClick(Point point)
		{
			SendNativeMouseDown(point);
			SendNativeMouseUp(point);
		}

		/// <summary>
		/// Sends the first half of a native left-click sequence to the TreeList.
		/// </summary>
		private void SendNativeMouseDown(Point point)
		{
			SendMessage(_tree.Handle, WmLButtonDown, new IntPtr(MkLButton), MakeLParam(point));
		}

		/// <summary>
		/// Sends the MouseUp half of a native left-click sequence to the TreeList.
		/// </summary>
		private void SendNativeMouseUp(Point point)
		{
			SendMessage(_tree.Handle, WmLButtonUp, IntPtr.Zero, MakeLParam(point));
		}

		/// <summary>
		/// Completes the second click of a native double-click without replacing the first gesture target.
		/// </summary>
		private void SendNativeDoubleClickContinuation(Point point)
		{
			SendMessage(_tree.Handle, WmLButtonDoubleClick, new IntPtr(MkLButton), MakeLParam(point));
			SendNativeMouseUp(point);
		}

		/// <summary>
		/// Encodes TreeList client coordinates in the LPARAM format used by Windows mouse messages.
		/// </summary>
		private static IntPtr MakeLParam(Point point)
		{
			int value = (point.Y << 16) | (point.X & 0xFFFF);
			return new IntPtr(value);
		}

		/// <summary>
		/// Drains posted BeginInvoke work used by deferred editor reconciliation.
		/// </summary>
		private void DrainPostedCallbacks()
		{
			const int maxMessagePumpPasses = 50;
			PropertyInfo pendingProperty = _surface.GetType().GetProperty("HasPendingDeferredUpdates", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(pendingProperty, Is.Not.Null);

			for (int pass = 0; pass < maxMessagePumpPasses; pass++)
			{
				Application.DoEvents();
				bool pending = (bool)pendingProperty.GetValue(_surface, null);
				if (!pending)
				{
					// Give manager-owned callbacks posted by EditReconciliationCompleted one final turn.
					Application.DoEvents();
					return;
				}

				Thread.Sleep(1);
			}

			Assert.Fail("Deferred Category View reconciliation did not settle after the editor closed.");
		}

		/// <summary>
		/// Invokes a public production surface method without exposing application internals for tests.
		/// </summary>
		private void InvokeSurface(string methodName, params object[] arguments)
		{
			_surface.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public).Invoke(_surface, arguments);
		}

		/// <summary>
		/// Invokes a non-public production surface method used by Category View lifecycle regression cases.
		/// </summary>
		private void InvokeSurfaceNonPublic(string methodName, params object[] arguments)
		{
			InvokeSurfaceNonPublicResult(methodName, arguments);
		}

		/// <summary>
		/// Invokes and returns the result of a non-public production surface method.
		/// </summary>
		private object InvokeSurfaceNonPublicResult(string methodName, params object[] arguments)
		{
			MethodInfo method = _surface.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
				.Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
			return method.Invoke(_surface, arguments);
		}

		/// <summary>
		/// Invokes a private Category View interaction/lifecycle method without exposing it to production callers.
		/// </summary>
		private void InvokeViewNonPublic(string methodName, params object[] arguments)
		{
			InvokeViewNonPublicResult(methodName, arguments);
		}

		/// <summary>
		/// Invokes and returns the result of a private Category View interaction/lifecycle method.
		/// </summary>
		private object InvokeViewNonPublicResult(string methodName, params object[] arguments)
		{
			MethodInfo method = _view.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
				.Single(candidate => candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
			return method.Invoke(_view, arguments);
		}
	}
}
