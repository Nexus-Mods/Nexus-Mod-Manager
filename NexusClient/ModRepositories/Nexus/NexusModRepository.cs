using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text.RegularExpressions;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.ModManagement;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModRepositories.Nexus
{
	/// <remarks>
	/// The Nexus mod repository is the repository hosted with the Nexus group of websites.
	/// </remarks>
	public class NexusModRepository : IModRepository
	{
		/// <summary>
		/// Gets an instance of the Nexus mod repository.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_booOfflineMode">Whether the repository should be in a forced offline mode.</param>
		/// <returns>An instance of the Nexus mod repository.</returns>
		public static IModRepository GetRepository(IGameMode p_gmdGameMode)
		{
			return new NexusModRepository(p_gmdGameMode);
		}

		private string m_strWebsite = null;
		private string m_strEndpoint = null;
		private int m_intRemoteGameId = 0;
		private int m_intMaxConcurrentDownloads = 5;
		private string[] m_strUserStatus = null;
		private Dictionary<string, string> m_dicAuthenticationTokens = null;

		#region Custom Events

		public event EventHandler UserStatusUpdate;

		#endregion

		#region Properties

		/// <summary>
		/// Gets the id of the mod repository.
		/// </summary>
		/// <value>The id of the mod repository.</value>
		public string Id
		{
			get
			{
				return "Nexus";
			}
		}

		/// <summary>
		/// Gets the name of the mod repository.
		/// </summary>
		/// <value>The name of the mod repository.</value>
		public string Name
		{
			get
			{
				return "Nexus";
			}
		}

		/// <summary>
		/// Gets the user membership status.
		/// </summary>
		/// <value>The user membership status.</value>
		public string[] UserStatus
		{
			get
			{
				return m_strUserStatus;
			}
			private set
			{
				m_strUserStatus = value;
			}
		}

		/// <summary>
		/// Gets whether the repository supports unauthenticated downloads.
		/// </summary>
		/// <value>Whether the repository supports unauthenticated downloads.</value>
		public bool SupportsUnauthenticatedDownload
		{
			get
			{
				return false;
			}
		}

		/// <summary>
		/// Gets the User Agent used for the mod repository.
		/// </summary>
		/// <value>The User Agent.</value>
		public string UserAgent { get; private set; }

		/// <summary>
		/// Gets whether the repository is in a forced offline mode.
		/// </summary>
		/// <value>Whether the repository is in a forced offline mode.</value>
		public bool IsOffline
		{
			get
			{
				return ((m_strUserStatus == null) || (m_strUserStatus.Length <= 0));
			}
		}

		/// <summary>
		/// Gets the repository's file server zones.
		/// </summary>
		/// <value>the repository's file server zones.</value>
		public List<FileServerZone> FileServerZones { get; private set; }

		/// <summary>
		/// Gets the repository's file server zones.
		/// </summary>
		/// <value>the repository's file server zones.</value>
		private List<FileServerZone> RepositoryFileServerZones { get; set; }

		/// <summary>
		/// Gets the number allowed connections.
		/// </summary>
		/// <value>The number allowed connections.</value>
		public Int32 AllowedConnections { get; private set; }

		/// <summary>
		/// Gets the number of maximum allowed concurrent downloads.
		/// </summary>
		/// <value>The number of maximum allowed concurrent downloads.</value>
		public Int32 MaxConcurrentDownloads
		{
			get
			{
				return m_intMaxConcurrentDownloads;
			}
		}

		public string GameModeWebsite
		{
			get
			{
				return m_strWebsite;
			}
		}

		/// <summary>
		/// Gets the remote id of the mod repository.
		/// </summary>
		/// <value>The id of the mod repository.</value>
		public int RemoteGameId
		{
			get
			{
				return m_intRemoteGameId;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor the initializes the object with the required dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode for which mods are being managed.</param>
		public NexusModRepository(IGameMode p_gmdGameMode)
		{
			SetWebsite(p_gmdGameMode);
			UserAgent = String.Format("Nexus Client v{0}", ProgrammeMetadata.VersionString);
			SetFileServerZones();
			AllowedConnections = 1;
		}

		#endregion

		#region Helpers

		protected void SetFileServerZones()
		{
			FileServerZones = new List<FileServerZone>();
			FileServerZones.Add(new FileServerZone());
            //FileServerZones.Add(new FileServerZone("us", "U.S.A.", 2, global::Nexus.Client.Properties.Resources.us, false));
            FileServerZones.Add(new FileServerZone("na.ca", "N.A. - North America Premium", 2, global::Nexus.Client.Properties.Resources.us, true));
   //         FileServerZones.Add(new FileServerZone("us.p2", "U.S. - Dallas Premium", 2, global::Nexus.Client.Properties.Resources.us, true));
			//FileServerZones.Add(new FileServerZone("us.p1", "U.S. - Washington Premium", 2, global::Nexus.Client.Properties.Resources.us, true));
			FileServerZones.Add(new FileServerZone("eu.p1", "E.U. - UK Premium", 1, global::Nexus.Client.Properties.Resources.europeanunion, true));
            FileServerZones.Add(new FileServerZone("eu.fr", "E.U. - Europe Premium", 2, global::Nexus.Client.Properties.Resources.europeanunion, true));
            //FileServerZones.Add(new FileServerZone("eu", "European Union", 1, global::Nexus.Client.Properties.Resources.europeanunion, false));

            RepositoryFileServerZones = new List<FileServerZone>();
			RepositoryFileServerZones.Add(new FileServerZone());
			//RepositoryFileServerZones.Add(new FileServerZone("en", "England", 1, global::Nexus.Client.Properties.Resources.en, false));
			//RepositoryFileServerZones.Add(new FileServerZone("us.w", "US West Coast", 2, global::Nexus.Client.Properties.Resources.us, false));
			//RepositoryFileServerZones.Add(new FileServerZone("us.e", "US East Coast", 2, global::Nexus.Client.Properties.Resources.us, false));
			//RepositoryFileServerZones.Add(new FileServerZone("us.c", "US Central", 2, global::Nexus.Client.Properties.Resources.us, false));
			//RepositoryFileServerZones.Add(new FileServerZone("nl", "Netherlands", 1, global::Nexus.Client.Properties.Resources.nl, false));
			//RepositoryFileServerZones.Add(new FileServerZone("cz", "Czech Republic", 1, global::Nexus.Client.Properties.Resources.cz, false));
			//RepositoryFileServerZones.Add(new FileServerZone("us.p2", "U.S. - Dallas Premium", 2, global::Nexus.Client.Properties.Resources.us, true));
			//RepositoryFileServerZones.Add(new FileServerZone("us.p1", "U.S. - Washington Premium", 2, global::Nexus.Client.Properties.Resources.us, true));
            RepositoryFileServerZones.Add(new FileServerZone("na.ca", "N.A. - North America Premium", 2, global::Nexus.Client.Properties.Resources.us, true));
            RepositoryFileServerZones.Add(new FileServerZone("eu.p1", "E.U. - UK Premium", 1, global::Nexus.Client.Properties.Resources.europeanunion, true));
            RepositoryFileServerZones.Add(new FileServerZone("eu.fr", "E.U. - Europe Premium", 1, global::Nexus.Client.Properties.Resources.europeanunion, true));
        }

		/// <summary>
		/// Sets the service endpoint to use for the given game mode.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode for which mods are being managed.</param>
		/// <returns>The website for the given Nexus site.</returns>
		protected void SetWebsite(IGameMode p_gmdGameMode)
		{
			switch (p_gmdGameMode.ModeId)
			{
				case "BreakingWheel":
					m_strWebsite = "www.nexusmods.com/breakingwheel";
					m_strEndpoint = "BWNexusREST";
					m_intRemoteGameId = 1767;
					break;
				case "DragonAge":
					m_strWebsite = "www.nexusmods.com/dragonage";
					m_strEndpoint = "DAONexusREST";
					m_intRemoteGameId = 140;
					break;
				case "DragonAge2":
					m_strWebsite = "www.nexusmods.com/dragonage2";
					m_strEndpoint = "DA2NexusREST";
					m_intRemoteGameId = 141;
					break;
				case "DragonsDogma":
					m_strWebsite = "www.nexusmods.com/dragonsdogma";
					m_strEndpoint = "DDDANexusREST";
					m_intRemoteGameId = 1249;
					break;
				case "Fallout3":
					m_strWebsite = "www.nexusmods.com/fallout3";
					m_strEndpoint = "FO3NexusREST";
					m_intRemoteGameId = 120;
					break;
				case "FalloutNV":
					m_strWebsite = "www.nexusmods.com/newvegas";
					m_strEndpoint = "FONVNexusREST";
					m_intRemoteGameId = 130;
					break;
				case "Fallout4":
					m_strWebsite = "www.nexusmods.com/fallout4";
					m_strEndpoint = "FO4NexusREST";
					m_intRemoteGameId = 1151;
					break;
				case "Morrowind":
					m_strWebsite = "www.nexusmods.com/morrowind";
					m_strEndpoint = "MWNexusREST";
					m_intRemoteGameId = 100;
					break;
				case "NoMansSky":
					m_strWebsite = "www.nexusmods.com/nomanssky";
					m_strEndpoint = "NMSNexusREST";
					m_intRemoteGameId = 1634;
					break;
				case "Oblivion":
					m_strWebsite = "www.nexusmods.com/oblivion";
					m_strEndpoint = "OBNexusREST";
					m_intRemoteGameId = 101;
					break;
				case "Skyrim":
					m_strWebsite = "www.nexusmods.com/skyrim";
					m_strEndpoint = "SKYRIMNexusREST";
					m_intRemoteGameId = 110;
					break;
				case "SkyrimSE":
					m_strWebsite = "www.nexusmods.com/skyrimspecialedition";
					m_strEndpoint = "SKYRIMSENexusREST";
					m_intRemoteGameId = 1704;
					break;
				case "WorldOfTanks":
					m_strWebsite = "www.nexusmods.com/worldoftanks";
					m_strEndpoint = "WOTNexusREST";
					m_intRemoteGameId = 160;
					break;
				case "DarkSouls":
					m_strWebsite = "www.nexusmods.com/darksouls";
					m_strEndpoint = "DSNexusREST";
					m_intRemoteGameId = 162;
					break;
				case "DarkSouls2":
					m_strWebsite = "www.nexusmods.com/darksouls2";
					m_strEndpoint = "DS2NexusREST";
					m_intRemoteGameId = 482;
					break;
				case "TESO":
					m_strWebsite = "www.nexusmods.com/elderscrollsonline";
					m_strEndpoint = "TESONexusREST";
					m_intRemoteGameId = 419;
					break;
				case "Grimrock":
					m_strWebsite = "www.nexusmods.com/grimrock";
					m_strEndpoint = "LOGNexusREST";
					m_intRemoteGameId = 161;
					break;
				case "Witcher2":
					m_strWebsite = "www.nexusmods.com/witcher2";
					m_strEndpoint = "W2NexusREST";
					m_intRemoteGameId = 153;
					break;
				case "Witcher3":
					m_strWebsite = "witcher3.nexusmods.com";
					m_strEndpoint = "W3NexusREST";
					m_intRemoteGameId = 952;
					break;
				case "XRebirth":
					m_strWebsite = "www.nexusmods.com/xrebirth";
					m_strEndpoint = "XRNexusREST";
					m_intRemoteGameId = 154;
					break;
				case "Starbound":
					m_strWebsite = "www.nexusmods.com/starbound";
					m_strEndpoint = "STARBOUNDNexusREST";
					m_intRemoteGameId = 242;
					break;
				case "StateOfDecay":
					m_strWebsite = "www.nexusmods.com/stateofdecay";
					m_strEndpoint = "SODNexusREST";
					m_intRemoteGameId = 223;
					break;
				case "WarThunder":
					m_strWebsite = "www.nexusmods.com/warthunder";
					m_strEndpoint = "WTNexusREST";
					m_intRemoteGameId = 449;
					break;
				case "XCOM2":
					m_strWebsite = "www.nexusmods.com/xcom2";
					m_strEndpoint = "XCOM2NexusREST";
					m_intRemoteGameId = 1271;
					break;
				default:
					throw new Exception("Unsupported game mode: " + p_gmdGameMode.ModeId);
			}
		}

		/// <summary>
		/// Returns a factory that is used to create proxies to the repository.
		/// </summary>
		/// <returns>A factory that is used to create proxies to the repository.</returns>
		protected ChannelFactory<INexusModRepositoryApi> GetProxyFactory()
		{
			return GetProxyFactory(false);
		}

		/// <summary>
		/// Returns a factory that is used to create proxies to the repository.
		/// </summary>
		/// <returns>A factory that is used to create proxies to the repository.</returns>
		protected ChannelFactory<INexusModRepositoryApi> GetProxyFactory(long p_timeout)
		{
			return GetProxyFactory(false, p_timeout);
		}

		/// <summary>
		/// Returns a factory that is used to create proxies to the repository.
		/// </summary>
		/// <param name="p_booIsGatekeeper">Whether or not we are communicating with the gatekeeper.</param>
		/// <returns>A factory that is used to create proxies to the repository.</returns>
		protected ChannelFactory<INexusModRepositoryApi> GetProxyFactory(bool p_booIsGatekeeper)
		{
			ChannelFactory<INexusModRepositoryApi> cftProxyFactory = new ChannelFactory<INexusModRepositoryApi>(p_booIsGatekeeper ? "GatekeeperNexusREST" : m_strEndpoint);
			cftProxyFactory.Endpoint.Behaviors.Add(new HttpUserAgentEndpointBehaviour(UserAgent));
			cftProxyFactory.Endpoint.Behaviors.Add(new CookieEndpointBehaviour(m_dicAuthenticationTokens));
			return cftProxyFactory;
		}

		/// <summary>
		/// Returns a factory that is used to create proxies to the repository.
		/// </summary>
		/// <param name="p_booIsGatekeeper">Whether or not we are communicating with the gatekeeper.</param>
		/// <param name="p_timeout">The timeout value.</param>
		/// <returns>A factory that is used to create proxies to the repository.</returns>
		protected ChannelFactory<INexusModRepositoryApi> GetProxyFactory(bool p_booIsGatekeeper, long p_timeout)
		{
			ChannelFactory<INexusModRepositoryApi> cftProxyFactory = new ChannelFactory<INexusModRepositoryApi>(p_booIsGatekeeper ? "GatekeeperNexusREST" : m_strEndpoint);
			cftProxyFactory.Endpoint.Behaviors.Add(new HttpUserAgentEndpointBehaviour(UserAgent));
			cftProxyFactory.Endpoint.Behaviors.Add(new CookieEndpointBehaviour(m_dicAuthenticationTokens));
			if (p_timeout > 0)
			{
				cftProxyFactory.Endpoint.Binding.OpenTimeout = TimeSpan.FromSeconds(p_timeout);
				cftProxyFactory.Endpoint.Binding.SendTimeout = TimeSpan.FromSeconds(p_timeout);
			}
			return cftProxyFactory;
		}

		protected int GetNthIndex(string s, char t, int n)
		{
			int count = 0;
			for (int i = 0; i < s.Length; i++)
			{
				if (s[i] == t)
				{
					count++;
					if (count == n)
					{
						return i;
					}
				}
			}
			return -1;
		}

		/// <summary>
		/// Parses out the mod id from the given mod file name.
		/// </summary>
		/// <param name="p_strFilename">The filename from which to parse the mod id.</param>
		/// <param name="p_mifInfo">The mod info for the mod identified by the parsed mod id.</param>
		/// <returns>The mod id, if one was found; <c>null</c> otherwise.</returns>
		protected string ParseModIdFromFilename(string p_strFilename, out IModInfo p_mifInfo)
		{
			Regex rgxModId = new Regex(@"-((\d+)[-\.])+");

			int intCount = 0;
			foreach (char c in p_strFilename)
				if (c == '-') intCount++;

			string strFilename = Path.GetFileName(p_strFilename);
			Match mchModId = null;

			if (intCount > 3)
			{
				string strCheckName = Path.GetFileName(p_strFilename);
				strCheckName = strCheckName.Substring(GetNthIndex(strCheckName, '-', 1));
				mchModId = rgxModId.Match(strCheckName);
				if (!mchModId.Success)
				{
					p_mifInfo = null;
					return null;
				}
			}
			else
			{
				mchModId = rgxModId.Match(strFilename);
				if (!mchModId.Success)
				{
					p_mifInfo = null;
					return null;
				}
			}

			IModInfo mifInfo = null;
			string[] strFilenameWords = strFilename.Split(new char[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
			List<KeyValuePair<Int32, IModInfo>> lstCandidates = new List<KeyValuePair<Int32, IModInfo>>();
			foreach (Capture cptMatch in mchModId.Groups[2].Captures)
			{
				string strId = cptMatch.Value;
				//get the mod info to make sure the id is valid, and not
				// just some random match from elsewhere in the filename
				IModInfo mifInfoCandidate = GetModInfo(strId);
				if (mifInfoCandidate != null)
				{
					IList<IModFileInfo> lstFiles = GetModFileInfo(strId);
					int intBestFoundWordCount = 0;
					int intValidWordCount = 0;
					foreach (IModFileInfo mfiFile in lstFiles)
					{
						if (mfiFile.Filename.Equals(strFilename, StringComparison.OrdinalIgnoreCase) ||
							mfiFile.Filename.Replace(' ', '_').Equals(strFilename, StringComparison.OrdinalIgnoreCase))
						{
							mifInfo = mifInfoCandidate;
							mifInfo.HumanReadableVersion = mfiFile.HumanReadableVersion;
							break;
						}
						int intFoundWordCount = 0;
						foreach (string strWord in strFilenameWords)
						{
							if (strWord.Length > 2)
							{
								intValidWordCount++;
								if (mfiFile.Filename.IndexOf(strWord, StringComparison.OrdinalIgnoreCase) > -1)
									intFoundWordCount++;
							}
						}
						if (intFoundWordCount > intBestFoundWordCount)
							intBestFoundWordCount = intFoundWordCount;
					}
					if (mifInfo != null)
						break;

					if (intBestFoundWordCount > 0)
					{
						int intWords = intValidWordCount / 2;
						if ((strFilenameWords.Length == 1) || (intValidWordCount == 1)  || (intBestFoundWordCount > intWords))
							lstCandidates.Add(new KeyValuePair<Int32, IModInfo>(intBestFoundWordCount, mifInfoCandidate));
					}
				}
			}
			if ((mifInfo == null) && !lstCandidates.IsNullOrEmpty())
			{
				lstCandidates.Sort((x, y) => -x.Key.CompareTo(y.Key));
				mifInfo = lstCandidates[0].Value;
			}
			p_mifInfo = mifInfo;
			return (mifInfo == null) ? null : mifInfo.Id;
		}

		#endregion

		#region Account Management

		/// <summary>
		/// Logs the user into the mod repository.
		/// </summary>
		/// <param name="p_strUsername">The username of the account with which to login.</param>
		/// <param name="p_strPassword">The password of the account with which to login.</param>
		/// <param name="p_dicTokens">The returned tokens that can be used to login instead of the username/password
		/// credentials.</param>
		/// <returns><c>true</c> if the login was successful;
		/// <c>fales</c> otherwise.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository is not available.</exception>
		public bool Login(string p_strUsername, string p_strPassword, out Dictionary<string, string> p_dicTokens)
		{
			string strCookie = null;
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory(true).CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					strCookie = nmrApi.Login(p_strUsername, p_strPassword);
				}
			}
			catch (MessageHeaderException e)
			{
				//'NexusLoginErrorCode: 1' - 'Invalid username or password.'
				//'NexusLoginErrorCode: 2' - 'Validation required in order to use the account.'
				//'NexusLoginErrorCode: 3' - 'This account has been banned.'
				//'NexusLoginErrorCode: 4' - 'Invalid login data.'

				string strNexusLoginErrorCode = e.Message.Split('#')[0].Trim();
				string strNexusLoginErrorMessage = String.Empty;
				if (strNexusLoginErrorCode.Equals("4"))
					strNexusLoginErrorMessage = "Wrong username or password.";
				else
					strNexusLoginErrorMessage = e.Message.Split('#')[1].Trim();
				throw new RepositoryUnavailableException(String.Format("{0}", strNexusLoginErrorMessage), e);
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Timeout! Cannot reach the {0} login server.", Name), e);
			}
			catch (CommunicationException e)
			{
				if ((((System.Exception)(e)).InnerException != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers != null))
				{
					WebHeaderCollection whcHeaders = ((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers;
					string strNexusError = String.Empty;
					string strNexusErrorInfo = String.Empty;
					foreach (string Header in whcHeaders.Keys)
					{
						switch (Header)
						{
							case "NexusError":
								strNexusError = whcHeaders.GetValues(Header)[0];
								break;
							case "NexusErrorInfo":
								strNexusErrorInfo = whcHeaders.GetValues(Header)[0];
								break;
						}
					}

					if (!string.IsNullOrEmpty(strNexusError) && (strNexusError == "666"))
					{
						Trace.WriteLine("Login error: " + e.Message);
						if (e.InnerException != null)
							Trace.WriteLine("Login inner exception: " + e.InnerException.Message);
						throw new RepositoryUnavailableException(strNexusErrorInfo + Environment.NewLine + "You can keep using Nexus Mod Manager in OFFLINE MODE clicking the OFFLINE button.", e);
					}
				}
				if (e.Message == "Internal Server Error")
					throw new RepositoryUnavailableException(String.Format("{0} server error! This is a server issue, try again after a few minutes.", Name), e);
				else
					throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server. Either your firewall is blocking NMM or the login server is down.", Name), e);
			}
			catch (SerializationException e)
			{
				Trace.WriteLine("Login error: " + e.Message);
				if (e.InnerException != null)
					Trace.WriteLine("Login inner exception: " + e.InnerException.Message);
				throw new RepositoryUnavailableException(String.Format("Unexpected response from the {0} login server. Please try again later.", Name), e);
			}
			m_dicAuthenticationTokens = new Dictionary<string, string>();
			if (!String.IsNullOrEmpty(strCookie))
				m_dicAuthenticationTokens["sid"] = strCookie;
			p_dicTokens = m_dicAuthenticationTokens;

			if (m_dicAuthenticationTokens.Count > 0)
				GetUserCredentials();

			return m_dicAuthenticationTokens.Count > 0;
		}

		/// <summary>
		/// Logs the user into the mod repository.
		/// </summary>
		/// <param name="p_dicTokens">The authentication tokens with which to login.</param>
		/// <returns><c>true</c> if the given tokens are valid;
		/// <c>fales</c> otherwise.</returns>
		public bool Login(Dictionary<string, string> p_dicTokens)
		{
			m_dicAuthenticationTokens = p_dicTokens;
			string strCookie = null;
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory(true).CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					strCookie = nmrApi.ValidateTokens();
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Timeout! Cannot reach the {0} login server.", Name), e);
			}
			catch (CommunicationException e)
			{
				if ((((System.Exception)(e)).InnerException != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers != null))
				{
					WebHeaderCollection whcHeaders = ((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers;
					string strNexusError = String.Empty;
					string strNexusErrorInfo = String.Empty;
					foreach (string Header in whcHeaders.Keys)
					{
						switch (Header)
						{
							case "NexusError":
								strNexusError = whcHeaders.GetValues(Header)[0];
								break;
							case "NexusErrorInfo":
								strNexusErrorInfo = whcHeaders.GetValues(Header)[0];
								break;
						}
					}

					if (!string.IsNullOrEmpty(strNexusError) && (strNexusError == "666"))
					{
						Trace.WriteLine("Login error: " + e.Message);
						if (e.InnerException != null)
							Trace.WriteLine("Login inner exception: " + e.InnerException.Message);
						throw new RepositoryUnavailableException(strNexusErrorInfo + Environment.NewLine + "You can keep using Nexus Mod Manager in OFFLINE MODE by clicking the Stay Offline button.", e);
					}

				}
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server. Either your firewall is blocking NMM or the login server is down.", Name), e);
			}
			catch (SerializationException e)
			{
				Trace.WriteLine("Login error: " + e.Message);
				if (e.InnerException != null)
					Trace.WriteLine("Login inner exception: " + e.InnerException.Message);
				throw new RepositoryUnavailableException(String.Format("Unexpected response! Cannot reach the {0} login server.", Name), e);
			}
			catch (Exception e)
			{
				Trace.WriteLine("Login error: " + e.Message);
				if (e.InnerException != null)
					Trace.WriteLine("Login inner exception: " + e.InnerException.Message);
				throw new RepositoryUnavailableException(String.Format("Unable to perform token authentication, retry using your credentials.", Name), e);
			}


			if (String.IsNullOrEmpty(strCookie))
				m_dicAuthenticationTokens = null;

			if (!String.IsNullOrEmpty(strCookie))
				GetUserCredentials();

			return !String.IsNullOrEmpty(strCookie);
		}

		/// <summary>
		/// Logs the user out of the mod repository.
		/// </summary>
		public void Logout()
		{
			if (m_dicAuthenticationTokens != null)
				m_dicAuthenticationTokens.Clear();

			if (m_strUserStatus != null)
				m_strUserStatus = null;

			UserStatusUpdateEvent();
		}

		#endregion

		#region Mod Info

		/// <summary>
		/// Converts the native Nexus repository mod info data structure into an <see cref="IModInfo"/>
		/// structure.
		/// </summary>
		/// <param name="p_nmiNexusModInfo">The structure to convert.</param>
		/// <returns>The converted structure.</returns>
		private IModInfo Convert(NexusModInfo p_nmiNexusModInfo)
		{
			Uri uriWebsite = (String.IsNullOrEmpty(p_nmiNexusModInfo.Website) ? null : new Uri(p_nmiNexusModInfo.Website));
			string strDownloadID = p_nmiNexusModInfo.DownloadId;
			string strFilename = p_nmiNexusModInfo.Filename;
			string modName = string.IsNullOrWhiteSpace(p_nmiNexusModInfo.RequestedFileName) ? p_nmiNexusModInfo.Name : p_nmiNexusModInfo.Name + " - " + p_nmiNexusModInfo.RequestedFileName;
			if (String.IsNullOrWhiteSpace(strDownloadID) || (strDownloadID == "0") || (strDownloadID == "-1"))
			{
				strDownloadID = p_nmiNexusModInfo.NewDownloadId ?? strDownloadID;
				strFilename = p_nmiNexusModInfo.NewFilename ?? strFilename;
			}
			ModInfo mifInfo = new ModInfo(p_nmiNexusModInfo.Id, strDownloadID, modName, strFilename, p_nmiNexusModInfo.HumanReadableVersion, p_nmiNexusModInfo.HumanReadableVersion, p_nmiNexusModInfo.IsEndorsed, null, p_nmiNexusModInfo.Author, p_nmiNexusModInfo.CategoryId, -1, p_nmiNexusModInfo.Description, null, uriWebsite, null);
			return mifInfo;
		}

		/// <summary>
		/// Gets the mod info for the mod to which the specified download file belongs.
		/// </summary>
		/// <param name="p_strFilename">The name of the file whose mod's info is to be returned..</param>
		/// <returns>The info for the mod to which the specified file belongs.</returns>
		public IModInfo GetModInfoForFile(string p_strFilename)
		{
			IModInfo mifInfo = null;
			ParseModIdFromFilename(p_strFilename, out mifInfo);
			return mifInfo;
		}

		/// <summary>
		/// Gets the info for the specifed mod.
		/// </summary>
		/// <param name="p_strModId">The id of the mod info is be retrieved.</param>
		/// <returns>The info for the specifed mod.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IModInfo GetModInfo(string p_strModId)
		{
			NexusModInfo nmiInfo = null;

			if (IsOffline)
				return null;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory(15).CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					nmiInfo = nmrApi.GetModInfo(p_strModId, m_intRemoteGameId);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}

			if (nmiInfo == null)
				return null;

			return Convert(nmiInfo);
		}

		/// <summary>
		/// Gets the info for the specifed mod list.
		/// </summary>
		/// <param name="p_lstModList">The mod list to.</param>
		/// <returns>The update mods' list.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public List<IModInfo> GetModListInfo(List<string> p_lstModList)
		{
			NexusModInfo[] nmiInfo = null;
			List<IModInfo> imiUpdatedMods = new List<IModInfo>();
			string ModList = "";
			p_lstModList.ForEach(x => ModList += String.Format("{0},", "\"" + x + "\""));
			ModList = ModList.Trim(",".ToCharArray());
			ModList = "[" + ModList + "]";

			int howManyBytes = ModList.Length * sizeof(Char);
			if (howManyBytes == 1234)
				return null;

			if (IsOffline)
				return null;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory(15).CreateChannel())
				{

					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;

					nmiInfo = nmrApi.GetModListInfo(ModList, m_intRemoteGameId);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}

			if (nmiInfo == null)
				return null;

			foreach (NexusModInfo iMod in nmiInfo)
				imiUpdatedMods.Add(Convert(iMod));

			return imiUpdatedMods;
		}

		/// <summary>
		/// Gets the info for the specifed file list.
		/// </summary>
		/// <param name="p_lstFileList">The file list to.</param>
		/// <returns>The update file' list.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public List<IModInfo> GetFileListInfo(List<string> p_lstFileList)
		{
			NexusModInfo[] nmiInfo = null;
			List<IModInfo> imiUpdatedMods = new List<IModInfo>();
			string FileList = "";
			p_lstFileList.ForEach(x => FileList += String.Format("{0},", "\"" + x + "\""));
			FileList = FileList.Trim(",".ToCharArray());
			FileList = "[" + FileList + "]";

			int howManyBytes = FileList.Length * sizeof(Char);
			if (howManyBytes == 1234)
				return null;

			if (IsOffline)
				return null;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory(20).CreateChannel())
				{

					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;

					nmiInfo = nmrApi.GetFileListInfo(FileList, m_intRemoteGameId);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Timeout: Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				if ((((System.Exception)(e)).InnerException != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers != null))
				{
					WebHeaderCollection whcHeaders = ((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers;
					string strNexusError = String.Empty;
					string strNexusErrorInfo = String.Empty;
					foreach (string Header in whcHeaders.Keys)
					{
						switch (Header)
						{
							case "NexusError":
								strNexusError = whcHeaders.GetValues(Header)[0];
								break;
							case "NexusErrorInfo":
								strNexusErrorInfo = whcHeaders.GetValues(Header)[0];
								break;
						}
					}

					if (!string.IsNullOrEmpty(strNexusError) && (strNexusError == "666"))
						throw new RepositoryUnavailableException(strNexusErrorInfo, e);

				}
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Serialization: Cannot reach the {0} metadata server.", Name), e);
			}

			if (nmiInfo == null)
				return null;

			foreach (NexusModInfo iMod in nmiInfo)
				imiUpdatedMods.Add(Convert(iMod));

			return imiUpdatedMods;
		}

		/// <summary>
		/// Toggles the mod Endorsement state.
		/// </summary>
		/// <param name="p_strModId">The mod ID.</param>
		/// <param name="p_intLocalState">The local Endorsement state.</param>
		/// <returns>The updated online Endorsement state.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public bool ToggleEndorsement(string p_strModId, int p_intLocalState)
		{
			bool booOnlineState;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					booOnlineState = nmrApi.ToggleEndorsement(p_strModId, p_intLocalState, m_intRemoteGameId);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(e.InnerException.Message, e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(e.InnerException.Message, e);
			}
			catch (SerializationException e)
			{
				string strMessage = "you might not have downloaded this mod from the Nexus (or you downloaded it with another account)  or you have tried to endorse it within 15 minutes of downloading the file.";
				throw new RepositoryUnavailableException(strMessage, e);
			}

			return booOnlineState;
		}

		/// <summary>
		/// Finds the mods containing the given search terms.
		/// </summary>
		/// <param name="p_strModNameSearchString">The terms to use to search for mods.</param>
		/// <param name="p_booIncludeAllTerms">Whether the returned mods' names should include all of
		/// the given search terms.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IList<IModInfo> FindMods(string p_strModNameSearchString, bool p_booIncludeAllTerms)
		{
			if (IsOffline)
				return null;

			string[] strTerms = p_strModNameSearchString.Split('"');
			for (Int32 i = 0; i < strTerms.Length; i += 2)
				strTerms[i] = strTerms[i].Replace(' ', '~');
			//if the are an even number of terms we have unclosed quotes,
			// which means the last item is not actually quoted:
			// so replace its spaces, too.
			if (strTerms.Length % 2 == 0)
				strTerms[strTerms.Length - 1] = strTerms[strTerms.Length - 1].Replace(' ', '~');
			string strSearchString = String.Join("\"", strTerms);
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					List<IModInfo> mfiMods = new List<IModInfo>();
					nmrApi.FindMods(strSearchString, p_booIncludeAllTerms ? "ALL" : "ANY", m_intRemoteGameId).ForEach(x => mfiMods.Add(Convert(x)));
					return mfiMods;
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (NullReferenceException e)
			{
				throw new NullReferenceException(String.Format("No matches on the {0} server.", Name), e);
			}
		}

		/// <summary>
		/// Finds the mods containing the given search terms.
		/// </summary>
		/// <param name="p_strModNameSearchString">The terms to use to search for mods.</param>
		/// <param name="p_strModAuthor">The Mod author.</param>
		/// <param name="p_booIncludeAllTerms">Whether the returned mods' names should include all of
		/// the given search terms.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IList<IModInfo> FindMods(string p_strModNameSearchString, string p_strModAuthor, bool p_booIncludeAllTerms)
		{
			if (IsOffline)
				return null;

			string[] strTerms = p_strModNameSearchString.Split('"');
			for (Int32 i = 0; i < strTerms.Length; i += 2)
				strTerms[i] = strTerms[i].Replace(' ', '~');
			//if the are an even number of terms we have unclosed quotes,
			// which means the last item is not actually quoted:
			// so replace its spaces, too.
			if (strTerms.Length % 2 == 0)
				strTerms[strTerms.Length - 1] = strTerms[strTerms.Length - 1].Replace(' ', '~');
			string strSearchString = String.Join("\"", strTerms);

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					List<IModInfo> mfiMods = new List<IModInfo>();
					if (String.IsNullOrEmpty(p_strModAuthor))
						nmrApi.FindMods(strSearchString, p_booIncludeAllTerms ? "ALL" : "ANY", m_intRemoteGameId).ForEach(x => mfiMods.Add(Convert(x)));
					else
						nmrApi.FindModsAuthor(strSearchString, p_booIncludeAllTerms ? "ALL" : "ANY", p_strModAuthor, m_intRemoteGameId).ForEach(x => mfiMods.Add(Convert(x)));
					return mfiMods;
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (NullReferenceException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
		}

		/// <summary>
		/// Finds the mods by Author name.
		/// </summary>
		/// <param name="p_strModNameSearchString">The terms to use to search for mods.</param>
		/// <param name="p_strAuthorSearchString">The Author to use to search for mods.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IList<IModInfo> FindMods(string p_strModNameSearchString, string p_strAuthorSearchString)
		{
			if (IsOffline)
				return null;

			string[] strTerms = p_strModNameSearchString.Split('"');
			for (Int32 i = 0; i < strTerms.Length; i += 2)
				strTerms[i] = strTerms[i].Replace(' ', '~');
			//if the are an even number of terms we have unclosed quotes,
			// which means the last item is not actually quoted:
			// so replace its spaces, too.
			if (strTerms.Length % 2 == 0)
				strTerms[strTerms.Length - 1] = strTerms[strTerms.Length - 1].Replace(' ', '~');
			string strSearchString = String.Join("\"", strTerms);
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					List<IModInfo> mfiMods = new List<IModInfo>();
					nmrApi.FindModsAuthor(strSearchString, "ANY", p_strAuthorSearchString, m_intRemoteGameId).ForEach(x => mfiMods.Add(Convert(x)));
					return mfiMods;
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (NullReferenceException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
		}

		/// <summary>
		/// Gets the list of files for the specified mod.
		/// </summary>
		/// <param name="p_strModId">The id of the mod whose list of files is to be returned.</param>
		/// <returns>The list of files for the specified mod.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IList<IModFileInfo> GetModFileInfo(string p_strModId)
		{
			if (IsOffline)
				return null;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory(15).CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					return nmrApi.GetModFiles(p_strModId, m_intRemoteGameId).ConvertAll(x => (IModFileInfo)x);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
		}

		#endregion

		#region File Info

		/// <summary>
		/// Gets the URLs of the file parts for the default download file of the specified mod.
		/// </summary>
		/// <param name="p_strModId">The id of the mod whose default download file's parts' URLs are to be retrieved.</param>
		/// <param name="p_strFileId">The id of the file whose parts' URLs are to be retrieved.</param>
		/// <returns>The URLs of the file parts for the default download file.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public Uri[] GetFilePartUrls(string p_strModId, string p_strFileId)
		{
			if (IsOffline)
				return null;

			List<Uri> lstDownloadUrls = new List<Uri>();
			#region Deprecated
			//try
			//{
			//    using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
			//    {
			//        INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
			//        foreach (string strUrl in nmrApi.GetModFileDownloadUrls(p_strFileId))
			//            lstDownloadUrls.Add(new Uri(strUrl));
			//    }
			//}
			//catch (TimeoutException e)
			//{
			//    throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			//}
			//catch (CommunicationException e)
			//{
			//    throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			//}
			//catch (SerializationException e)
			//{
			//    throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			//}
			#endregion
			return lstDownloadUrls.ToArray();
		}

		/// <summary>
		/// Gets the URLs of the file parts for the default download file of the specified mod.
		/// </summary>
		/// <param name="p_strModId">The id of the mod whose default download file's parts' URLs are to be retrieved.</param>
		/// <param name="p_strFileId">The id of the file whose parts' URLs are to be retrieved.</param>
		/// <param name="p_strUserLocation">The preferred user location.</param>
		/// <param name="p_strRepositoryMessage">Custom repository message, if needed.</param>
		/// <returns>The FileserverInfo of the file parts for the default download file.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public List<FileserverInfo> GetFilePartInfo(string p_strModId, string p_strFileId, string p_strUserLocation, out string p_strRepositoryMessage)
		{
			p_strRepositoryMessage = String.Empty;
			if (IsOffline)
				return null;

			List<FileserverInfo> fsiServerInfo = new List<FileserverInfo>();
			List<FileserverInfo> fsiBestMatch;
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					fsiServerInfo = nmrApi.GetModFileDownloadUrls(p_strFileId, m_intRemoteGameId);
					fsiBestMatch = GetBestFileserver(fsiServerInfo, p_strUserLocation, out p_strRepositoryMessage);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			return fsiBestMatch;
		}


		private List<FileserverInfo> GetBestFileserver(List<FileserverInfo> fsiList, string p_strUserLocation, out string p_strRepositoryMessage)
		{
			List<FileserverInfo> fsiBestMatch = new List<FileserverInfo>();
			int intServerAffinity = 0;
			bool booPremium = false;
			bool booPremiumUnavailable = false;
			p_strRepositoryMessage = String.Empty;

			try
			{
				List<string> Countries = (from Url
											in fsiList
										  where !String.IsNullOrEmpty(Url.Country)
										  select Url.Country).ToList();

				if ((Countries != null) && (Countries.Count > 0))
				{
					if (p_strUserLocation != "default")
					{
						FileServerZone fszUser = FileServerZones.Find(x => x.FileServerID == p_strUserLocation);
						if (fszUser != null)
						{
							intServerAffinity = fszUser.FileServerAffinity;
							booPremium = fszUser.IsPremium;
						}
						else
							p_strUserLocation = "default";
					}

					if (booPremium && p_strUserLocation != "default")
					{
						fsiBestMatch = (from Url
										in fsiList
										where Url.Country == p_strUserLocation && !String.IsNullOrEmpty(Url.DownloadLink)
										orderby Url.IsPremium descending, Url.ConnectedUsers ascending
										select Url).ToList();
					}
					else if (booPremium && p_strUserLocation == "default")
					{
						fsiBestMatch = (from Url
										in fsiList
										where !String.IsNullOrEmpty(Url.DownloadLink)
										orderby Url.IsPremium descending, Url.ConnectedUsers ascending
										select Url).ToList();
					}

					if (fsiBestMatch == null)
						booPremiumUnavailable = true;

					if ((p_strUserLocation != "default") && ((fsiBestMatch == null) || (fsiBestMatch.Count == 0)))
					{

						Countries = (from Url
										in fsiList
									 where RepositoryFileServerZones.Find(x => x.FileServerID == Url.Country) != null
									 select Url.Country).ToList();

						if ((Countries != null) && (Countries.Count > 0))
						{
							fsiBestMatch = (from Url
											in fsiList
											where RepositoryFileServerZones.Find(x => x.FileServerID == Url.Country).FileServerAffinity == intServerAffinity && Url.IsPremium == false && !String.IsNullOrEmpty(Url.DownloadLink)
											orderby Url.ConnectedUsers ascending
											select Url).ToList();
						}
					}
				}
			}
			catch
			{ }

			try
			{
				if ((fsiBestMatch == null) || (fsiBestMatch.Count == 0))
				{
					fsiBestMatch = (from Url
									in fsiList
									where Url.IsPremium == false && !String.IsNullOrEmpty(Url.DownloadLink)
									orderby Url.IsPremium descending, Url.ConnectedUsers ascending
									select Url).ToList();
				}
			}
			catch { }

			if (booPremium && booPremiumUnavailable && ((p_strUserLocation == "us.p1") || (p_strUserLocation == "us.p2") || (p_strUserLocation == "eu.p1")))
				p_strRepositoryMessage = "Premium server unavailable, redirected: ";

			return fsiBestMatch;
		}

		/// <summary>
		/// Gets the user's membership status.
		/// </summary>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		private void GetUserCredentials()
		{
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					m_strUserStatus = nmrApi.GetCredentials(m_intRemoteGameId);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				if ((((System.Exception)(e)).InnerException != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers != null))
				{
					WebHeaderCollection whcHeaders = ((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers;
					string strNexusError = String.Empty;
					string strNexusErrorInfo = String.Empty;
					foreach (string Header in whcHeaders.Keys)
					{
						switch (Header)
						{
							case "NexusError":
								strNexusError = whcHeaders.GetValues(Header)[0];
								break;
							case "NexusErrorInfo":
								strNexusErrorInfo = whcHeaders.GetValues(Header)[0];
								break;
						}
					}

					if (!string.IsNullOrEmpty(strNexusError) && (strNexusError == "666"))
						throw new RepositoryUnavailableException(strNexusErrorInfo + Environment.NewLine + "You can keep using Nexus Mod Manager in OFFLINE MODE by clicking the Stay Offline button.", e);

				}
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}

			if (m_strUserStatus != null)
			{
				if (m_strUserStatus[1] != null)
				{
					if ((m_strUserStatus[1] == "4") || (m_strUserStatus[1] == "6") || (m_strUserStatus[1] == "13") || (m_strUserStatus[1] == "27") || (m_strUserStatus[1] == "31") || (m_strUserStatus[1] == "32"))
					{
						AllowedConnections = 4;
						m_intMaxConcurrentDownloads = 10;
					}
					else
					{
						AllowedConnections = 1;
						m_intMaxConcurrentDownloads = 5;
					}
				}
			}

			UserStatusUpdateEvent();
		}

		/// <summary>
		/// Gets the file info for the specified download file of the specified mod.
		/// </summary>
		/// <param name="p_strModId">The id of the mod the whose file's metadata is to be retrieved.</param>
		/// <param name="p_strFileId">The id of the download file whose metadata is to be retrieved.</param>
		/// <returns>The file info for the specified download file of the specified mod.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository is not available.</exception>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IModFileInfo GetFileInfo(string p_strModId, string p_strFileId)
		{
			if (IsOffline)
				return null;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					return nmrApi.GetModFile(p_strFileId, m_intRemoteGameId);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
		}

		/// <summary>
		/// Gets the file info for the specified download file.
		/// </summary>
		/// <param name="p_strFilename">The name of the file whose info is to be returned.</param>
		/// <param name="p_mifInfo">The mod info for the mod to which the specified file belongs.</param>
		/// <returns>The file info for the specified download file.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		private NexusModFileInfo GetFileInfoForFile(string p_strFilename, out IModInfo p_mifInfo)
		{
			if (IsOffline)
			{
				p_mifInfo = null;
				return null;
			}

			string strModId = ParseModIdFromFilename(p_strFilename, out p_mifInfo);
			if (strModId == null)
				return null;
			string strFilename = Path.GetFileName(p_strFilename);
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					List<NexusModFileInfo> mfiFiles = nmrApi.GetModFiles(strModId, m_intRemoteGameId);
					NexusModFileInfo mfiFileInfo = mfiFiles.Find(x => x.Filename.Equals(strFilename, StringComparison.OrdinalIgnoreCase));
					if (mfiFileInfo == null)
						mfiFileInfo = mfiFiles.Find(x => x.Filename.Replace(' ', '_').Equals(strFilename, StringComparison.OrdinalIgnoreCase));
					return mfiFileInfo;
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
		}

		/// <summary>
		/// Gets the file info for the specified download file.
		/// </summary>
		/// <param name="p_strFilename">The name of the file whose info is to be returned..</param>
		/// <returns>The file info for the specified download file.</returns>
		public IModFileInfo GetFileInfoForFile(string p_strFilename)
		{
			IModInfo mifInfo = null;
			return GetFileInfoForFile(p_strFilename, out mifInfo);
		}

		/// <summary>
		/// Gets the file info for the default file of the speficied mod.
		/// </summary>
		/// <param name="p_strModId">The id of the mod the whose default file's metadata is to be retrieved.</param>
		/// <returns>The file info for the default file of the speficied mod.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IModFileInfo GetDefaultFileInfo(string p_strModId)
		{
			if (IsOffline)
				return null;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					List<NexusModFileInfo> mfiFiles = nmrApi.GetModFiles(p_strModId, m_intRemoteGameId);
					NexusModFileInfo mfiDefault = (from f in mfiFiles
													where f.Category == ModFileCategory.MainFiles
													orderby f.Date descending
													select f).FirstOrDefault();
					if (mfiDefault == null)
						mfiDefault = (from f in mfiFiles
									  orderby f.Date descending
									  select f).FirstOrDefault();
					return mfiDefault;
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
		}

		#endregion

		public List<CategoriesInfo> GetCategories(int p_intGameId)
		{
			List<CategoriesInfo> nst = null;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					nst = nmrApi.GetCategories(p_intGameId);
				}
			}
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (CommunicationException e)
			{
				if ((((System.Exception)(e)).InnerException != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response != null) && (((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers != null))
				{
					WebHeaderCollection whcHeaders = ((System.Net.WebException)(((System.Exception)(e)).InnerException)).Response.Headers;
					string strNexusError = String.Empty;
					string strNexusErrorInfo = String.Empty;
					foreach (string Header in whcHeaders.Keys)
					{
						switch (Header)
						{
							case "NexusError":
								strNexusError = whcHeaders.GetValues(Header)[0];
								break;
							case "NexusErrorInfo":
								strNexusErrorInfo = whcHeaders.GetValues(Header)[0];
								break;
						}
					}

					if (!string.IsNullOrEmpty(strNexusError) && (strNexusError == "666"))
						throw new RepositoryUnavailableException(strNexusErrorInfo + Environment.NewLine + "You can keep using Nexus Mod Manager in OFFLINE MODE by clicking the Stay Offline button.", e);

				}
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} metadata server.", Name), e);
			}

			return nst;
		}

		private void UserStatusUpdateEvent()
		{
			if (this.UserStatusUpdate != null)
				this.UserStatusUpdate(this, new EventArgs());
		}
	}
}
