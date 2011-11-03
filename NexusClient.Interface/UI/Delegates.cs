
namespace Nexus.Client.UI
{
	/// <summary>
	/// Displays a view.
	/// </summary>
	/// <param name="p_vewView">The view to display.</param>
	/// <param name="p_booModal">Wheher the view should be modal.</param>
	/// <returns>The return value of the displayed view.</returns>
	public delegate object ShowViewDelegate(IView p_vewView, bool p_booModal);

	/// <summary>
	/// Displays a message.
	/// </summary>
	/// <param name="p_vmgMessage">The properties of the message to dislpay.</param>
	/// <returns>The return value of the displayed message.</returns>
	public delegate object ShowMessageDelegate(ViewMessage p_vmgMessage);
}
