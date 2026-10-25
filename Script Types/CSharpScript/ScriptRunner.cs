using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.ModManagement.Scripting.CSharpScript
{
	/// <summary>
	/// Runs a C# script.
	/// </summary>
	/// <remarks>
	/// This class is meant to be run in a sandboxed domain in order to limit the possible
	/// damage from malicious or poorly written code.
	/// </remarks>
	public class ScriptRunner : MarshalByRefObject
	{
		private CSharpScriptFunctionProxy m_csfFunctions = null;

		#region Constructors
		
		/// <summary>
		/// A parameterless constructor used when the runner is activated across an AppDomain boundary.
		/// </summary>
		public ScriptRunner()
		{
		}

		/// <summary>
		/// A simple construtor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_csfFunctions">The object that implements the script functions.</param>
		public ScriptRunner(CSharpScriptFunctionProxy p_csfFunctions)
		{
			Initialize(p_csfFunctions);
		}

		/// <summary>
		/// Supplies the host-side script function proxy after cross-AppDomain activation.
		/// </summary>
		/// <param name="p_csfFunctions">The object that implements the script functions.</param>
		public void Initialize(CSharpScriptFunctionProxy p_csfFunctions)
		{
			m_csfFunctions = p_csfFunctions;
		}

		#endregion

		/// <summary>
		/// Executes the given script.
		/// </summary>
		/// <param name="p_bteScript">The bytes of the assembly containing the script to execute.</param>
		/// <returns><c>true</c> if the script completes successfully;
		/// <c>false</c> otherwise.</returns>
		public bool Execute(byte[] p_bteScript)
		{
			EventHandler attachWindows = null;
			try
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Control.CheckForIllegalCrossThreadCalls = false;
				if (m_csfFunctions != null && m_csfFunctions.IsCollectionCapture)
				{
					var attached = new HashSet<Form>();
					attachWindows = (sender, args) =>
					{
						foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
							if (form.Visible && !form.IsDisposed && attached.Add(form))
								m_csfFunctions.AttachInstallerWindow(form.Handle);
					};
					Application.Idle += attachWindows;
				}

				Assembly asmScript = Assembly.Load(p_bteScript);

				object s = asmScript.CreateInstance("Script");
				//s = new Script();
				if (s == null)
				{
					m_csfFunctions.MessageBox("C# Script did not contain a 'Script' class in the root namespace.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

				MethodInfo mifMethod = null;
				for (Type tpeScriptType = s.GetType(); mifMethod == null; tpeScriptType = tpeScriptType.BaseType)
					mifMethod = tpeScriptType.GetMethod("Setup", new Type[] { typeof(CSharpScriptFunctionProxy) });
				mifMethod.Invoke(s, new object[] { m_csfFunctions });
				return (bool)s.GetType().GetMethod("OnActivate").Invoke(s, null);
			}
			catch (Exception ex)
			{
				if (m_csfFunctions != null && m_csfFunctions.IsCollectionCapture)
					throw new InvalidOperationException("The C# installer failed while preparing Collection choices.", ex);

				StringBuilder stbException = new StringBuilder(ex.ToString());
				while (ex.InnerException != null)
				{
					ex = ex.InnerException;
					stbException.AppendLine().AppendLine().Append(ex.ToString());
				}
				string strMessage = "An exception occured in the script.";
				m_csfFunctions.ExtendedMessageBox(strMessage, "Error", stbException.ToString(), MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
			finally
			{
				if (attachWindows != null) Application.Idle -= attachWindows;
			}
		}
	}
}
