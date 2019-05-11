namespace Nexus.Client.SSO
{
    using System;
    using System.Diagnostics;
    using System.Security.Authentication;
    using System.Windows.Forms;
    using Newtonsoft.Json.Linq;
    using Util;
    using WebSocketSharp;

    /// <summary>
    /// Object managing an SSO authentication session.
    /// </summary>
    public class SsoManager : IDisposable
    {
        private const string SsoServiceAddress = "wss://sso.nexusmods.com";
        private const string NexusAddressBase = "https://www.nexusmods.com/sso?id={0}&application=nexusmodmanager";

        private readonly WebSocket _webSocket;

        private readonly string _uuid;
        private string _token;
        private bool _active;

        /// <summary>
        /// Event invoked when an API key has been received by this <see cref="SsoManager"/>.
        /// </summary>
        public event EventHandler<ApiKeyReceivedEventArgs> ApiKeyReceived;

        /// <summary>
        /// Event invoked when the authorization process is cancelled.
        /// </summary>
        public event EventHandler<CancellationEventArgs> AuthenticationCancelled;

        /// <summary>
        /// Creates a new <see cref="SsoManager"/>.
        /// </summary>
        public SsoManager()
        {
            _webSocket = new WebSocket(SsoServiceAddress);

            // Work-around for some old problem with WebSocketSharp.
            _webSocket.SslConfiguration.EnabledSslProtocols = (SslProtocols)(192|768|3072);

            _webSocket.OnOpen += WebSocketOnOpen;
            _webSocket.OnClose += WebSocketOnClose;
            _webSocket.OnError += WebSocketOnError;
            _webSocket.OnMessage += WebSocketOnMessage;

            _uuid = Guid.NewGuid().ToString("D");
            _active = false;
        }

        /// <summary>
        /// Initiates an authentication session.
        /// </summary>
        public void Start()
        {
            _active = true;
            _webSocket.Connect();
        }

        /// <summary>
        /// Cancels an authentication session.
        /// </summary>
        public void Cancel()
        {
            Cancel(AuthenticationCancelledReason.Manual);
        }

        private void Cancel(AuthenticationCancelledReason reason)
        {
            _active = false;
            _webSocket.Close();

            AuthenticationCancelled?.Invoke(this, new CancellationEventArgs(reason));
        }

        #region WebSocket events

        private void WebSocketOnMessage(object sender, MessageEventArgs e)
        {
            dynamic message = JObject.Parse(e.Data);

            if ($"{message.success}".Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                if (message.data.api_key != null)
                {
                    ApiKeyReceived?.Invoke(this, new ApiKeyReceivedEventArgs(message.data.api_key.ToString()));
                    _active = false;
                    _webSocket.Close();
                }
                else if (message.data.connection_token != null)
                {
                    _token = message.data.connection_token;
                }
                else
                {
                    MessageBox.Show(e.Data, "Unknown WebSocket Message");
                    Trace.TraceError($"Unknown WebSocket message:\n\t{e.Data}.");
                }
            }
            else
            {
                throw new InvalidOperationException(message.data.error);
            }
        }

        private void WebSocketOnError(object sender, ErrorEventArgs e)
        {
            MessageBox.Show($"{e.Message}\n{e.Exception.GetType()}: {e.Exception.Message}", "WebSocket Error");
        }

        private void WebSocketOnClose(object sender, CloseEventArgs e)
        {
            if (_active)
            {
                try
                {
                    _webSocket.Connect();
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("A series of reconnecting has failed."))
                    {
                        Trace.TraceWarning($"SsoManager: Could not connect to \"{SsoServiceAddress}\"");
                        Cancel(AuthenticationCancelledReason.ConnectionIssue);
                    }

                    TraceUtil.TraceException(ex);
                    Cancel(AuthenticationCancelledReason.Unknown);
                }
            }
        }

        private void WebSocketOnOpen(object sender, EventArgs e)
        {
            dynamic data = new JObject();
            data.id = _uuid;
            data.token = _token;
            data.protocol = 2;

            _webSocket.Send(data.ToString());
            Process.Start(string.Format(NexusAddressBase, _uuid));
        }

        #endregion

        /// <inheritdoc cref="IDisposable"/>
        public void Dispose()
        {
            _webSocket.OnOpen -= WebSocketOnOpen;
            _webSocket.OnClose -= WebSocketOnClose;
            _webSocket.OnError -= WebSocketOnError;
            _webSocket.OnMessage -= WebSocketOnMessage;

            ((IDisposable) _webSocket)?.Dispose();
        }
    }
}
