using System.Collections.Generic;

namespace Nexus.Client
{
	public class HelpInformation
	{
		/// <summary>
		/// Describes a link to a help resource.
		/// </summary>
		public class HelpLink
		{
			#region Properties

			/// <summary>
			/// Gets the URL to the help resource.
			/// </summary>
			/// <value>The URL to the help resource.</value>
			public string Url { get; private set; }

			/// <summary>
			/// Gets the name of the help resource.
			/// </summary>
			/// <value>The name of the help resource.</value>
			public string Name { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with its dependencies.
			/// </summary>
			/// <param name="p_strName">The name of the help resource.</param>
			/// <param name="p_strUrl">The URL to the help resource.</param>
			public HelpLink(string p_strName, string p_strUrl)
			{
				Url = p_strUrl;
				Name = p_strName;
			}

			#endregion
		}
		#region Properties

		/// <summary>
		/// Gets the links to launch help.
		/// </summary>
		/// <value>The links to launch help.</value>
		public IEnumerable<HelpLink> HelpLinks { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public HelpInformation(IEnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;

			List<HelpLink> lstLinks = new List<HelpLink>();
			foreach (KeyValuePair<string, string> kvpLink in p_eifEnvironmentInfo.Settings.HelpLinks)
				lstLinks.Add(new HelpLink(kvpLink.Key, kvpLink.Value));
			HelpLinks = lstLinks;
		}

		#endregion
	}
}
