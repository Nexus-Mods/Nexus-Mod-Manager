using System;
using ChinhDo.Transactions;
using Nexus.Client.Games.Gamebryo.Tools.Shader;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games.Gamebryo.ModManagement
{
	/// <summary>
	/// Installs values that are specific to a game mode.
	/// </summary>
	public class GamebryoGameSpecificValueInstaller : IGameSpecificValueInstaller
	{
		/// <summary>
		/// Describes an edit made to a shader.
		/// </summary>
		public class ShaderEdit
		{
			#region Properties

			/// <summary>
			/// Get the id of the package containing the edited shader.
			/// </summary>
			/// <value>The id of the package containing the edited shader.</value>
			public Int32 Package { get; private set; }

			/// <summary>
			/// Gets the name of the edited shader.
			/// </summary>
			/// <value>The name of the edited shader.</value>
			public string ShaderName { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_intPackage">The id of the package containing the edited shader.</param>
			/// <param name="p_strName">The name of the edited shader.</param>
			public ShaderEdit(Int32 p_intPackage, string p_strName)
			{
				Package = p_intPackage;
				ShaderName = p_strName;
			}

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_strKey">The identiying the edited shader.</param>
			public ShaderEdit(string p_strKey)
			{
				string[] strInfo = p_strKey.Split(':', '/');
				Package = Int32.Parse(strInfo[1]);
				ShaderName = strInfo[2];
			}

			#endregion

			/// <summary>
			/// Returns a string representation of the shader that was edited.
			/// </summary>
			/// <remarks>
			/// The returned string is an URI uniquely describing the edited shader and package.
			/// </remarks>
			/// <returns>A string representation of the shader that was edited.</returns>
			public override string ToString()
			{
				return String.Format("sdp:{0}/{1}", Package, ShaderName);
			}
		}

		private bool m_booDontOverwriteAll = false;
		private bool m_booOverwriteAll = false;
		private ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmationDelegate = null;

		#region Properties

		/// <summary>
		/// Gets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected IMod Mod { get; private set; }

		/// <summary>
		/// Gets the environment info of the current game mode.
		/// </summary>
		/// <value>The environment info of the current game mode.</value>
		protected IGameModeEnvironmentInfo GameModeInfo { get; private set; }

		/// <summary>
		/// Gets or sets the transactional file manager to use to interact with the file system.
		/// </summary>
		/// <value>The transactional file manager to use to interact with the file system.</value>
		protected TxFileManager TransactionalFileManager { get; set; }

		/// <summary>
		/// Gets or sets the install log to use to log file installations.
		/// </summary>
		/// <value>The install log to use to log file installations.</value>
		protected IInstallLog InstallLog { get; set; }

		/// <summary>
		/// Gets or sets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		protected FileUtil FileUtility { get; set; }

		/// <summary>
		/// Gets the set of files that have been edited.
		/// </summary>
		protected Set<string> TouchedFiles { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmiGameModeInfo">The environment info of the current game mode.</param>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log file installations.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public GamebryoGameSpecificValueInstaller(IMod p_modMod, IGameModeEnvironmentInfo p_gmiGameModeInfo, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			TouchedFiles = new Set<string>(StringComparer.OrdinalIgnoreCase);
			Mod = p_modMod;
			GameModeInfo = p_gmiGameModeInfo;
			InstallLog = p_ilgInstallLog;
			TransactionalFileManager = p_tfmFileManager;
			FileUtility = p_futFileUtility;
			m_dlgOverwriteConfirmationDelegate = p_dlgOverwriteConfirmationDelegate;
		}

		#endregion

		/// <summary>
		/// Edits the specified game specific value.
		/// </summary>
		/// <param name="p_strKey">The key of the edited Game Specific Value.</param>
		/// <param name="p_bteValue">The value to install.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c> otherwise.</returns>
		public virtual bool EditGameSpecificValue(string p_strKey, byte[] p_bteValue)
		{
			if (m_booDontOverwriteAll)
				return false;

			ShaderEdit sedShader = new ShaderEdit(p_strKey);
			SDPArchives sdpManager = new SDPArchives(GameModeInfo, FileUtility);
			if (!TouchedFiles.Contains(sdpManager.GetPath(sedShader.Package)))
			{
				TouchedFiles.Add(sdpManager.GetPath(sedShader.Package));
				TransactionalFileManager.Snapshot(sdpManager.GetPath(sedShader.Package));
			}

			IMod modOldMod = InstallLog.GetCurrentGameSpecificValueEditOwner(p_strKey);
			if (!m_booOverwriteAll && (modOldMod != null))
			{
				string strMessage = String.Format("Shader '{0}' in package '{1}' has already been overwritten by '{2}'\n" +
										"Overwrite the changes?", sedShader.ShaderName, sedShader.Package, modOldMod.ModName);
				switch (m_dlgOverwriteConfirmationDelegate(strMessage, false, false))
				{
					case OverwriteResult.YesToAll:
						m_booOverwriteAll = true;
						break;
					case OverwriteResult.NoToAll:
						m_booDontOverwriteAll = true;
						break;
					case OverwriteResult.Yes:
						break;
					default:
						return false;
				}
			}

			byte[] oldData;
			if (!sdpManager.EditShader(sedShader.Package, sedShader.ShaderName, p_bteValue, out oldData))
				throw new Exception("Failed to edit the shader");

			//if we are overwriting an original shader, back it up
			if ((modOldMod == null) && (oldData != null))
				InstallLog.LogOriginalGameSpecificValue(p_strKey, oldData);

			InstallLog.AddGameSpecificValueEdit(Mod, p_strKey, p_bteValue);
			return true;
		}

		/// <summary>
		/// Undoes the edit made to the specified game specific value.
		/// </summary>
		/// <param name="p_strKey">The key of the edited Game Specific Value.</param>
		public void UnEditGameSpecificValue(string p_strKey)
		{
			ShaderEdit sedShader = new ShaderEdit(p_strKey);
			SDPArchives sdpManager = new SDPArchives(GameModeInfo, FileUtility);
			if (!TouchedFiles.Contains(sdpManager.GetPath(sedShader.Package)))
			{
				TouchedFiles.Add(sdpManager.GetPath(sedShader.Package));
				TransactionalFileManager.Snapshot(sdpManager.GetPath(sedShader.Package));
			}

			string strKey = InstallLog.GetModKey(Mod);
			string strCurrentOwnerKey = InstallLog.GetCurrentGameSpecificValueEditOwnerKey(p_strKey);
			//if we didn't edit the shader, then leave it alone
			if (!strKey.Equals(strCurrentOwnerKey))
				return;

			//if we did edit the shader, replace it with the shader we overwrote
			// if we didn't overwrite the shader, then just delete it
			byte[] btePreviousData = InstallLog.GetPreviousGameSpecificValue(p_strKey);
			if (btePreviousData != null)
			{
				/*TODO (likely never): I'm not sure if this is the strictly correct way to unedit a shader
				 * the original unedit code was:
				 * 
				 *	if (m_xelModInstallLogSdpEdits != null)
				 *	{
				 *		foreach (XmlNode node in m_xelModInstallLogSdpEdits.ChildNodes)
				 *		{
				 *			//TODO (likely never): Remove this workaround for the release version
				 *			if (node.Attributes.GetNamedItem("crc") == null)
				 *			{
				 *				InstallLog.UndoShaderEdit(int.Parse(node.Attributes.GetNamedItem("package").Value), node.Attributes.GetNamedItem("shader").Value, 0);
				 *			}
				 *			else
				 *			{
				 *				InstallLog.UndoShaderEdit(int.Parse(node.Attributes.GetNamedItem("package").Value), node.Attributes.GetNamedItem("shader").Value,
				 *					uint.Parse(node.Attributes.GetNamedItem("crc").Value));
				 *			}
				 *		}
				 *	}
				 *	
				 * where InstallLog.UndoShaderEdit was:
				 * 
				 *	public void UndoShaderEdit(int package, string shader, uint crc)
				 *	{
				 *		XmlNode node = sdpEditsNode.SelectSingleNode("sdp[@package='" + package + "' and @shader='" + shader + "']");
				 *		if (node == null) return;
				 *		byte[] b = new byte[node.InnerText.Length / 2];
				 *		for (int i = 0; i < b.Length; i++)
				 *		{
				 *			b[i] = byte.Parse("" + node.InnerText[i * 2] + node.InnerText[i * 2 + 1], System.Globalization.NumberStyles.AllowHexSpecifier);
				 *		}
				 *		if (SDPArchives.RestoreShader(package, shader, b, crc)) sdpEditsNode.RemoveChild(node);
				 *	}
				 *	
				 * after looking at SDPArchives it is not clear to me why a crc was being used.
				 * if ever it becomes evident that a crc is required, I will have to alter the log to store
				 *  a crc and pass it to the RestoreShader method.
				 */
				if (!sdpManager.RestoreShader(sedShader.Package, sedShader.ShaderName, btePreviousData, 0))
					throw new Exception("Failed to unedit the shader");
			}
			//TODO (likely never): how do we delete a shader? Right now, if there was no previous shader the current shader
			// remains
		}

		/// <summary>
		/// Finalizes the installation of the values.
		/// </summary>
		public virtual void FinalizeInstall()
		{
		}
	}
}
