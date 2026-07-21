using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// A normal <see cref="ListView"/>, but with double buffering enabled.
	/// </summary>
	public class DoubleBufferedListView : ListView
	{
		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public DoubleBufferedListView()
			: base()
		{
			this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
		}

		#endregion
	}
}
