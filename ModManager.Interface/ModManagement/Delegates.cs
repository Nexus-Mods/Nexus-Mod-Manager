
using Nexus.Client.Mods;
namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// This asks the user to confirm the overwriting of the specified item.
	/// </summary>
	/// <param name="p_strItemMessage">The message describing the item being overwritten..</param>
	/// <param name="p_booAllowPerGroupChoice">Whether to allow the user to make the decision to make the selection for all items in the current item's group.</param>
	/// <param name="p_booAllowPerModChoice">Whether to allow the user to make the decision to make the selection for all items in the current Mod.</param>
	/// <returns>The user's choice.</returns>
	public delegate OverwriteResult ConfirmItemOverwriteDelegate(string p_strItemMessage, bool p_booAllowPerGroupChoice, bool p_booAllowPerModChoice);

	/// <summary>
	/// This asks the use to confirm the upgrading of the given old mod to the given new mod.
	/// </summary>
	/// <param name="p_modOld">The old mod to be upgrade to the new mod.</param>
	/// <param name="p_modNew">The new mod to which to upgrade from the old.</param>
	/// <returns>The user's choice.</returns>
	public delegate ConfirmUpgradeResult ConfirmModUpgradeDelegate(IMod p_modOld, IMod p_modNew);
}
