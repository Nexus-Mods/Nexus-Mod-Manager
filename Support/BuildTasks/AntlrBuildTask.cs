using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BuildTasks
{
	/// <summary>
	/// A custom MSBuild task to compile Antlr grammar files.
	/// </summary>
	/// <remarks>
	/// The discovered grammar files are only compiled if they have changed since their last compilation.
	/// </remarks>
	public class AntlrBuildTask : Task
	{
		private Regex m_rgxImport = new Regex(@"import\s+([^;]+);", RegexOptions.IgnoreCase);

		#region Properties

		/// <summary>
		/// Gets or sets the path to the grammar file to compile.
		/// </summary>
		/// <value>The path to the grammar file to compile.</value>
		[Required]
		public string GrammarFile { get; set; }

		/// <summary>
		/// Gets or sets the list of all grammar files in the solution.
		/// </summary>
		/// <remarks>
		/// This list is used to discover imported grammars.
		/// </remarks>
		/// <value>The list of all grammar files in the solution.</value>
		public ITaskItem[] LibraryGrammarFiles { get; set; }

		/// <summary>
		/// Gets or set the path to the Antlr jar.
		/// </summary>
		/// <value>The path to the Antlr jar.</value>
		[Required]
		public string AntlrJarPath { get; set; }

		/// <summary>
		/// Gets or sets the path to the JVM executable (java.exe).
		/// </summary>
		/// <value>The path to the JVM executable (java.exe).</value>
		[Required]
		public string JavaPath { get; set; }

		#endregion

		/// <summary>
		/// Runs the task.
		/// </summary>
		/// <remarks>
		/// This checks to see if the current grammar file needs to be compiled. If it is out out
		/// date, it is compiled.
		/// </remarks>
		/// <returns><c>true</c> if the task completed successfully;
		/// <c>false</c> otherwise.</returns>
		public override bool Execute()
		{
			List<string> lstLibraryGrammars = new List<string>();
			foreach (ITaskItem titGrammar in LibraryGrammarFiles)
				if (!titGrammar.ItemSpec.Equals(GrammarFile))
					lstLibraryGrammars.Add(titGrammar.ItemSpec);

			List<string> lstLibPaths = new List<string>();
			string strGrammarFile = GrammarFile;
			string strImportGrammar = null;
			string strOutputFileBase = Path.Combine(Path.GetDirectoryName(strGrammarFile), Path.GetFileNameWithoutExtension(strGrammarFile));
			while (true)
			{
				string strOutputFile = strOutputFileBase + ".cs";
				FileInfo fifGrammar = new FileInfo(strGrammarFile);
				FileInfo fifOutput = File.Exists(strOutputFile) ? new FileInfo(strOutputFile) : null;

				strGrammarFile = FindImportGrammarFile(strGrammarFile, out strImportGrammar, lstLibraryGrammars);
				strOutputFileBase += "_" + strImportGrammar;

				if (!String.IsNullOrEmpty(strImportGrammar))
				{
					//we found an import grammar, but not the import grammar file, so fail
					if (String.IsNullOrEmpty(strGrammarFile))
						return false;

					if (!lstLibPaths.Contains(Path.GetDirectoryName(strGrammarFile), StringComparer.OrdinalIgnoreCase))
						lstLibPaths.Add(Path.GetDirectoryName(strGrammarFile));
				}

				if ((fifOutput != null) && (fifOutput.LastWriteTimeUtc > fifGrammar.LastWriteTimeUtc))
				{
					//if the output file is newer than the grammar file,
					// and there is no imported grammar, we're done
					if (String.IsNullOrEmpty(strImportGrammar))
						return true;
				}
				else
					break;
			}

			StringBuilder stbCompileCommand = new StringBuilder();
			stbCompileCommand.AppendFormat(@"-jar ""{0}"" ", AntlrJarPath);
			foreach (string strLibPath in lstLibPaths)
				stbCompileCommand.AppendFormat(@"-lib ""{0}"" ", strLibPath);
			stbCompileCommand.AppendFormat("\"{0}\"", GrammarFile);

			ProcessStartInfo psiStartInfo = new ProcessStartInfo(JavaPath, stbCompileCommand.ToString());
			psiStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			psiStartInfo.CreateNoWindow = true;
			psiStartInfo.UseShellExecute = false;
			psiStartInfo.RedirectStandardError = true;

			Log.LogMessage(MessageImportance.Low, "ANTLR: Compiling: {0}", GrammarFile);
			Process pcsCompile = Process.Start(psiStartInfo);
			if (pcsCompile == null)
			{
				Log.LogError("ANTLR", null, null, GrammarFile, 0, 0, 0, 0, "ANTLR: Could not start java.", GrammarFile);
				return false;
			}
			pcsCompile.WaitForExit();
			if (pcsCompile.ExitCode != 0)
			{
				string strErrors = pcsCompile.StandardError.ReadToEnd();
				Log.LogError("ANTLR", null, null, GrammarFile, 0, 0, 0, 0, "ANTLR: {0}", strErrors);
				return false;
			}

			//add the ignore missing comments pragma to all generated files
			// ideally we would do this with antlr natively, but the @header
			// block in the grammar files doesn't propagate to child grammars
			string[] strOutputFiles = Directory.GetFiles(Path.GetDirectoryName(GrammarFile), Path.GetFileNameWithoutExtension(GrammarFile) + "*.cs");
			foreach (string strOutputFile in strOutputFiles)
			{
				string strContents = "//turn off warning about missing comments" + Environment.NewLine +
									"#pragma warning disable 1591" + Environment.NewLine +
									"//turn off warning about not needing CLSCompliant attribute" + Environment.NewLine +
									"#pragma warning disable 3021" + Environment.NewLine +
									File.ReadAllText(strOutputFile);
				File.WriteAllText(strOutputFile, strContents);
			}

			return true;
		}

		/// <summary>
		/// Checks the specified grammar file for imported grammars.
		/// </summary>
		/// <param name="p_strGrammarFile">the grammar file to check for imported grammars.</param>
		/// <param name="p_strImportGrammar">An out parameter that will be set to the name of the
		/// found imported grammar, or <c>null</c> if there was no imported grammar.</param>
		/// <param name="p_lstLibraries">The list of all grammar files in the solution.</param>
		/// <returns>The filename of the imported grammar.</returns>
		protected string FindImportGrammarFile(string p_strGrammarFile, out string p_strImportGrammar, List<string> p_lstLibraries)
		{
			Match mchImport = m_rgxImport.Match(File.ReadAllText(p_strGrammarFile));

			//no grammar was imported, so we're done
			if (!mchImport.Success)
			{
				p_strImportGrammar = null;
				return null;
			}

			string strImportGrammar = mchImport.Groups[1].Value;
			p_strImportGrammar = strImportGrammar;
			string strImportGrammarFile = p_lstLibraries.Find(x => x.EndsWith(Path.AltDirectorySeparatorChar + strImportGrammar + ".g", StringComparison.OrdinalIgnoreCase) || x.EndsWith(Path.DirectorySeparatorChar + strImportGrammar + ".g", StringComparison.OrdinalIgnoreCase));
			if (String.IsNullOrEmpty(strImportGrammarFile) || !File.Exists(strImportGrammarFile))
			{
				//we couldn't find the imported grammar
				string[] strFileContents = File.ReadAllLines(p_strGrammarFile);
				Int32 intCol = mchImport.Groups[1].Index - mchImport.Groups[0].Index + 1;
				Int32 intRow = Array.IndexOf(strFileContents, mchImport.Groups[0].Value) + 1;
				Log.LogError("ANTLR", null, null, GrammarFile, intRow, intCol, intRow, intCol + mchImport.Groups[1].Length, "ANTLR: Imported grammar '{0}' could not be found.", strImportGrammar);
				return null; ;
			}
			return strImportGrammarFile;
		}
	}
}
