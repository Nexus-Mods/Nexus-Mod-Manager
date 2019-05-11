namespace Nexus.Client.SSO
{
    using System;

    /// <summary>
    /// Arguments related to the cancellation of an authentication process.
    /// </summary>
    public class CancellationEventArgs : EventArgs
    {
        /// <summary>
        /// The reason for the cancellation.
        /// </summary>
        public AuthenticationCancelledReason Reason { get; }

        /// <summary>
        /// Constructs a new <see cref="CancellationEventArgs"/>.
        /// </summary>
        /// <param name="reason">The reason for the cancellation.</param>
        public CancellationEventArgs(AuthenticationCancelledReason reason)
        {
            Reason = reason;
        }
    }
}
