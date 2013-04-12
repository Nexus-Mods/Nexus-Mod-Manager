using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;

namespace Nexus.Client.ModRepositories.Nexus
{
	/// <summary>
	/// An Client Message Inspector that sets the user-agent for the service call.
	/// </summary>
	public class HttpUserAgentMessageInspector : IClientMessageInspector
	{
		private const string USER_AGENT_HTTP_HEADER = "user-agent";
		private string m_userAgent = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="userAgent">The user agent to use for the service calls.</param>
		public HttpUserAgentMessageInspector(string userAgent)
		{
			this.m_userAgent = userAgent;
		}

		#endregion

		#region IClientMessageInspector Members

		/// <summary>
		/// Processes the reply message.
		/// </summary>
		/// <remarks>
		/// This does nothing.
		/// </remarks>
		/// <param name="reply">The received reply to process.</param>
		/// <param name="correlationState">The correlation state.</param>
		public void AfterReceiveReply(ref Message reply, object correlationState)
		{

		}

		/// <summary>
		/// Processes the request message.
		/// </summary>
		/// <remarks>
		/// This adds the specified user-agent to the request.
		/// </remarks>
		/// <param name="request">The request to process.</param>
		/// <param name="channel">The client channel.</param>
		public object BeforeSendRequest(ref Message request, IClientChannel channel)
		{
			HttpRequestMessageProperty httpRequestMessage;
			object httpRequestMessageObject;
			if (request.Properties.TryGetValue(HttpRequestMessageProperty.Name, out httpRequestMessageObject))
			{
				httpRequestMessage = httpRequestMessageObject as HttpRequestMessageProperty;
				if (string.IsNullOrEmpty(httpRequestMessage.Headers[USER_AGENT_HTTP_HEADER]))
				{
					httpRequestMessage.Headers[USER_AGENT_HTTP_HEADER] = this.m_userAgent;
				}
			}
			else
			{
				httpRequestMessage = new HttpRequestMessageProperty();
				httpRequestMessage.Headers.Add(USER_AGENT_HTTP_HEADER, this.m_userAgent);
				request.Properties.Add(HttpRequestMessageProperty.Name, httpRequestMessage);
			}
			return null;
		}

		#endregion
	}
}
