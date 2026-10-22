<# .SYNOPSIS Verifies shared Gamebryo type identity under the production .NET Framework discovery order. #>
param([string]$ReleasePath = (Join-Path $PSScriptRoot '..\..\..\Stage\Release'))

$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleasePath).Path
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('NmmGamebryoAssembly-' + [Guid]::NewGuid().ToString('N'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

$source = @'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Nexus.Client;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Settings;

/// <summary>Exercises default host binding followed by production plugin discovery in a fresh CLR process.</summary>
public static class GamebryoAssemblyIdentityProbe
{
    /// <summary>Discovers game modes before accessing the data-driven Gamebryo type, as NMM does at startup.</summary>
    public static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            Type settingsType = typeof(EnvironmentInfo).Assembly.GetType("Nexus.Client.Properties.Settings", true);
            ISettings settings = (ISettings)Activator.CreateInstance(settingsType, true);
            settingsType.GetProperty("InstallationPaths").SetValue(settings, new PerGameModeSettings<string>(), null);
            settingsType.GetProperty("ExecutablePaths").SetValue(settings, new PerGameModeSettings<string>(), null);
            settingsType.GetProperty("TempPathFolder").SetValue(settings, AppDomain.CurrentDomain.BaseDirectory, null);
            GameModeRegistry.DiscoverSupportedGameModes(new EnvironmentInfo(settings));
            AssertInstallerCompatibility();
            Console.WriteLine("PASS: C# BSA access and XML NVSE conditions share the host Gamebryo type.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    /// <summary>Resolves game-specific script dependencies from the same folders used by NMM.</summary>
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.FullName == args.Name) return assembly;
        foreach (string folder in new[] { "", "GameModes", "ScriptTypes" })
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder, new AssemblyName(args.Name).Name + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }

    /// <summary>Checks actual installer bindings without initializing a game or changing mods, profiles or user settings.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertInstallerCompatibility()
    {
        Type gameType = typeof(DataDrivenGamebryoGameMode);
        object mode = FormatterServices.GetUninitializedObject(gameType);
        gameType.GetField("_definition", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(mode,
            new GameModeDefinition { Gamebryo = new GameModeGamebryoDefinition { ScriptExtenderExecutables = new string[0] } });

        string root = AppDomain.CurrentDomain.BaseDirectory;
        Assembly falloutScript = Assembly.LoadFrom(Path.Combine(root, "GameModes", "Fallout3.CSharpScript.dll"));
        Type proxyType = falloutScript.GetType("Nexus.Client.Games.Fallout3.Scripting.CSharpScript.FalloutCSharpScriptFunctionProxy", true);
        Type bsaType = proxyType.GetProperty("BsaManager").PropertyType;
        using (IDisposable bsa = (IDisposable)bsaType.GetConstructors()[0].Invoke(new[] { mode })) { }

        Assembly xmlScript = Assembly.LoadFrom(Path.Combine(root, "ScriptTypes", "XmlScript.dll"));
        Type stateType = xmlScript.GetType("Nexus.Client.ModManagement.Scripting.XmlScript.ConditionStateManager", true);
        object state = Activator.CreateInstance(stateType, new object[] { null, mode, null, null });
        Assembly nvScript = Assembly.LoadFrom(Path.Combine(root, "GameModes", "FalloutNV.XmlScript.dll"));
        Type conditionType = nvScript.GetType("Nexus.Client.Games.FalloutNV.Scripting.XmlScript.NvseCondition", true);
        object condition = Activator.CreateInstance(conditionType, new object[] { new Version(1, 0) });
        if ((bool)conditionType.GetMethod("GetIsFulfilled").Invoke(condition, new[] { state }))
            throw new InvalidOperationException("An absent NVSE must not fulfill the version requirement.");
        if (!((string)conditionType.GetMethod("GetMessage").Invoke(condition, new[] { state })).Contains("requires NVSE"))
            throw new InvalidOperationException("The missing-NVSE message changed.");

        int count = 0;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "GamebryoBase") count++;
        if (count != 1) throw new InvalidOperationException("Loaded " + count + " Gamebryo runtimes instead of one.");
    }
}
'@

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    Copy-Item -Path (Join-Path $release '*.dll') -Destination $temporaryRoot
    Copy-Item -LiteralPath (Join-Path $release 'NexusClient.exe') -Destination $temporaryRoot
    foreach ($folder in @('GameModes', 'ScriptTypes')) {
        $destination = Join-Path $temporaryRoot $folder
        New-Item -ItemType Directory -Path $destination | Out-Null
        Copy-Item -Path (Join-Path $release ($folder + '\*.dll')) -Destination $destination
    }
    $sourcePath = Join-Path $temporaryRoot 'GamebryoAssemblyIdentityProbe.cs'
    $probePath = Join-Path $temporaryRoot 'GamebryoAssemblyIdentityProbe.exe'
    [IO.File]::WriteAllText($sourcePath, $source)
    Copy-Item -LiteralPath (Join-Path $release 'NexusClient.exe.config') -Destination ($probePath + '.config')
    $references = @('NexusClient.exe', 'GamebryoBase.dll', 'NexusClient.Interface.dll', 'ModManager.Interface.dll', 'Util.dll') |
        ForEach-Object { '/reference:' + (Join-Path $temporaryRoot $_) }
    & $compiler /nologo /target:exe ('/out:' + $probePath) /reference:System.Configuration.dll @references $sourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the .NET Framework assembly-identity probe.' }

    $mirrorPath = Join-Path $temporaryRoot 'GameModes\GamebryoBase.dll'
    if (Test-Path -LiteralPath $mirrorPath) { throw 'The build output still contains a mirrored Gamebryo runtime.' }
    & $probePath
    if ($LASTEXITCODE -ne 0) { throw 'Canonical build layout failed the installer compatibility check.' }

    Copy-Item -LiteralPath (Join-Path $temporaryRoot 'GamebryoBase.dll') -Destination $mirrorPath
    & $probePath
    if ($LASTEXITCODE -ne 0) { throw 'An archive upgrade retaining the legacy mirror failed the installer compatibility check.' }
    Write-Output 'PASS: canonical build layout and archive upgrade with a retained mirror.'
}
finally {
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $temporaryRoot.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test directory.' }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
