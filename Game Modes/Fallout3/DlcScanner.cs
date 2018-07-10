using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using System.Windows.Forms;
using Nexus.UI.Controls;

namespace Nexus.Client.Games.Fallout3
{
	/// <summary>
	/// Scans for DLCs, and optional moves them to make them compatible with the mod manager.
	/// </summary>
	public class DlcScanner
	{
		#region Properties

		/// <summary>
		/// Gets the application's environement info.
		/// </summary>
		/// <value>The application's environement info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the descriptor of the game mode that this factory builds.
		/// </summary>
		/// <value>The descriptor of the game mode that this factory builds.</value>
		public IGameModeDescriptor GameModeDescriptor { get; private set; }

		/// <summary>
		/// Gets or sets the delegate to call to confirm an action.
		/// </summary>
		/// <value>The delegate to call to confirm an action.</value>
		public ShowMessageDelegate ConfirmAction { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple consturctor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environement info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor of the game mode that this factory builds.</param>
		public DlcScanner(IEnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameModeDescriptor = p_gmdGameModeInfo;
			ConfirmAction = delegate { return DialogResult.Cancel; };
		}

		#endregion

		/// <summary>
		/// This checks for DLCs isntall by Windows Live, and optionally moves them so
		/// they are compatible with FOSE.
		/// </summary>
		public void CheckForDLCs()
		{
			Trace.TraceInformation("Checking DLCs location.");
			Trace.Indent();

			string strDLCDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\xlive\\DLC");
			string strInstallPath = EnvironmentInfo.Settings.InstallationPaths[GameModeDescriptor.ModeId];
			strInstallPath = Path.Combine(strInstallPath, "data");
			if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
			{
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();
				EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["IgnoreDLC"] = false;
			}
			if (Directory.Exists(strDLCDirectory) && !(bool)EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["IgnoreDLC"])
			{
				Dictionary<string, Dictionary<string, string>> dicDLCs = new Dictionary<string, Dictionary<string, string>>()
				{
					{ "Anchorage", new Dictionary<string, string>()},
					{ "ThePitt", new Dictionary<string, string>()},
					{ "BrokenSteel",new Dictionary<string, string>()
						{
							{"2 weeks later.bik", "video\\2 weeks later.bik"},
							{"B09.bik", "video\\B09.bik"},
							{"B27.bik", "video\\B27.bik"},
							{"B28.bik", "video\\B28.bik"},
							{"B29.bik", "video\\B29.bik"},
						}
					},
					{ "PointLookout", new Dictionary<string, string>()},
					{ "Zeta", new Dictionary<string, string>()}
				};

				foreach (string strDLC in dicDLCs.Keys)
				{
					Trace.Write(strDLC + "...");

					string[] strMainPlugin = Directory.GetFiles(strDLCDirectory, strDLC + ".esm", SearchOption.AllDirectories);
					if (strMainPlugin.Length == 1)
					{
						string strMainPluginInstallPath = Path.Combine(strInstallPath, strDLC + ".esm");
						string strMainBsaInstallPath = Path.Combine(strInstallPath, strDLC + " - Main.bsa");
						string strSoundBsaInstallPath = Path.Combine(strInstallPath, strDLC + " - Sounds.bsa");
						if (!File.Exists(strMainPluginInstallPath) && !File.Exists(strMainBsaInstallPath) && !File.Exists(strSoundBsaInstallPath))
						{
							string[] strMainBsa = Directory.GetFiles(strDLCDirectory, strDLC + " - Main.bsa", SearchOption.AllDirectories);
							string[] strSoundBsa = Directory.GetFiles(strDLCDirectory, strDLC + " - Sounds.bsa", SearchOption.AllDirectories);
							Dictionary<string, string[]> dicExtraFiles = new Dictionary<string, string[]>();
							foreach (string strExtraFile in dicDLCs[strDLC].Keys)
							{
								string[] strFound = Directory.GetFiles(strDLCDirectory, strExtraFile, SearchOption.AllDirectories);
								if (strFound.Length > 0)
									dicExtraFiles[strExtraFile] = strFound;
							}
							if (strMainPlugin.Length == 1 && strMainBsa.Length == 1 && strSoundBsa.Length == 1)
							{
								StringBuilder stbMessage = new StringBuilder("You seem to have bought the DLC ");
								stbMessage.AppendLine(strDLC);
								stbMessage.AppendFormat("Would you like to move it to {0}'s data directory to allow for offline use and script extender compatibility?", GameModeDescriptor.Name).AppendLine();
								stbMessage.AppendLine("Note that this may cause issues with any save games created after it was purchased but before it was moved.");
								stbMessage.AppendFormat("Click YES to move, IGNORE to ignore, and CANCEL if you don't want {0} to offer to move any DLCs for you again.", EnvironmentInfo.Settings.ModManagerName);

								switch ((DialogResult)ConfirmAction(new ViewMessage(stbMessage.ToString(), null, "Move DLC", ExtendedMessageBoxButtons.Yes | ExtendedMessageBoxButtons.Ignore | ExtendedMessageBoxButtons.Cancel, MessageBoxIcon.Question)))
								{
									case DialogResult.Yes:
										File.Move(strMainPlugin[0], strMainPluginInstallPath);
										File.Move(strMainBsa[0], strMainBsaInstallPath);
										File.Move(strSoundBsa[0], strSoundBsaInstallPath);
										foreach (string strExtraFile in dicExtraFiles.Keys)
										{
											string strNewPath = Path.Combine(strInstallPath, dicDLCs[strDLC][strExtraFile]);
											if (File.Exists(strNewPath))
												File.Move(strNewPath, strNewPath + ".old");
											if (!Directory.Exists(Path.GetDirectoryName(strNewPath)))
												Directory.CreateDirectory(Path.GetDirectoryName(strNewPath));
											File.Move(dicExtraFiles[strExtraFile][0], strNewPath);
										}
										break;
									case DialogResult.Ignore:
										EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["IgnoreDLC"] = true;
										EnvironmentInfo.Settings.Save();
										break;
								}
							}
						}
					}
					Trace.WriteLine("Done");
				}
			}
			Trace.Unindent();
		}
	}
}
