
namespace Nexus.Client.Util
{
	/// <summary>
	/// Confirms an action.
	/// </summary>
	/// <param name="p_booRememberSelection">Whether the choice should be remembered.</param>
	/// <returns><c>true</c> if the action has been confirmed;
	/// <c>false</c> otherwise.</returns>
	public delegate bool ConfirmRememberedActionMethod(out bool p_booRememberSelection);
}
