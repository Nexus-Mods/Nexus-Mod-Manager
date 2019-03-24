namespace Nexus.Client.Util
{
    using System;
    using System.IO;
    using System.Security.Cryptography;

    /// <summary>
    /// Utility class for MD5-related functions.
    /// </summary>
    public static class Md5
    {
        /// <summary>
        /// Generates an MD5 checksum of a given file.
        /// </summary>
        /// <param name="filename">Path to file to generate checksum for.</param>
        /// <returns>Plain string of MD5 hash, without dashes.</returns>
        /// <remarks>Taken from: https://stackoverflow.com/a/10520086/1728343 </remarks>
        public static string CalculateMd5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
    }
}
