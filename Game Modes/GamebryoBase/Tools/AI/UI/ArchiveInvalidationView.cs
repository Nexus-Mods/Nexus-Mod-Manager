using System.Windows.Forms;
using Nexus.Client.Games.Tools;
using Nexus.UI.Controls;
using System.ComponentModel;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.Gamebryo.Tools.AI.UI
{
	/// <summary>
	/// The view used to display ArchiveInvalidation UI.
	/// </summary>
	/// <remarks>
	/// This form is always invisible. It is used to show update
	/// and confirmations.
	/// </remarks>
	public class ArchiveInvalidationView : Form, IToolView
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_aitArchiveInvalidation">The <see cref="ArchiveInvalidationBase"/> tool
		/// for which we are presenting a UI.</param>
		public ArchiveInvalidationView(ArchiveInvalidationBase p_aitArchiveInvalidation)
		{
			p_aitArchiveInvalidation.ConfirmAiEnabling = ConfirmAiEnabling;
			p_aitArchiveInvalidation.ConfirmAiDisabling = ConfirmAiDisabling;
		}

		#endregion

		/// <summary>
		/// Sets the visibility of the form.
		/// </summary>
		/// <remarks>
		/// This ensures the form is never visible.
		/// </remarks>
		/// <param name="value">Whether the form should be visible. Always ignored.</param>
		protected override void SetVisibleCore(bool value)
		{
			base.SetVisibleCore(false);
		}

		/// <summary>
		/// Raises the <see cref="Form.Closing"/> event.
		/// </summary>
		/// <remarks>
		/// This cancels the closing event, and hides the form instead. This is done as
		/// this form should always be reused.
		/// </remarks>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		protected override void OnClosing(CancelEventArgs e)
		{
			//TODO at first glance this form seems like it can never close
			// look into it
			e.Cancel = true;
			base.OnClosing(e);
			Visible = false;
		}

		/// <summary>
		/// Confirms that the user wishes to enable AI.
		/// </summary>
		/// <returns><c>true</c> if the user wishes to enable AI.</returns>
		protected bool ConfirmAiEnabling()
		{
			return (ExtendedMessageBox.Show(this, LanguageManager.Get("GameModes.ArchiveInvalidation.ConfirmApply", "Apply archive invalidation?"), LanguageManager.Get("GameModes.ArchiveInvalidation.ShortTitle", "AI"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
		}

		/// <summary>
		/// Confirms that the user wishes to disable AI.
		/// </summary>
		/// <returns><c>true</c> if the user wishes to disable AI.</returns>
		protected bool ConfirmAiDisabling()
		{
			return (ExtendedMessageBox.Show(this, LanguageManager.Get("GameModes.ArchiveInvalidation.ConfirmRemove", "Remove archive invalidation?"), LanguageManager.Get("GameModes.ArchiveInvalidation.ShortTitle", "AI"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
		}
	}
}
