using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Text.RegularExpressions;
using Nexus.Client.Games;
using Nexus.Client.Mods;
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
		private string[] m_strUserStatus = null;
		private Dictionary<string, string> m_dicAuthenticationTokens = null;

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
		/// Gets the User Agent used for the mod repository.
		/// </summary>
		/// <value>The User Agent.</value>
		public string UserAgent { get; private set; }

		/// <summary>
		/// Gets whether the repository is in a forced offline mode.
		/// </summary>
		/// <value>Whether the repository is in a forced offline mode.</value>
		public bool IsOffline { get; private set; }

		/// <summary>
		/// Gets the repository's file server zones.
		/// </summary>
		/// <value>the repository's file server zones.</value>
		public List<FileServerZone> FileServerZones { get; private set; }

		/// <summary>
		/// Gets the number allowed connections.
		/// </summary>
		/// <value>The number allowed connections.</value>
		public Int32[] AllowedConnections { get; private set; }

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
			AllowedConnections = new Int32[] { 1 };
		}

		#endregion

		#region Helpers

		protected void SetFileServerZones()
		{
			FileServerZones = new List<FileServerZone>();
			FileServerZones.Add(new FileServerZone());
			FileServerZones.Add(new FileServerZone("en", "England", 1, global::Nexus.Client.Properties.Resources.en));
			FileServerZones.Add(new FileServerZone("us.w", "US West Coast", 2, global::Nexus.Client.Properties.Resources.us));
			FileServerZones.Add(new FileServerZone("us.e", "US East Coast", 2, global::Nexus.Client.Properties.Resources.us));
			FileServerZones.Add(new FileServerZone("us.c", "US Central", 2, global::Nexus.Client.Properties.Resources.us));
			FileServerZones.Add(new FileServerZone("nl", "Netherlands", 1, global::Nexus.Client.Properties.Resources.nl));
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
				case "DragonAgeOrigins":
					m_strWebsite = "";
					m_strEndpoint = "DAONexusREST";
					break;
				case "Fallout3":
					m_strWebsite = "fallout3.nexusmods.com";
					m_strEndpoint = "FO3NexusREST";
					break;
				case "FalloutNV":
					m_strWebsite = "newvegas.nexusmods.com";
					m_strEndpoint = "FONVNexusREST";
					break;
                case "Morrowind":
                    m_strWebsite = "morrowind.nexusmods.com";
                    m_strEndpoint = "MWNexusREST";
                    break;
				case "Oblivion":
					m_strWebsite = "oblivion.nexusmods.com";
                    m_strEndpoint = "OBNexusREST";
					break;
				case "Skyrim":
					m_strWebsite = "skyrim.nexusmods.com";
					m_strEndpoint = "SKYRIMNexusREST";
					break;
                case "WorldOfTanks":
                    m_strWebsite = "worldoftanks.nexusmods.com";
                    m_strEndpoint = "WOTNexusREST";
                    break;
				case "DarkSouls":
					m_strWebsite = "darksouls.nexusmods.com";
					m_strEndpoint = "DSNexusREST";
					break;
				case "Grimrock":
					m_strWebsite = "grimrock.nexusmods.com";
					m_strEndpoint = "LOGNexusREST";
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
		/// Parses out the mod id from the given mod file name.
		/// </summary>
		/// <param name="p_strFilename">The filename from which to parse the mod id.</param>
		/// <param name="p_mifInfo">The mod info for the mod identified by the parsed mod id.</param>
		/// <returns>The mod id, if one was found; <c>null</c> otherwise.</returns>
		protected string ParseModIdFromFilename(string p_strFilename, out IModInfo p_mifInfo)
		{
			Regex rgxModId = new Regex(@"-((\d+)[-\.])+");
			string strFilename = Path.GetFileName(p_strFilename);
			Match mchModId = rgxModId.Match(strFilename);
			if (!mchModId.Success)
			{
				p_mifInfo = null;
				return null;
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
					Int32 intBestFoundWordCount = 0;
					foreach (IModFileInfo mfiFile in lstFiles)
					{
						if (mfiFile.Filename.Equals(strFilename, StringComparison.OrdinalIgnoreCase) ||
							mfiFile.Filename.Replace(' ', '_').Equals(strFilename, StringComparison.OrdinalIgnoreCase))
						{
							mifInfo = mifInfoCandidate;
							break;
						}
						Int32 intFoundWordCount = 0;
						foreach (string strWord in strFilenameWords)
						{
							if (mfiFile.Filename.IndexOf(strWord, StringComparison.OrdinalIgnoreCase) > -1)
								intFoundWordCount++;
						}
						if (intFoundWordCount > intBestFoundWordCount)
							intBestFoundWordCount = intFoundWordCount;
					}
					if (mifInfo != null)
						break;

					if (intBestFoundWordCount > 0)
						lstCandidates.Add(new KeyValuePair<Int32, IModInfo>(intBestFoundWordCount, mifInfoCandidate));
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
			catch (TimeoutException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server.", Name), e);
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
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server.", Name), e);
			}
			catch (CommunicationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server.", Name), e);
			}
			catch (SerializationException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server.", Name), e);
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
		}

		/// <summary>
		/// Sets whether the repository should be used in offline mode.
		/// </summary>
		/// <param name="p_booOfflineMode">Whether the repository should be in a forced offline mode</param>
		public void SetOfflineMode(bool p_booOfflineMode)
		{
			IsOffline = p_booOfflineMode;
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
			//TODO ad URL to mod. should I generate the URL or should it be returned by the service?
			// I'm leaning toward it being returned by the service, as the URL can change at the whim of the server,
			// so having the base url hard-coded in the app is insane
			// i could possiblly derive it based on the url of the service, but the service could be on a different server
			string strURL = String.Format("http://{0}/downloads/file.php?id={1}", m_strWebsite, p_nmiNexusModInfo.Id);
			Uri uriWebsite = new Uri(strURL);
			ModInfo mifInfo = new ModInfo(p_nmiNexusModInfo.Id, p_nmiNexusModInfo.Name, p_nmiNexusModInfo.HumanReadableVersion, p_nmiNexusModInfo.HumanReadableVersion, p_nmiNexusModInfo.IsEndorsed, null, p_nmiNexusModInfo.Author, p_nmiNexusModInfo.CategoryId, -1, p_nmiNexusModInfo.Description, null, uriWebsite, null);
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
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					nmiInfo = nmrApi.GetModInfo(p_strModId);
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
			p_lstModList.ForEach(x => ModList += String.Format("{0},", "\"" +  x + "\""));
			ModList = ModList.Trim(",".ToCharArray());
			ModList = "[" + ModList + "]";

			if (IsOffline)
				return null;

			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					nmiInfo = nmrApi.GetModListInfo(ModList);
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
					booOnlineState = nmrApi.ToggleEndorsement(p_strModId, p_intLocalState);
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
				string strMessage = "you might not have downloaded this mod from the Nexus or you have tried to endorse it within 3 hours of downloading the file.";
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
					nmrApi.FindMods(strSearchString, p_booIncludeAllTerms ? "ALL" : "ANY").ForEach(x => mfiMods.Add(Convert(x)));
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
						nmrApi.FindMods(strSearchString, p_booIncludeAllTerms ? "ALL" : "ANY").ForEach(x => mfiMods.Add(Convert(x)));
					else
						nmrApi.FindModsAuthor(strSearchString, p_booIncludeAllTerms ? "ALL" : "ANY", p_strModAuthor).ForEach(x => mfiMods.Add(Convert(x)));
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
                    nmrApi.FindModsAuthor(strSearchString, "ANY", p_strAuthorSearchString).ForEach(x => mfiMods.Add(Convert(x)));
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
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					return nmrApi.GetModFiles(p_strModId).ConvertAll(x => (IModFileInfo)x);
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
		/// <param name="p_booPremiumOnly">Whether the user wants to use Premium servers only.</param>
		/// <param name="p_strUserLocation">The preferred user location.</param>
		/// <returns>The FileserverInfo of the file parts for the default download file.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public FileserverInfo GetFilePartInfo(string p_strModId, string p_strFileId, bool p_booPremiumOnly, string p_strUserLocation)
		{
			if (IsOffline)
				return null;

			List<FileserverInfo> fsiServerInfo = new List<FileserverInfo>();
			FileserverInfo fsiBestMatch;
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					fsiServerInfo = nmrApi.GetModFileDownloadUrls(p_strFileId);
					fsiBestMatch = GetBestFileserver(fsiServerInfo, p_booPremiumOnly, p_strUserLocation);
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


		private FileserverInfo GetBestFileserver(List<FileserverInfo> fsiList, bool p_booPremiumOnly, string p_strUserLocation)
		{
			FileserverInfo fsiBestMatch = new FileserverInfo();

			try
			{
				if (p_booPremiumOnly && p_strUserLocation != "default")
				{
					fsiBestMatch = (from Url
									in fsiList
									where Url.IsPremium == p_booPremiumOnly && Url.Country == p_strUserLocation
									orderby Url.ConnectedUsers ascending
									select Url).FirstOrDefault();
				}
				
				if ((((fsiBestMatch == null) || String.IsNullOrEmpty(fsiBestMatch.DownloadLink)) && p_booPremiumOnly) || (p_booPremiumOnly && p_strUserLocation == "default"))
				{
					fsiBestMatch = (from Url
									in fsiList
									where Url.IsPremium == p_booPremiumOnly
									orderby Url.ConnectedUsers ascending
									select Url).FirstOrDefault();
				}
				else if (p_strUserLocation != "default")
				{
					fsiBestMatch = (from Url
									in fsiList
									where Url.Country == p_strUserLocation
									orderby Url.ConnectedUsers ascending
									select Url).FirstOrDefault();
				}

				if ((fsiBestMatch == null) || String.IsNullOrEmpty(fsiBestMatch.DownloadLink))
				{
					fsiBestMatch = (from Url
									in fsiList
									orderby Url.ConnectedUsers ascending
									select Url).FirstOrDefault();
				}
			}
			catch
			{
			}

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
					m_strUserStatus = nmrApi.GetCredentials();
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

			if (m_strUserStatus != null)
				if (m_strUserStatus[1] != null)
					if ((m_strUserStatus[1] == "4") || (m_strUserStatus[1] == "6") || (m_strUserStatus[1] == "13") || (m_strUserStatus[1] == "27") || (m_strUserStatus[1] == "31") || (m_strUserStatus[1] == "32"))
						AllowedConnections = new Int32[] { 1, 2, 3, 4 };
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
					return nmrApi.GetModFile(p_strFileId);
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
					List<NexusModFileInfo> mfiFiles = nmrApi.GetModFiles(strModId);
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
					List<NexusModFileInfo> mfiFiles = nmrApi.GetModFiles(p_strModId);
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
	}
}
