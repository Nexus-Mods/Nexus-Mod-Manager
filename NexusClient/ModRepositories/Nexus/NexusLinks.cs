﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.ModRepositories.Nexus
{
	public static class NexusLinks
	{

		#region Properties

		public static string FAQs
		{
			get
			{
				return @"https://forums.nexusmods.com/index.php?/topic/721054-read-here-first-nexus-mod-manager-frequent-issues/";
			}
		}

		public static string Issues
		{
			get
			{
				return @"https://github.com/Nexus-Mods/Nexus-Mod-Manager/issues";
			}
		}

		public static string NexusURI
		{
			get
			{
				return @"https://www.nexusmods.com";
			}
		}

		public static string Premium
		{
			get
			{
				return @"https://www.nexusmods.com/register/premium";
			}
		}

		public static string LatestVersion
		{
			get
			{
				return @"https://legacy-api.nexusmods.com/NMM?GetLatestVersion";
			}
		}

		public static string LatestVersion4dot5
		{
			get
			{
				return @"https://dev.nexusmods.com/client/4.5/latestversion.php";
			}
		}

		public static string Releases
		{
			get
			{
				return @"https://github.com/Nexus-Mods/Nexus-Mod-Manager/releases";
			}
		}

		

		#endregion
	}
}
