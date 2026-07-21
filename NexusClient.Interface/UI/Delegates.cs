
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
	/// <param name="p_vmgMessage">The properties of the message to display.</param>
	/// <returns>The return value of the displayed message.</returns>
	public delegate object ShowMessageDelegate(ViewMessage p_vmgMessage);

	/// <summary>
	/// Confirms an action.
	/// </summary>
	/// <param name="p_strMessage">The message describing the action to confirm.</param>
	/// <param name="p_strTitle">The title of the action to confirm.</param>
	/// <returns><c>true</c> if the action has been confirmed;
	/// <c>false</c> otherwise.</returns>
	public delegate bool ConfirmActionMethod(string p_strMessage, string p_strTitle);
}
