using System;

namespace Nexus.Client.Exceptions
{
    /// <summary>
    /// Game modes cannot be registered because there are no dlls found in the target path.
    /// </summary>
    /// <inheritdoc cref="Exception"/>
    public class GameModeRegistryException
        : Exception
    {
        public GameModeRegistryException(string path, string message = null)
            : base(GetMessage(path, message))
        {

        }

        private static string GetMessage(string path, string message)
        {
            var msg = string.IsNullOrWhiteSpace(message) ? null : (". " + message);

            var str = $"No game modes found to register in target path: \"{path}\"" + msg;

            return str;
        }
    }
}
