using System;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A button with a space for a status indicator next to it.
	/// </summary>
	public class StatusButton : Control, IStatusProviderAware
	{
		private Button m_butButton = new Button();
		private Panel m_pnlSpacer = new Panel();

		#region Properties

		/// <summary>
		/// Gets the button of the control.
		/// </summary>
		/// <value>The button of the control.</value>
		internal Button Button
		{
			get
			{
				return m_butButton;
			}
		}

		/// <summary>
		/// Gets or sets the text of the button.
		/// </summary>
		/// <value>The text of the button.</value>
		public override string Text
		{
			get
			{
				return m_butButton.Text;
			}
			set
			{
				m_butButton.Text = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public StatusButton()
		{
			m_butButton.SizeChanged += new EventHandler(m_butButton_SizeChanged);

			m_butButton.Click += new EventHandler(m_butButton_Click);
			m_butButton.Dock = DockStyle.Fill;

			m_pnlSpacer.Dock = DockStyle.Right;

			this.Height = m_butButton.PreferredSize.Height;
			this.Width = m_butButton.PreferredSize.Width + m_pnlSpacer.Width;

			Controls.Add(m_butButton);
			Controls.Add(m_pnlSpacer);
		}

		#endregion

		/// <summary>
		/// Hanldes the <see cref="Control.Click"/> even of the button portion of the control.
		/// </summary>
		/// <remarks>
		/// This raise the click event of this control, so that consumers of this control
		/// will see the event coming from this parant control.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void m_butButton_Click(object sender, EventArgs e)
		{
			OnClick(e);
		}

		private void m_butButton_SizeChanged(object sender, EventArgs e)
		{
			this.Height = m_butButton.PreferredSize.Height;
			m_pnlSpacer.Width = Height;
			this.Width = m_butButton.PreferredSize.Width + m_pnlSpacer.Width;			
		}

		#region IStatusProviderAware Members

		/// <summary>
		/// Gets the button upon which to display status message from <see cref="SiteStatusProvider"/>s.
		/// </summary>
		/// <value>The button upon which to display status message from <see cref="SiteStatusProvider"/>s.</value>
		public Control StatusProviderSite
		{

			get
			{
				return Button;
			}
		}

		#endregion
	}
}
