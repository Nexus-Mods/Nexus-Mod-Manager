using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BuildTasks
{
	/// <summary>
	/// A custom MSBuild task to autoincrement assembly versions.
	/// </summary>
	public class AutoIncrementVersionBuildTask : Task
	{
		/// <summary>
		/// Encapsulates some metadata about assembly info files.
		/// </summary>
		private class AssemblyInfo
		{
			private static Regex m_rgxAssemblyVersion = new Regex(@"^\s*\[\s*assembly\s*:\s*AssemblyVersion\s*\(\s*""([^""]+)""\s*\)\s*\]", RegexOptions.Multiline);
			private static Regex m_rgxAssemblyFileVersion = new Regex(@"^\s*\[\s*assembly\s*:\s*AssemblyFileVersion\s*\(\s*""([^""]+)""\s*\)\s*\]", RegexOptions.Multiline);
			private static Regex m_rgxAssemblyFileVersionFormat = new Regex(@"//\s*AssemblyFileVersionFormat\s*\(\s*""([^""]+)""\s*\)");

			#region Properties

			/// <summary>
			/// Gets or sets the assembly version.
			/// </summary>
			/// <value>The assembly version.</value>
			public string AssemblyVersion { get; set; }

			/// <summary>
			/// Gets the assembly version's line number in the file.
			/// </summary>
			/// <value>The assembly version's line number in the file.</value>
			public Int32 AssemblyVersionLine { get; private set; }

			/// <summary>
			/// Gets the assembly version's column in the file.
			/// </summary>
			/// <value>The assembly version's column in the file.</value>
			public Int32 AssemblyVersionColumn { get; private set; }

			/// <summary>
			/// Gets or sets the assembly file version.
			/// </summary>
			/// <value>The assembly file version.</value>
			public string AssemblyFileVersion { get; set; }

			/// <summary>
			/// Gets or sets the assembly file version format.
			/// </summary>
			/// <value>The assembly file version format.</value>
			public string AssemblyFileVersionFormat { get; set; }

			/// <summary>
			/// Gets the assembly file version's line number in the file.
			/// </summary>
			/// <value>The assembly file version's line number in the file.</value>
			public Int32 AssemblyFileVersionLine { get; private set; }

			/// <summary>
			/// Gets the assembly file version's column in the file.
			/// </summary>
			/// <value>The assembly file version's column in the file.</value>
			public Int32 AssemblyFileVersionColumn { get; private set; }

			/// <summary>
			/// Gets the path to the assembly info file.
			/// </summary>
			/// <value>The path to the assembly info file.</value>
			public string FilePath { get; private set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_strFilePath">The path to the assembly info file.</param>
			public AssemblyInfo(string p_strFilePath)
			{
				FilePath = p_strFilePath;
				Parse();
			}

			#endregion

			/// <summary>
			/// Parses out the metadata we are interested in.
			/// </summary>
			protected void Parse()
			{
				string strAssemblyInfo = File.ReadAllText(FilePath);
				string[] strAssemblyInfoLines = File.ReadAllLines(FilePath);
				Match mchVersion = m_rgxAssemblyVersion.Match(strAssemblyInfo);
				if (mchVersion.Success)
				{
					AssemblyVersion = mchVersion.Groups[1].Value;
					AssemblyVersionColumn = mchVersion.Groups[1].Index - mchVersion.Groups[0].Index + 1;
					AssemblyVersionLine = Array.IndexOf(strAssemblyInfoLines, mchVersion.Groups[0].Value) + 1;
				}
				mchVersion = m_rgxAssemblyFileVersion.Match(strAssemblyInfo);
				if (mchVersion.Success)
				{
					AssemblyFileVersion = mchVersion.Groups[1].Value;
					AssemblyFileVersionColumn = mchVersion.Groups[1].Index - mchVersion.Groups[0].Index + 1;
					AssemblyFileVersionLine = Array.IndexOf(strAssemblyInfoLines, mchVersion.Groups[0].Value) + 1;
				}
				mchVersion = m_rgxAssemblyFileVersionFormat.Match(strAssemblyInfo);
				if (mchVersion.Success)
					AssemblyFileVersionFormat = mchVersion.Groups[1].Value;
			}

			/// <summary>
			/// Generate a new assembly info file using the current values.
			/// </summary>
			/// <returns>A new assembly info file containing the current values.</returns>
			public string GenerateFile()
			{
				string strAssemblyInfo = File.ReadAllText(FilePath);
				strAssemblyInfo = m_rgxAssemblyVersion.Replace(strAssemblyInfo, String.Format(@"[assembly: AssemblyVersion(""{0}"")]", AssemblyVersion));
				strAssemblyInfo = m_rgxAssemblyFileVersion.Replace(strAssemblyInfo, String.Format(@"[assembly: AssemblyFileVersion(""{0}"")]", AssemblyFileVersion));
				return strAssemblyInfo;
			}
		}

		#region Properties

		/// <summary>
		/// Gets or sets the assembly info file that need to be updated.
		/// </summary>
		/// <value>The assembly info file that need to be updated.</value>
		[Required]
		public ITaskItem[] AssemblyInfoFiles { get; set; }

		#endregion

		/// <summary>
		/// Runs the task.
		/// </summary>
		/// <remarks>
		/// This autoincrements assembly verions.
		/// </remarks>
		/// <returns><c>true</c> if the task completed successfully;
		/// <c>false</c> otherwise.</returns>
		public override bool Execute()
		{
			List<AssemblyInfo> lstFilesToSave = new List<AssemblyInfo>();
			foreach (ITaskItem titAssemblyInfoFile in AssemblyInfoFiles)
			{
				string strInfoFilePath = titAssemblyInfoFile.ItemSpec;
				if (!File.Exists(strInfoFilePath))
				{
					Log.LogError("Assembly Version Auto Increment", null, null, strInfoFilePath, 0, 0, 0, 0, "Unable to find Assembly Info File.");
					return false;
				}

				AssemblyInfo asiInfo = new AssemblyInfo(strInfoFilePath);
				if (asiInfo.AssemblyVersion.Split('.').Length != 2)
				{
					Log.LogError("Assembly Version Auto Increment", null, null, strInfoFilePath, asiInfo.AssemblyVersionLine, asiInfo.AssemblyVersionColumn, asiInfo.AssemblyVersionLine, asiInfo.AssemblyVersionColumn + asiInfo.AssemblyVersion.Length, "AssemblyVersion is not in Major.Minor form (Build and Revision are not allowed).");
					return false;
				}

				DateTime dteEpoch = new DateTime(2000, 01, 01, 00, 00, 00, DateTimeKind.Utc);
				Int32 intDaysSinceEpoch = (Int32)DateTime.UtcNow.Subtract(dteEpoch).TotalDays;
				DateTime dteMidnight = DateTime.UtcNow;
				dteMidnight = new DateTime(dteMidnight.Year, dteMidnight.Month, dteMidnight.Day, 0, 0, 0, DateTimeKind.Utc);
				Int32 intBiSecondsSinceMidnight = (Int32)DateTime.UtcNow.Subtract(dteMidnight).TotalSeconds / 2;

				if (String.IsNullOrEmpty(asiInfo.AssemblyFileVersionFormat))
				{
					Log.LogError("Assembly Version Auto Increment", null, null, strInfoFilePath, 0, 0, 0, 0, "AssemblyFileVersionFormat not found.");
					return false;
				}

				//Log.LogMessage("Setting AsseblyVersion: {0}", asiInfo.AssemblyVersion);
				string strAssemblyFileVersionFormat = null;
				switch (asiInfo.AssemblyFileVersionFormat.Split('.').Length)
				{
					case 0:
					case 1:
					case 2:
						Log.LogError("Assembly Version Auto Increment", null, null, strInfoFilePath, 0, 0, 0, 0, "AssemblyFileVersionFormat must be 1.1.* or 1.1.1.*");
						return false;
					case 3:
						strAssemblyFileVersionFormat = "{0}.{1}.{2}";
						break;
					case 4:
						strAssemblyFileVersionFormat = "{0}.{2}";
						lstFilesToSave.Add(asiInfo);
						break;
				}
				asiInfo.AssemblyFileVersion = String.Format(strAssemblyFileVersionFormat, asiInfo.AssemblyVersion, intDaysSinceEpoch, intBiSecondsSinceMidnight);
				lstFilesToSave.Add(asiInfo);
				Log.LogMessage("Setting AsseblyFileVersion: {0}", asiInfo.AssemblyFileVersion);				
			}

			foreach (AssemblyInfo asiInfo in lstFilesToSave)
			{
				string strTempFile = Path.GetTempFileName();
				File.WriteAllText(strTempFile, asiInfo.GenerateFile());
				File.Copy(strTempFile, asiInfo.FilePath, true);
				File.Delete(strTempFile);
			}
			return true;
		}
	}
}
