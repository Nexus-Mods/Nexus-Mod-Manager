namespace Nexus.Client.Util
{
    using System;

    public static class GameDomainTranslator
    {
        /// <summary>
        /// Translates improper game domains to proper ones.
        /// </summary>
        /// <param name="currentGameDomain">Input to translate.</param>
        /// <returns>The proper game domain.</returns>
        public static string DetermineGameDomain(string currentGameDomain)
        {
            if (currentGameDomain.Equals("fallout4vr", StringComparison.OrdinalIgnoreCase))
            {
                return "fallout4";
            }

            if (currentGameDomain.Equals("skyrimvr", StringComparison.OrdinalIgnoreCase))
            {
                return "skyrimspecialedition";
            }

            if (currentGameDomain.Equals("skyrimse", StringComparison.OrdinalIgnoreCase))
            {
                return "skyrimspecialedition";
            }

            if (currentGameDomain.Equals("falloutnv", StringComparison.OrdinalIgnoreCase))
            {
                return "newvegas";
            }

            return currentGameDomain;
        }
    }
}
