using System.Drawing;
using System.IO;

namespace Nexus.Client.Games.XRebirth
{
	/// <summary>
	/// Provides common information about XRebirth based games.
	/// </summary>
	public class XRebirthGameModeDescriptor : GameModeDescriptorBase
	{
		private static readonly string[] EXECUTABLES = { "XRebirth.exe" };
		private const string MODE_ID = "XRebirth";

		#region Properties

		/// <summary>
		/// Gets the directory where XRebirth plugins are installed.
		/// </summary>
		/// <value>The directory where XRebirth plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				return null;
			}
		}

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "X Rebirth";
			}
		}

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public override string ModeId
		{
			get
			{
				return MODE_ID;
			}
		}

		/// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		public override string[] GameExecutables
		{
			get
			{
				return EXECUTABLES;
			}
		}

		/// <summary>
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public override Theme ModeTheme
		{
			get
			{
				return new Theme(Properties.Resources.wot_logo, Color.FromArgb(80, 45, 23), null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public XRebirthGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
