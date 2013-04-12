using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

namespace Nexus.Client.ModRepositories.Nexus
{
	/// <summary>
	/// An HTTP Endpoint Behaviour that sets the user-agent for the service call.
	/// </summary>
	public class HttpUserAgentEndpointBehaviour : WebHttpBehavior
	{
		private string m_userAgent;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="userAgent">The user agent to use for the service calls.</param>
		public HttpUserAgentEndpointBehaviour(string userAgent)
		{
			this.m_userAgent = userAgent;
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
			HttpUserAgentMessageInspector amiInspector = new HttpUserAgentMessageInspector(this.m_userAgent);
			clientRuntime.MessageInspectors.Add(amiInspector);

		}
	}
}
