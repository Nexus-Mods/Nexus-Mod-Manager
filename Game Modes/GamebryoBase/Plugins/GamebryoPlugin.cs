using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.Games.Gamebryo.Plugins
{
	/// <summary>
	/// Encapsulates the information about a Gamebryo based game plugin.
	/// </summary>
	public class GamebryoPlugin : Plugin
	{
		#region Properties

		/// <summary>
		/// Gets whether the plugin is a master file.
		/// </summary>
		/// <value>Whether the plugin is a master file.</value>
		public bool IsMaster { get; private set; }

		/// <summary>
		/// Gets whether the plugin is a light master file.
		/// </summary>
		/// <value>Whether the plugin is a light master file.</value>
		public bool IsLightMaster { get; private set; }

        /// Gets whether the plugin ignores indexing.
        /// </summary>
        /// <value>Whether the plugin ignores indexing.</value>
        public override bool IgnoreIndexing
        {
            get
            {
                return Metadata != null && Metadata.AddressClass != PluginAddressClass.Full;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_strPath">The filename of the plugin.</param>
        /// <param name="p_strDescription">The description of the plugin.</param>
        /// <param name="p_imgPicture">The picture of the plugin.</param>
        /// <param name="p_booIsMaster">Whether the plugin is a master file.</param>
        /// <param name="p_booIsLightMaster">Whether the plugin is a light master file.</param>
        public GamebryoPlugin(string p_strPath, string p_strDescription, Image p_imgPicture, bool p_booIsMaster, bool p_booIsLightMaster)
			: this(p_strPath, p_strDescription, p_imgPicture, p_booIsMaster, p_booIsLightMaster, 0, null)
		{
		}

		public GamebryoPlugin(string p_strPath, string p_strDescription, Image p_imgPicture, bool p_booIsMaster, bool p_booIsLightMaster, int p_intFormVersion, IList<string> p_lstMasters)
			: this(p_strPath, p_strDescription, p_imgPicture, new PluginMetadata(Path.GetExtension(p_strPath), BuildLegacyHeaderFlags(p_booIsMaster, p_booIsLightMaster), p_intFormVersion, p_lstMasters ?? new List<string>(), p_booIsMaster, p_booIsLightMaster ? PluginAddressClass.Light : PluginAddressClass.Full, PluginSpecialFlags.None, PluginParseStatus.Parsed))
		{
		}

		public GamebryoPlugin(string p_strPath, string p_strDescription, Image p_imgPicture, PluginMetadata p_pmdMetadata)
			: base(p_strPath, p_strDescription, p_imgPicture)
		{
			SetMetadata(p_pmdMetadata);
			IsMaster = Metadata.EffectiveMaster;
			IsLightMaster = Metadata.AddressClass == PluginAddressClass.Light;
		}

		private static PluginHeaderFlags BuildLegacyHeaderFlags(bool p_booIsMaster, bool p_booIsLightMaster)
		{
			PluginHeaderFlags flags = PluginHeaderFlags.None;
			if (p_booIsMaster)
				flags |= PluginHeaderFlags.Master;
			if (p_booIsLightMaster)
				flags |= PluginHeaderFlags.Light;
			return flags;
		}

		#endregion
	}
}
