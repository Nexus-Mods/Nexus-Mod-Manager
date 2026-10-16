using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Nexus.Client.ModManagement
{
	public partial class VirtualModLink : IVirtualModLink, IEquatable<VirtualModLink>
	{
		/// <summary>Notifies the native store tracker when mutable link metadata changes.</summary>
		internal event EventHandler PersistenceChanged;

		#region Properties
		
		private string _persistedVirtualModPath;
		public string VirtualModPath
		{
			get { return _persistedVirtualModPath; }
			set
			{
				if (EqualityComparer<string>.Default.Equals(_persistedVirtualModPath, value)) return;
				_persistedVirtualModPath = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		private string _persistedRealModPath;
		public string RealModPath
		{
			get { return _persistedRealModPath; }
			set
			{
				if (EqualityComparer<string>.Default.Equals(_persistedRealModPath, value)) return;
				_persistedRealModPath = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		private int _persistedPriority;
		public int Priority
		{
			get { return _persistedPriority; }
			set
			{
				if (EqualityComparer<int>.Default.Equals(_persistedPriority, value)) return;
				_persistedPriority = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		private bool _persistedActive;
		public bool Active
		{
			get { return _persistedActive; }
			set
			{
				if (EqualityComparer<bool>.Default.Equals(_persistedActive, value)) return;
				_persistedActive = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		private IVirtualModInfo _persistedModInfo;
		public IVirtualModInfo ModInfo
		{
			get { return _persistedModInfo; }
			set
			{
				if (ReferenceEquals(_persistedModInfo, value)) return;
				_persistedModInfo = value;
				PersistenceChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModInfo)));
			}
		}
		private ModInstallRoot _persistedInstallRoot;
		public ModInstallRoot InstallRoot
		{
			get { return _persistedInstallRoot; }
			set
			{
				if (EqualityComparer<ModInstallRoot>.Default.Equals(_persistedInstallRoot, value)) return;
				_persistedInstallRoot = value;
				PersistenceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		
		#endregion

		#region Constructors

		public VirtualModLink(string p_strRealPath, string p_strVirtualPath, int p_intPriority, bool p_booActive, IVirtualModInfo p_vmiVirtualModInfo)
			: this(p_strRealPath, p_strVirtualPath, p_intPriority, p_booActive, p_vmiVirtualModInfo, ModInstallRoot.Default)
		{
		}

		public VirtualModLink(string p_strRealPath, string p_strVirtualPath, int p_intPriority, bool p_booActive, IVirtualModInfo p_vmiVirtualModInfo, ModInstallRoot p_mirInstallRoot)
		{
			VirtualModPath = p_strVirtualPath;
			RealModPath = p_strRealPath;
			Priority = p_intPriority;
			Active = p_booActive;
			ModInfo = p_vmiVirtualModInfo;
			InstallRoot = p_mirInstallRoot;
		}

		public VirtualModLink(IVirtualModLink p_vmlLink)
		{
			VirtualModPath = p_vmlLink.VirtualModPath;
			RealModPath = p_vmlLink.RealModPath;
			Priority = p_vmlLink.Priority;
			Active = p_vmlLink.Active;
			ModInfo = p_vmlLink.ModInfo;
			InstallRoot = p_vmlLink.InstallRoot;
		}

	#endregion

	#region IEquatable<IVirtualModLink>

	public bool Equals(IVirtualModLink other)
		{
			if (other == null) return false;
			return (this.RealModPath.Equals(other.RealModPath, StringComparison.InvariantCultureIgnoreCase) && this.VirtualModPath.Equals(other.VirtualModPath, StringComparison.InvariantCultureIgnoreCase));
		}

		public bool Equals(VirtualModLink other)
		{
			if (other == null) return false;
			return (this.RealModPath.Equals(other.RealModPath, StringComparison.InvariantCultureIgnoreCase) && this.VirtualModPath.Equals(other.VirtualModPath, StringComparison.InvariantCultureIgnoreCase));
		}

		#endregion
	}
}
