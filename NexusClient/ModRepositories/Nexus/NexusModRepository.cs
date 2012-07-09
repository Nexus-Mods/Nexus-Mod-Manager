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
		/// <returns>An instance of the Nexus mod repository.</returns>
		public static IModRepository GetRepository(IGameMode p_gmdGameMode)
		{
			return new NexusModRepository(p_gmdGameMode);
		}

		private string m_strWebsite = null;
		private string m_strEndpoint = null;
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
		}

		/// <summary>
		/// Gets the user agent to use when making requests of the repository.
		/// </summary>
		/// <value>The user agent to use when making requests of the repository.</value>
		protected string UserAgent { get; private set; }

		#endregion

		#region Helpers

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
			ChannelFactory<INexusModRepositoryApi> cftProxyFactory = new ChannelFactory<INexusModRepositoryApi>(m_strEndpoint);
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
			string strSite = m_strWebsite;
			string strLoginUrl = String.Format("http://{0}/modules/login/do_login.php", strSite);
			HttpWebRequest hwrLogin = (HttpWebRequest)WebRequest.Create(strLoginUrl);
			CookieContainer ckcCookies = new CookieContainer();
			hwrLogin.CookieContainer = ckcCookies;
			hwrLogin.Method = WebRequestMethods.Http.Post;
			hwrLogin.ContentType = "application/x-www-form-urlencoded";
			hwrLogin.UserAgent = UserAgent;
			hwrLogin.ServicePoint.Expect100Continue = false;

			string strFields = String.Format("user={0}&pass={1}", p_strUsername, p_strPassword);
			byte[] bteFields = System.Text.Encoding.UTF8.GetBytes(strFields);
			hwrLogin.ContentLength = bteFields.Length;

			try
			{
				hwrLogin.GetRequestStream().Write(bteFields, 0, bteFields.Length);
				string strLoginResultPage = null;
				using (WebResponse wrpLoginResultPage = hwrLogin.GetResponse())
				{
					if (((HttpWebResponse)wrpLoginResultPage).StatusCode != HttpStatusCode.OK)
						throw new Exception("Request to the login page failed with HTTP error: " + ((HttpWebResponse)wrpLoginResultPage).StatusCode);

					Stream stmLoginResultPage = wrpLoginResultPage.GetResponseStream();
					using (StreamReader srdLoginResultPage = new StreamReader(stmLoginResultPage))
					{
						strLoginResultPage = srdLoginResultPage.ReadToEnd();
						srdLoginResultPage.Close();
					}
					wrpLoginResultPage.Close();
				}
			}
			catch (WebException e)
			{
				throw new RepositoryUnavailableException(String.Format("Cannot reach the {0} login server: {1}", Name, strLoginUrl), e);
			}
			m_dicAuthenticationTokens = new Dictionary<string, string>();
			foreach (Cookie ckeToken in ckcCookies.GetCookies(new Uri("http://" + strSite)))
				if (ckeToken.Name.EndsWith("_Member") || ckeToken.Name.EndsWith("_Premium"))
					m_dicAuthenticationTokens[ckeToken.Name] = ckeToken.Value;
			p_dicTokens = m_dicAuthenticationTokens;
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
			//TODO validate tokens
			m_dicAuthenticationTokens = new Dictionary<string, string>();
			foreach (KeyValuePair<string, string> kvpToken in p_dicTokens)
			{
				if (!kvpToken.Key.StartsWith("Nexus"))
				{
					m_dicAuthenticationTokens.Clear();
					return false;
				}
				m_dicAuthenticationTokens[kvpToken.Key] = kvpToken.Value;
			}
			return true;
		}

		/// <summary>
		/// Logs the user out of the mod repository.
		/// </summary>
		public void Logout()
		{
			m_dicAuthenticationTokens.Clear();
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
			ModInfo mifInfo = new ModInfo(p_nmiNexusModInfo.Id, p_nmiNexusModInfo.Name, p_nmiNexusModInfo.HumanReadableVersion, null, p_nmiNexusModInfo.Author, p_nmiNexusModInfo.Description, uriWebsite, null);
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
		/// Finds the mods containing the given search terms.
		/// </summary>
		/// <param name="p_strModNameSearchString">The terms to use to search for mods.</param>
		/// <param name="p_booIncludeAllTerms">Whether the returned mods' names should include all of
		/// the given search terms.</param>
		/// <returns>The mod info for the mods matching the given search criteria.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IList<IModInfo> FindMods(string p_strModNameSearchString, bool p_booIncludeAllTerms)
		{
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
			string[] strTerms = p_strModNameSearchString.Split(' ', '-', '_');
			string strSearchString = strTerms.OrderByDescending(s => s.Length).FirstOrDefault();

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
        }

		/// <summary>
		/// Gets the list of files for the specified mod.
		/// </summary>
		/// <param name="p_strModId">The id of the mod whose list of files is to be returned.</param>
		/// <returns>The list of files for the specified mod.</returns>
		/// <exception cref="RepositoryUnavailableException">Thrown if the repository cannot be reached.</exception>
		public IList<IModFileInfo> GetModFileInfo(string p_strModId)
		{
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
			List<Uri> lstDownloadUrls = new List<Uri>();
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					foreach (string strUrl in nmrApi.GetModFileDownloadUrls(p_strModId, p_strFileId))
						lstDownloadUrls.Add(new Uri(strUrl));
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
			return lstDownloadUrls.ToArray();
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
			try
			{
				using (IDisposable dspProxy = (IDisposable)GetProxyFactory().CreateChannel())
				{
					INexusModRepositoryApi nmrApi = (INexusModRepositoryApi)dspProxy;
					return nmrApi.GetModFile(p_strModId, p_strFileId);
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
