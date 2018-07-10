using System;
using System.Collections.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI.Controls.NodeEditors
{
	public class InstallableFileVM : ObservableObject, IEquatable<InstallableFileVM>
	{
		private bool m_booAlwaysInstall = false;
		private bool m_booInstallIfUsable = false;
		private string m_strDestination = null;
		private string m_strSource = null;
		private Int32 m_intPriority = 0;

		#region Properties

		protected InstallableFile InstallableFile { get; private set; }

		public bool AlwaysInstall
		{
			get
			{
				return m_booAlwaysInstall;
			}
			set
			{
				SetPropertyIfChanged(ref m_booAlwaysInstall, value, () => AlwaysInstall);
			}
		}

		public bool InstallIfUsable
		{
			get
			{
				return m_booInstallIfUsable;
			}
			set
			{
				SetPropertyIfChanged(ref m_booInstallIfUsable, value, () => InstallIfUsable);
			}
		}

		public string Destination
		{
			get
			{
				return m_strDestination;
			}
			set
			{
				SetPropertyIfChanged(ref m_strDestination, value, () => Destination);
			}
		}

		public string Source
		{
			get
			{
				return m_strSource;
			}
			set
			{
				SetPropertyIfChanged(ref m_strSource, value, () => Source);
			}
		}

		public Int32 Priority
		{
			get
			{
				return m_intPriority;
			}
			set
			{
				SetPropertyIfChanged(ref m_intPriority, value, () => Priority);
			}
		}

		#endregion

		public InstallableFileVM(InstallableFile p_iflInstallableFile)
		{
			InstallableFile = p_iflInstallableFile ?? new InstallableFile(null, null, false, 0, false, false);
			Reset();
		}

		public InstallableFile Commit()
		{
			InstallableFile.AlwaysInstall = AlwaysInstall;
			InstallableFile.Destination = Destination;
			InstallableFile.InstallIfUsable = InstallIfUsable;
			InstallableFile.Priority = Priority;
			InstallableFile.Source = Source;
			return InstallableFile;
		}

		public void Reset()
		{
			AlwaysInstall = InstallableFile.AlwaysInstall;
			Destination = InstallableFile.Destination;
			InstallIfUsable = InstallableFile.InstallIfUsable;
			Priority = InstallableFile.Priority;
			Source = InstallableFile.Source;
		}

		#region IEquatable<InstallableFileVM> Members

		public bool Equals(InstallableFileVM other)
		{
			return InstallableFile == other.InstallableFile;
		}

		#endregion
	}

	public class InstallableFileEditorVM : ObservableObject, IViewModel
	{
		public event EventHandler InstallableFileValidated = delegate { };
		public event EventHandler<EventArgs<InstallableFile>> InstallableFileSaved = delegate { };

		private InstallableFileVM m_ifmInstallableFileVM = null;

		public IList<VirtualFileSystemItem> FileSystemItems { get; private set; }

		public InstallableFile InstallableFile
		{
			set
			{
				InstallableFileVM = new InstallableFileVM(value);
			}
		}

		public InstallableFileVM InstallableFileVM
		{
			get
			{
				return m_ifmInstallableFileVM;
			}
			private set
			{
				SetPropertyIfChanged(ref m_ifmInstallableFileVM, value, () => InstallableFileVM);
			}
		}

		public ErrorContainer Errors { get; private set; }

		public InstallableFileEditorVM(InstallableFile p_iflInstallableFile, IList<VirtualFileSystemItem> p_lstFileSystemItems)
		{
			InstallableFile = p_iflInstallableFile;
			FileSystemItems = p_lstFileSystemItems;
			Errors = new ErrorContainer();
		}

		public void SaveInstallableFile()
		{
			if (Validate())
			{
				InstallableFileSaved(this, new EventArgs<InstallableFile>(InstallableFileVM.Commit()));
				InstallableFileVM = new InstallableFileVM(null);
			}
		}

		/// <summary>
		/// Ensures that the source is valid.
		/// </summary>
		/// <returns><c>true</c> if the source is valid;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateSource()
		{
			bool booIsValid = true;
			Errors.Clear();
			if (String.IsNullOrEmpty(InstallableFileVM.Source))
			{
				Errors.SetError(() => InstallableFileVM.Source, "You must select a source.");
				booIsValid = false;
			}
			InstallableFileValidated(this, new EventArgs());
			return booIsValid;
		}

		#region IViewModel Members

		public bool Validate()
		{
			return ValidateSource();
		}

		#endregion
	}
}
