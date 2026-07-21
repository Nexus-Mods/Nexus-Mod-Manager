using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Windows.Forms.Design;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// The designer that controls how the <see cref="DropDownTabControl"/> behaves
	/// and is designed.
	/// </summary>
	public class DropDownTabControlDesigner : ParentControlDesigner
	{
		private DesignerVerbCollection m_dvcVerbs = new DesignerVerbCollection();
		private IDesignerHost m_dhtDesignerHost = null;
		private ISelectionService m_slsSelectionService = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="DropDownTabControl"/> being designed.
		/// </summary>
		/// <value>The <see cref="DropDownTabControl"/> being designed.</value>
		protected DropDownTabControl DesignedTabControl
		{
			get
			{
				return (DropDownTabControl)Control;
			}
		}

		/// <summary>
		/// Gets the design verbs implemented by this designer.
		/// </summary>
		/// <value>The design verbs implemented by this designer.</value>
		public override DesignerVerbCollection Verbs
		{
			get
			{
				EnableVerbs();
				return m_dvcVerbs;
			}
		}

		/// <summary>
		/// Gets the designer host.
		/// </summary>
		/// <value>The designer host.</value>
		public IDesignerHost DesignerHost
		{
			get
			{
				if (m_dhtDesignerHost == null)
					m_dhtDesignerHost = (IDesignerHost)GetService(typeof(IDesignerHost));
				return m_dhtDesignerHost;
			}
		}

		/// <summary>
		/// Gets the selection service.
		/// </summary>
		/// <value>The selection service.</value>
		public ISelectionService SelectionService
		{
			get
			{
				if (m_slsSelectionService == null)
					m_slsSelectionService = (ISelectionService)GetService(typeof(ISelectionService));
				return m_slsSelectionService;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public DropDownTabControlDesigner()
			: base()
		{
			DesignerVerb dvbAddPage = new DesignerVerb("Add Tab Page", new EventHandler(AddTabPage));
			DesignerVerb dvbRemovePage = new DesignerVerb("Remove Tab Page", new EventHandler(RemoveTabPage));
			m_dvcVerbs.AddRange(new DesignerVerb[] { dvbAddPage, dvbRemovePage });
		}

		#endregion

		/// <summary>
		/// Enables or disables verbs dependent upon the current state of the control.
		/// </summary>
		protected void EnableVerbs()
		{
			m_dvcVerbs[1].Enabled = DesignedTabControl.TabPages.Count > 0;
		}

		/// <summary>
		/// The event handler for the "Add Tab Page" verb.
		/// </summary>
		/// <remarks>
		/// Adds a new tab page to the control.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void AddTabPage(object sender, EventArgs e)
		{
			DropDownTabControl.TabPageCollection tpcOldPages = DesignedTabControl.TabPages;

			RaiseComponentChanging(TypeDescriptor.GetProperties(DesignedTabControl)["TabPages"]);

			DropDownTabPage tpgPage = (DropDownTabPage)DesignerHost.CreateComponent(typeof(DropDownTabPage));
			tpgPage.Text = tpgPage.Name;
			tpgPage.BackColor = Color.FromKnownColor(KnownColor.Control);
			DesignedTabControl.TabPages.Add(tpgPage);

			RaiseComponentChanged(TypeDescriptor.GetProperties(DesignedTabControl)["TabPages"], tpcOldPages, DesignedTabControl.TabPages);

			DesignedTabControl.SelectedTabPage = tpgPage;
			EnableVerbs();
		}


		/// <summary>
		/// The event handler for the "Remove Tab Page" verb.
		/// </summary>
		/// <remarks>
		/// Removes the current tab page from the control.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void RemoveTabPage(object sender, EventArgs e)
		{
			if (DesignedTabControl.SelectedIndex < 0)
				return;

			DropDownTabControl.TabPageCollection tpcOldPages = DesignedTabControl.TabPages;

			RaiseComponentChanging(TypeDescriptor.GetProperties(DesignedTabControl)["TabPages"]);
			DesignerHost.DestroyComponent((DropDownTabPage)(DesignedTabControl.TabPages[DesignedTabControl.SelectedIndex]));
			RaiseComponentChanged(TypeDescriptor.GetProperties(DesignedTabControl)["TabPages"], tpcOldPages, DesignedTabControl.TabPages);

			SelectionService.SetSelectedComponents(new IComponent[] { DesignedTabControl }, SelectionTypes.Auto);
			EnableVerbs();
		}

		/// <summary>
		/// Determines of the control should respond to a mouse click.
		/// </summary>
		/// <param name="point">The point where the mouse was clicked.</param>
		/// <returns><c>true</c> if the designed control should process the mouse click;
		/// <c>false</c> otherwise.</returns>
		protected override bool GetHitTest(Point point)
		{
			DropDownTabControl ddtTabControl = (DropDownTabControl)Control;
			return ddtTabControl.TabSelector.ClientRectangle.Contains(ddtTabControl.TabSelector.PointToClient(point));
		}

		/// <summary>
		/// Adds default tag pages to a new <see cref="DropDownTabControl"/>.
		/// </summary>
		/// <param name="defaultValues">The values with which to instantiate the control.</param>
		public override void InitializeNewComponent(IDictionary defaultValues)
		{
			base.InitializeNewComponent(defaultValues);

			DropDownTabPage tpgPage = (DropDownTabPage)DesignerHost.CreateComponent(typeof(DropDownTabPage));
			tpgPage.Text = tpgPage.Name;
			tpgPage.BackColor = Color.FromKnownColor(KnownColor.Control);
			DesignedTabControl.TabPages.Add(tpgPage);

			tpgPage = (DropDownTabPage)DesignerHost.CreateComponent(typeof(DropDownTabPage));
			tpgPage.Text = tpgPage.Name;
			tpgPage.BackColor = Color.FromKnownColor(KnownColor.Control);
			DesignedTabControl.TabPages.Add(tpgPage);

			DesignedTabControl.SelectedIndex = 0;
		}
	}
}
