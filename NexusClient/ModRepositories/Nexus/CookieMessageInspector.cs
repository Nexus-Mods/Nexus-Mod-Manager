using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;

namespace Nexus.Client.ModRepositories.Nexus
{
	/// <summary>
	/// An Client Message Inspector that sets cookies for the service call.
	/// </summary>
	public class CookieMessageInspector : IClientMessageInspector
	{
		private const string COOKIE_HTTP_HEADER = "Cookie";
		private Dictionary<string, string> m_dicAuthenticationCookies = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_dicAuthenticationCookies">The cookies to add to the request.</param>
		public CookieMessageInspector(Dictionary<string, string> p_dicAuthenticationCookies)
		{
			m_dicAuthenticationCookies = p_dicAuthenticationCookies;
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
		/// This adds the specified cookies to the request.
		/// </remarks>
		/// <param name="request">The request to process.</param>
		/// <param name="channel">The client channel.</param>
		public object BeforeSendRequest(ref Message request, IClientChannel channel)
		{
			HttpRequestMessageProperty httpRequestMessage = null;
			object httpRequestMessageObject;
			if (request.Properties.TryGetValue(HttpRequestMessageProperty.Name, out httpRequestMessageObject))
				httpRequestMessage = httpRequestMessageObject as HttpRequestMessageProperty;
			if (httpRequestMessage == null)
			{
				httpRequestMessage = new HttpRequestMessageProperty();
				request.Properties.Add(HttpRequestMessageProperty.Name, httpRequestMessage);
			}
			string strCookie = httpRequestMessage.Headers[COOKIE_HTTP_HEADER];
			if (!String.IsNullOrEmpty(strCookie) && !strCookie.EndsWith(";"))
				strCookie += ";";
			foreach (KeyValuePair<string, string> kvpCookie in m_dicAuthenticationCookies)
				strCookie += String.Format("{0}={1};", kvpCookie.Key, kvpCookie.Value);
			httpRequestMessage.Headers[COOKIE_HTTP_HEADER] = strCookie;
			return null;
		}

		#endregion
	}
}
