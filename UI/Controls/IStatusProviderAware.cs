using System;
using System.Windows.Forms;

namespace Nexus.UI.Controls
{
	/// <summary>
	/// An interface that allows a control to specify where to display the icon
	/// of a <see cref="SiteStatusProvider"/>.
	/// </summary>
	public interface IStatusProviderAware
	{
		/// <summary>
		/// Gets the child control next to which to display the icon.
		/// </summary>
		/// <value>The child control next to which to display the icon.</value>
		Control StatusProviderSite
		{
			get;
		}
	}
}
