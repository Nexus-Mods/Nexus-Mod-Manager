using System.Reflection;
using Nexus.Client.Util;

// Versions
[assembly: AssemblyVersion(CommonData.VersionString + ".0")]
[assembly: AssemblyFileVersion(CommonData.VersionString + ".0")]
[assembly: AssemblyInformationalVersion(CommonData.VersionString + ".0")]

// Other shared information
[assembly: AssemblyCompany("Black Tree Gaming")]
[assembly: AssemblyCopyright("Copyright © Black Tree Gaming 2018")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif