using System.Collections.Generic;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

namespace Nexus.Client.ModRepositories.Nexus
{
	/// <summary>
	/// An HTTP Endpoint Behaviour that sets cookies for the service call.
	/// </summary>
	public class CookieEndpointBehaviour : WebHttpBehavior
	{
		private Dictionary<string, string> m_dicAuthenticationCookies = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_dicAuthenticationCookies">The cookies to add to the request.</param>
		public CookieEndpointBehaviour(Dictionary<string, string> p_dicAuthenticationCookies)
		{
			m_dicAuthenticationCookies = p_dicAuthenticationCookies;
		}

		#endregion

		/// <summary>
		/// Injects the user agent into the service request.
		/// </summary>
		/// <param name="endpoint">The enpoint of the service for which to set the user-agent.</param>
		/// <param name="clientRuntime">The client runtime.</param>
		public override void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
		{
			base.ApplyClientBehavior(endpoint, clientRuntime);
			CookieMessageInspector cmiInspector = new CookieMessageInspector(m_dicAuthenticationCookies);
			clientRuntime.MessageInspectors.Add(cmiInspector);

		}
	}
}
