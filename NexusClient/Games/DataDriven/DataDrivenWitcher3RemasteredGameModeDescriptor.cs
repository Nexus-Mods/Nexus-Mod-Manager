using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using Nexus.Client.Games.Witcher3;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>
    /// Remastered discovery has an independent version gate. The DX12 binary also exists in
    /// Next-Gen 4.x and is therefore not an adequate game identity by itself.
    /// </summary>
    public sealed class DataDrivenWitcher3RemasteredGameModeDescriptor : DataDrivenGameModeDescriptor,
        IGameInstallationCandidateValidator
    {
        public DataDrivenWitcher3RemasteredGameModeDescriptor(IEnvironmentInfo environmentInfo,
            GameModeDefinition definition) : base(environmentInfo, definition)
        {
            if (definition == null ||
                !string.Equals(definition.ModeId, "Witcher3Remastered", StringComparison.Ordinal) ||
                !string.Equals(definition.BehaviorProfile, "witcher3remastered", StringComparison.Ordinal))
                throw new ArgumentException("The Remastered descriptor requires its dedicated data-driven definition.", nameof(definition));
        }

        public static bool IsConfirmedRemastered(Witcher3EditionResolution resolution)
        {
            return resolution != null && resolution.Edition == Witcher3Edition.Remastered;
        }

        public bool IsValidInstallationCandidate(string absoluteGameRoot)
        {
            if (string.IsNullOrWhiteSpace(absoluteGameRoot))
                return false;
            try
            {
                return IsConfirmedRemastered(Witcher3InstallationProbe.Inspect(absoluteGameRoot).Edition);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is SecurityException || ex is ArgumentException || ex is NotSupportedException ||
                ex is Win32Exception)
            {
                return false;
            }
        }
    }
}
