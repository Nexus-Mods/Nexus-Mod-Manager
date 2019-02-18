namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Collections.Generic;
    using RestSharp;

    /// <summary>
    /// Keeps track of current rate limit against the API.
    /// </summary>
    public class RateLimit
    {
        public RateLimit()
        {
            HourlyLimit = -1;
            HourlyLimitRemaining = -1;
            HourlyLimitReset = DateTime.MinValue;

            DailyLimit = -1;
            DailyLimitRemaining = -1;
            DailyLimitReset = DateTime.MinValue;
        }

        /// <summary>
        /// The maximum API calls allowed per hour.
        /// </summary>
        public int HourlyLimit { get; private set; }

        /// <summary>
        /// Number of API calls remaining of the hourly limit.
        /// </summary>
        public int HourlyLimitRemaining { get; private set; }

        /// <summary>
        /// Time when the hourly limit is reset.
        /// </summary>
        public DateTime HourlyLimitReset { get; private set; }

        /// <summary>
        /// The maximum API calls allowed per day.
        /// </summary>
        public int DailyLimit { get; private set; }

        /// <summary>
        /// Number of API calls remaining of the daily limit.
        /// </summary>
        public int DailyLimitRemaining { get; private set; }

        /// <summary>
        /// Time when the daily limit is reset.
        /// </summary>
        public DateTime DailyLimitReset { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="headers"></param>
        public void Update(IList<Parameter> headers)
        {
            if (headers == null)
            {
                return;
            }

            foreach (var header in headers)
            {
                if (header.Name.Equals("X-RL-Hourly-Limit"))
                {
                    HourlyLimit = Convert.ToInt32(header.Value);
                }
                else if (header.Name.Equals("X-RL-Hourly-Remaining"))
                {
                    HourlyLimitRemaining = Convert.ToInt32(header.Value);
                }
                else if (header.Name.Equals("X-RL-Hourly-Reset"))
                {
                    HourlyLimitReset = DateTime.Parse(header.Value.ToString());
                }
                else if (header.Name.Equals("X-RL-Daily-Limit"))
                {
                    DailyLimit = Convert.ToInt32(header.Value);
                }
                else if (header.Name.Equals("X-RL-Daily-Remaining"))
                {
                    DailyLimitRemaining = Convert.ToInt32(header.Value);
                }
                else if (header.Name.Equals("X-RL-Daily-Reset"))
                {
                    DailyLimitReset = DateTime.Parse(header.Value.ToString());
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"Hourly - Limit: {HourlyLimit}, Remaining: {HourlyLimitRemaining}, Reset: {HourlyLimitReset}\n" +
                   $"Daily - Limit: {DailyLimit}, Remaining: {DailyLimitRemaining}, Rest: {DailyLimitReset}";
        }
    }
}
