using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.TipsManagement
{
	interface ITipsManager
	{
		#region Properties

		/// <summary>
		/// Gets the list of tips.
		/// </summary>
		/// <value>The list of tips.</value>
		ThreadSafeObservableList<ITips> Tips { get; }

		#endregion
	}
}
