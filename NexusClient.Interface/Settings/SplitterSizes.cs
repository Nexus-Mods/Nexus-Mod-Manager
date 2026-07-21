using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// Stores splitter distances.
	/// </summary>
	public class SplitterSizes : KeyedSettings<SettingsList>
	{
		/// <summary>
		/// Stores the given control's splitter distances using the given name.
		/// </summary>
		/// <remarks>
		/// This method recurses through the control tree and stores the splitter distances
		/// of all nested splitter containers.
		/// </remarks>
		/// <param name="p_strName">The name under which to store the splitter distances.</param>
		/// <param name="p_spcSplitter">The control whose splitter distances are to be stored.</param>
		public void SaveSplitterSizes(string p_strName, SplitContainer p_spcSplitter)
		{
			List<Int32> lstWidths = new List<Int32>();
			Stack<SplitContainer> stkSplitters = new Stack<SplitContainer>();
			stkSplitters.Push(p_spcSplitter);
			while (stkSplitters.Count > 0)
			{
				SplitContainer spcCurrent = stkSplitters.Pop();
				lstWidths.Add(spcCurrent.SplitterDistance);
				foreach (Control ctlSplitter in spcCurrent.Panel1.Controls)
					if (ctlSplitter is SplitContainer)
						stkSplitters.Push((SplitContainer)ctlSplitter);
				foreach (Control ctlSplitter in spcCurrent.Panel2.Controls)
					if (ctlSplitter is SplitContainer)
						stkSplitters.Push((SplitContainer)ctlSplitter);
			}
			this[p_strName] = lstWidths;
		}

		/// <summary>
		/// Sets the splitter distances in the given control based on the stored values.
		/// </summary>
		/// <remarks>
		/// This method recurses through the control tree and sets the splitter distances
		/// of all nested splitter containers.
		/// </remarks>
		/// <param name="p_strName">The name of the settings to use to size the splitter containers.</param>
		/// <param name="p_spcSplitter">The control whose splitter distances are to be set.</param>
		public void LoadSplitterSizes(string p_strName, SplitContainer p_spcSplitter)
		{
			try
			{
				List<Int32> lstWidths = this[p_strName];
				if (lstWidths == null)
					return;
				Queue<Int32> queWidths = new Queue<Int32>(lstWidths);
				Stack<SplitContainer> stkSplitters = new Stack<SplitContainer>();
				stkSplitters.Push(p_spcSplitter);
				while (queWidths.Count > 0)
				{
					SplitContainer spcCurrent = stkSplitters.Pop();
					spcCurrent.SplitterDistance = queWidths.Dequeue();
					foreach (Control ctlSplitter in spcCurrent.Panel1.Controls)
						if (ctlSplitter is SplitContainer)
							stkSplitters.Push((SplitContainer)ctlSplitter);
					foreach (Control ctlSplitter in spcCurrent.Panel2.Controls)
						if (ctlSplitter is SplitContainer)
							stkSplitters.Push((SplitContainer)ctlSplitter);
				}
			}
			catch { }
		}
	}
}
