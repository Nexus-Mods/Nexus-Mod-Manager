﻿namespace Nexus.Client.Util
{
    /// <summary>
    /// Data that is commonly accessed throughout the solution.
    /// </summary>
    public static class CommonData
    {
        /// <summary>
        /// The string representation of the version of the application.
        /// </summary>
        /// <remarks>
        /// The version is in the form: a.b.c
        /// 
        /// (a) should change when there is a major alteration to the program.
        /// Something akin to a UI overhaul, or a major new feature that alters
        /// how users will interact with the application.
        /// 
        /// (b) should change when there is a normal alteration to the program.
        /// Something akin to regular maintenance, a major bug fix, or a simple
        /// new feature addition.
        /// 
        /// (c) should change when there is a minor alteration to the program.
        /// Something akin to a minor bug fix, or a typo correction.
        /// </remarks>
        public const string VersionString = "0.88.2";

        /// <summary>
        /// Gets the full name of the mod manager.
        /// </summary>
        public const string ModManagerName = "Nexus Mod Manager";
    }
}
