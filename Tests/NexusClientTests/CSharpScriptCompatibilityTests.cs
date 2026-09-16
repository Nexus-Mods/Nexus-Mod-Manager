using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.Threading;
using Nexus.Client;
using Nexus.Client.Games.Fallout3.Scripting.CSharpScript;
using Nexus.Client.ModManagement.Scripting.CSharpScript;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Characterizes the .NET Framework runtime requirements of the C# scripted installer engine.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class CSharpScriptCompatibilityTests
    {
        private const string ValidScript = @"
using Nexus.Client.ModManagement.Scripting.CSharpScript;

public class Script : CSharpBaseScript
{
    public bool OnActivate()
    {
        return true;
    }
}";

        /// <summary>
        /// Verifies that the CodeDOM compiler can still produce a loadable script assembly.
        /// </summary>
        [Test]
        public void Compiler_ValidScript_ProducesAssembly()
        {
            CompilerErrorCollection errors;
            byte[] assembly = new CSharpScriptCompiler().Compile(ValidScript, typeof(CSharpBaseScript), out errors);

            Assert.That(errors, Is.Null);
            Assert.That(assembly, Is.Not.Null.And.Not.Empty);
        }

        /// <summary>
        /// Verifies that a compiled script can execute repeatedly inside a restricted AppDomain and unload cleanly.
        /// </summary>
        [Test]
        public void Sandbox_ValidScript_ExecutesRepeatedlyAndUnloads()
        {
            byte[] assembly = Compile(ValidScript);

            for (int i = 0; i < 3; i++)
                Assert.That(ExecuteInSandbox(assembly), Is.True);
        }

        /// <summary>
        /// Verifies that the restricted AppDomain continues to deny filesystem access outside its grant set.
        /// </summary>
        [Test]
        public void Sandbox_UnpermittedFileRead_IsDenied()
        {
            string deniedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(deniedPath, "sandbox probe");
            try
            {
                string escapedPath = deniedPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string script = @"
using System.IO;
using System.Security;
using Nexus.Client.ModManagement.Scripting.CSharpScript;

public class Script : CSharpBaseScript
{
    public bool OnActivate()
    {
        try
        {
            File.ReadAllText(""__DENIED_PATH__"");
            return false;
        }
        catch (SecurityException)
        {
            return true;
        }
    }
}";
                script = script.Replace("__DENIED_PATH__", escapedPath);

                Assert.That(ExecuteInSandbox(Compile(script)), Is.True);
            }
            finally
            {
                File.Delete(deniedPath);
            }
        }

        /// <summary>
        /// Verifies that a script-level failure result still allows the restricted AppDomain to unload and be recreated.
        /// </summary>
        [Test]
        public void Sandbox_FailedScriptResult_DoesNotPreventNextExecution()
        {
            const string failedScript = @"
using Nexus.Client.ModManagement.Scripting.CSharpScript;

public class Script : CSharpBaseScript
{
    public bool OnActivate()
    {
        return false;
    }
}";

            Assert.That(ExecuteInSandbox(Compile(failedScript)), Is.False);
            Assert.That(ExecuteInSandbox(Compile(ValidScript)), Is.True);
        }

        /// <summary>
        /// Exercises the production executor, its sandbox construction, a real cross-domain function-proxy call,
        /// a representative game-specific base-script assembly, successful installation completion, and AppDomain cleanup.
        /// </summary>
        [Test]
        public void ProductionExecutor_GameSpecificBase_ExecutesProxyCallAndCompletes()
        {
            const string scriptCode = @"
using System;
using Nexus.Client.ModManagement.Scripting.CSharpScript;

public class Script : CSharpBaseScript
{
    public bool OnActivate()
    {
        return GetFommVersion() == new Version(9, 8, 7, 6);
    }
}";

            using (var temporaryDirectory = new TemporaryDirectory())
            {
                var context = new ScriptProxyContext(temporaryDirectory.Path, "123", false, false, null);
                IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
                {
                    switch (method.Name)
                    {
                        case "get_TemporaryPath":
                            return temporaryDirectory.Path;
                        case "get_ApplicationVersion":
                            return new Version(9, 8, 7, 6);
                        default:
                            return null;
                    }
                });

                var functionProxy = new CSharpScriptFunctionProxy(
                    context.Mod,
                    context.GameMode,
                    environmentInfo,
                    context.VirtualModActivator,
                    context.Installers,
                    null);
                var scriptType = new Fallout3CSharpScriptType();
                var script = new CSharpScript(scriptType, scriptCode);
                var executor = new CSharpScriptExecutor(
                    context.GameMode,
                    environmentInfo,
                    functionProxy,
                    typeof(Fallout3CSharpBaseScript),
                    string.Empty);

                Assert.That(executor.DoExecute(script), Is.True);
                Assert.That(context.FileInstaller.FinalizeCallCount, Is.EqualTo(1));

                // A second complete execution verifies that the production cleanup path unloaded the first sandbox cleanly.
                Assert.That(executor.DoExecute(script), Is.True);
                Assert.That(context.FileInstaller.FinalizeCallCount, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// Compiles the supplied script and asserts that the compatibility compiler accepted it.
        /// </summary>
        private static byte[] Compile(string code)
        {
            CompilerErrorCollection errors;
            byte[] assembly = new CSharpScriptCompiler().Compile(code, typeof(CSharpBaseScript), out errors);
            if (errors != null)
                Assert.Fail("C# scripted installer compilation failed: {0}", FormatErrors(errors));
            return assembly;
        }

        /// <summary>
        /// Executes a compiled script using the same restricted-domain flags required by the production executor.
        /// </summary>
        private static bool ExecuteInSandbox(byte[] assembly)
        {
            string applicationBase = Path.GetDirectoryName(typeof(ScriptRunner).Assembly.Location);
            AppDomainSetup setup = new AppDomainSetup
            {
                ApplicationBase = applicationBase,
                ApplicationName = "CSharpScriptCompatibilityTests",
                DisallowBindingRedirects = true,
                DisallowCodeDownload = true,
                DisallowPublisherPolicy = true
            };

            PermissionSet grants = new PermissionSet(PermissionState.None);
            grants.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
            grants.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, applicationBase));
            grants.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, applicationBase));
            grants.AddPermission(new ReflectionPermission(ReflectionPermissionFlag.RestrictedMemberAccess));
            grants.AddPermission(new UIPermission(UIPermissionClipboard.NoClipboard));
            grants.AddPermission(new UIPermission(UIPermissionWindow.AllWindows));

            AppDomain sandbox = AppDomain.CreateDomain("CSharpScriptCompatibilityDomain", null, setup, grants);
            try
            {
                object[] args = { null };
                ScriptRunner runner = (ScriptRunner)sandbox.CreateInstanceFromAndUnwrap(
                    typeof(ScriptRunner).Assembly.Location,
                    typeof(ScriptRunner).FullName,
                    false,
                    BindingFlags.Default,
                    null,
                    args,
                    null,
                    null);
                return runner.Execute(assembly);
            }
            finally
            {
                AppDomain.Unload(sandbox);
            }
        }

        /// <summary>
        /// Formats CodeDOM diagnostics for an actionable test failure.
        /// </summary>
        private static string FormatErrors(CompilerErrorCollection errors)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            foreach (CompilerError error in errors)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.AppendFormat("{0} {1}: {2}", error.IsWarning ? "warning" : "error", error.ErrorNumber, error.ErrorText);
            }
            return builder.ToString();
        }
    }
}
