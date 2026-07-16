using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Plugins;

namespace Nexus.Client.PluginManagement
{
    public enum PluginAddressClass
    {
        None = 0,
        Full = 1,
        Light = 2,
        Medium = 3,
        Small = 4
    }

    public enum PluginParseStatus
    {
        Unknown = 0,
        Parsed = 1,
        Unsupported = 2,
        Corrupt = 3
    }

    [Flags]
    public enum PluginHeaderFlags
    {
        None = 0,
        Master = 1,
        Light = 2,
        Medium = 4,
        Small = 8,
        Blueprint = 16
    }

    [Flags]
    public enum PluginSpecialFlags
    {
        None = 0,
        Blueprint = 1,
        Official = 2,
        Critical = 4,
        FixedOrder = 8,
        ForcedActive = 16
    }

    public enum PluginHeaderFlagSource
    {
        RecordFlags1 = 0,
        RecordFlags2 = 1,
        RecordFlags3 = 2
    }

    public sealed class PluginMetadata
    {
        public PluginMetadata(string physicalExtension, PluginHeaderFlags headerFlags, int formVersion, IList<string> masters, bool effectiveMaster, PluginAddressClass addressClass, PluginSpecialFlags specialFlags, PluginParseStatus parseStatus)
        {
            PhysicalExtension = NormalizeExtension(physicalExtension);
            HeaderFlags = headerFlags;
            FormVersion = formVersion;
            Masters = masters == null ? new List<string>() : new List<string>(masters);
            EffectiveMaster = effectiveMaster;
            AddressClass = addressClass;
            SpecialFlags = specialFlags;
            ParseStatus = parseStatus;
        }

        public string PhysicalExtension { get; private set; }
        public PluginHeaderFlags HeaderFlags { get; private set; }
        public int FormVersion { get; private set; }
        public List<string> Masters { get; private set; }
        public bool EffectiveMaster { get; private set; }
        public PluginAddressClass AddressClass { get; private set; }
        public PluginSpecialFlags SpecialFlags { get; private set; }
        public PluginParseStatus ParseStatus { get; private set; }

        public static PluginMetadata Unknown(string pluginPath)
        {
            return new PluginMetadata(Path.GetExtension(pluginPath), PluginHeaderFlags.None, 0, null, false, PluginAddressClass.Full, PluginSpecialFlags.None, PluginParseStatus.Unknown);
        }

        private static string NormalizeExtension(string extension)
        {
            if (String.IsNullOrWhiteSpace(extension))
                return String.Empty;

            extension = extension.Trim();
            if (!extension.StartsWith(".", StringComparison.Ordinal))
                extension = "." + extension;

            return extension.ToLowerInvariant();
        }
    }

    public sealed class PluginAddressSpacePolicy
    {
        public PluginAddressSpacePolicy(PluginAddressClass addressClass, int firstIndex, int maxCount, string displayFormat)
        {
            AddressClass = addressClass;
            FirstIndex = firstIndex;
            MaxCount = maxCount;
            DisplayFormat = String.IsNullOrWhiteSpace(displayFormat) ? "{0:X2}" : displayFormat;
        }

        public PluginAddressClass AddressClass { get; private set; }
        public int FirstIndex { get; private set; }
        public int MaxCount { get; private set; }
        public string DisplayFormat { get; private set; }

        public string Format(int allocatedIndex)
        {
            return String.Format(DisplayFormat, allocatedIndex);
        }
    }

    public sealed class PluginExtensionPolicy
    {
        public PluginExtensionPolicy(string extension, PluginHeaderFlags forcedFlags, PluginAddressClass forcedAddressClass)
        {
            Extension = NormalizeExtension(extension);
            ForcedFlags = forcedFlags;
            ForcedAddressClass = forcedAddressClass;
        }

        public string Extension { get; private set; }
        public PluginHeaderFlags ForcedFlags { get; private set; }
        public PluginAddressClass ForcedAddressClass { get; private set; }

        private static string NormalizeExtension(string extension)
        {
            if (String.IsNullOrWhiteSpace(extension))
                return String.Empty;
            extension = extension.Trim();
            if (!extension.StartsWith(".", StringComparison.Ordinal))
                extension = "." + extension;
            return extension.ToLowerInvariant();
        }
    }

    public sealed class PluginHeaderFlagMapping
    {
        public PluginHeaderFlagMapping(PluginHeaderFlagSource source, uint mask, PluginHeaderFlags flags)
        {
            Source = source;
            Mask = mask;
            Flags = flags;
        }

        public PluginHeaderFlagSource Source { get; private set; }
        public uint Mask { get; private set; }
        public PluginHeaderFlags Flags { get; private set; }
    }

    public sealed class PluginManagementPolicy
    {
        private readonly Dictionary<string, PluginExtensionPolicy> m_dicExtensions = new Dictionary<string, PluginExtensionPolicy>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<PluginAddressClass, PluginAddressSpacePolicy> m_dicAddressSpaces = new Dictionary<PluginAddressClass, PluginAddressSpacePolicy>();
        private readonly List<PluginHeaderFlagMapping> m_lstHeaderFlagMappings = new List<PluginHeaderFlagMapping>();
        private readonly HashSet<string> m_setOfficialPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_setCriticalPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_setFixedOrderPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> m_lstFixedOrderPlugins = new List<string>();
        private readonly HashSet<string> m_setForcedActivePlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_setBlueprintPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> m_lstBlueprintPluginPrefixes = new List<string>();

        public PluginManagementPolicy()
        {
            MasterPluginsMustLoadBeforeNonMasters = true;
            ValidateDependencies = true;
            ParserStrategy = "gamebryo";
            PersistenceStrategy = "legacy";
            AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Full, 0, 254, "{0:X2}"));
            AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Light, 0, 4096, "FE:{0:X3}"));
            AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Medium, 0, 256, "FD:{0:X2}"));
            AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Small, 0, 4096, "FE:{0:X3}"));
            AddHeaderFlagMapping(new PluginHeaderFlagMapping(PluginHeaderFlagSource.RecordFlags1, 0x00000001, PluginHeaderFlags.Master));
            AddHeaderFlagMapping(new PluginHeaderFlagMapping(PluginHeaderFlagSource.RecordFlags1, 0x00000200, PluginHeaderFlags.Light));
        }

        public string ParserStrategy { get; set; }
        public string PersistenceStrategy { get; set; }
        public bool MasterPluginsMustLoadBeforeNonMasters { get; set; }
        public bool ValidateDependencies { get; set; }
        public string PluginsFilePath { get; set; }
        public string LoadOrderFilePath { get; set; }
        public string EncodingName { get; set; }
        public string ActiveMarker { get; set; }
        public string InactiveMarker { get; set; }
        public string AppDataGameFolderName { get; set; }
        public bool? UseTimestampOrder { get; set; }
        public bool? IgnoreOfficialPlugins { get; set; }
        public bool? ForcedReadOnly { get; set; }
        public bool? SingleFileManagement { get; set; }
        public bool? OfficialPluginsAreImplicitlyActive { get; set; }
        public bool? LoadOrderInPluginDirectory { get; set; }
        public bool? ShowStarfieldCustomPluginsHeader { get; set; }

        public IEnumerable<PluginExtensionPolicy> ExtensionPolicies { get { return m_dicExtensions.Values; } }
        public IEnumerable<PluginAddressSpacePolicy> AddressSpaces { get { return m_dicAddressSpaces.Values; } }
        public IEnumerable<PluginHeaderFlagMapping> HeaderFlagMappings { get { return m_lstHeaderFlagMappings; } }
        public IEnumerable<string> OfficialPlugins { get { return m_setOfficialPlugins; } }
        public IEnumerable<string> CriticalPlugins { get { return m_setCriticalPlugins; } }
        public IEnumerable<string> FixedOrderPlugins { get { return m_lstFixedOrderPlugins; } }
        public IEnumerable<string> ForcedActivePlugins { get { return m_setForcedActivePlugins; } }
        public IEnumerable<string> BlueprintPlugins { get { return m_setBlueprintPlugins; } }
        public IEnumerable<string> BlueprintPluginPrefixes { get { return m_lstBlueprintPluginPrefixes; } }

        public static PluginManagementPolicy CreateDefault(IEnumerable<string> pluginExtensions, IEnumerable<string> criticalPlugins, IEnumerable<string> officialPlugins, IEnumerable<string> officialUnmanagedPlugins, int fullSlotLimit)
        {
            PluginManagementPolicy policy = new PluginManagementPolicy();
            if (fullSlotLimit > 0)
                policy.AddAddressSpace(new PluginAddressSpacePolicy(PluginAddressClass.Full, 0, fullSlotLimit, "{0:X2}"));

            foreach (string extension in pluginExtensions ?? new string[0])
                policy.AddExtension(new PluginExtensionPolicy(extension, GetDefaultForcedFlags(extension), GetDefaultAddressClass(extension)));

            foreach (string plugin in criticalPlugins ?? new string[0])
            {
                policy.AddCriticalPlugin(plugin);
                policy.AddFixedOrderPlugin(plugin);
                policy.AddForcedActivePlugin(plugin);
            }

            foreach (string plugin in officialPlugins ?? new string[0])
            {
                policy.AddOfficialPlugin(plugin);
                policy.AddFixedOrderPlugin(plugin);
            }

            foreach (string plugin in officialUnmanagedPlugins ?? new string[0])
                policy.AddOfficialPlugin(plugin);

            return policy;
        }

        public void AddExtension(PluginExtensionPolicy extensionPolicy)
        {
            if (extensionPolicy == null || String.IsNullOrEmpty(extensionPolicy.Extension))
                return;
            m_dicExtensions[extensionPolicy.Extension] = extensionPolicy;
        }

        public PluginExtensionPolicy GetExtensionPolicy(string extension)
        {
            if (String.IsNullOrWhiteSpace(extension))
                return null;
            if (!extension.StartsWith(".", StringComparison.Ordinal))
                extension = "." + extension;
            PluginExtensionPolicy policy;
            return m_dicExtensions.TryGetValue(extension.ToLowerInvariant(), out policy) ? policy : null;
        }

        public void AddAddressSpace(PluginAddressSpacePolicy addressSpace)
        {
            if (addressSpace != null)
                m_dicAddressSpaces[addressSpace.AddressClass] = addressSpace;
        }

        public void AddHeaderFlagMapping(PluginHeaderFlagMapping mapping)
        {
            if (mapping != null && mapping.Mask != 0)
                m_lstHeaderFlagMappings.Add(mapping);
        }

        public PluginHeaderFlags MapHeaderFlags(uint recordFlags1, uint recordFlags2, uint recordFlags3)
        {
            PluginHeaderFlags flags = PluginHeaderFlags.None;
            foreach (PluginHeaderFlagMapping mapping in m_lstHeaderFlagMappings)
            {
                uint value = recordFlags1;
                if (mapping.Source == PluginHeaderFlagSource.RecordFlags2)
                    value = recordFlags2;
                else if (mapping.Source == PluginHeaderFlagSource.RecordFlags3)
                    value = recordFlags3;

                if ((value & mapping.Mask) == mapping.Mask)
                    flags |= mapping.Flags;
            }
            return flags;
        }

        public PluginAddressSpacePolicy GetAddressSpace(PluginAddressClass addressClass)
        {
            PluginAddressSpacePolicy addressSpace;
            return m_dicAddressSpaces.TryGetValue(addressClass, out addressSpace) ? addressSpace : null;
        }

        public void AddOfficialPlugin(string pluginName)
        {
            AddPluginName(m_setOfficialPlugins, pluginName);
        }

        public void AddCriticalPlugin(string pluginName)
        {
            AddPluginName(m_setCriticalPlugins, pluginName);
        }

        public void AddFixedOrderPlugin(string pluginName)
        {
            pluginName = NormalizePluginName(pluginName);
            if (String.IsNullOrEmpty(pluginName))
                return;
            if (m_setFixedOrderPlugins.Add(pluginName))
                m_lstFixedOrderPlugins.Add(pluginName);
        }

        public void AddForcedActivePlugin(string pluginName)
        {
            AddPluginName(m_setForcedActivePlugins, pluginName);
        }

        public void AddBlueprintPlugin(string pluginName)
        {
            AddPluginName(m_setBlueprintPlugins, pluginName);
        }

        public void AddBlueprintPluginPrefix(string pluginNamePrefix)
        {
            pluginNamePrefix = NormalizePluginNamePrefix(pluginNamePrefix);
            if (!String.IsNullOrEmpty(pluginNamePrefix) && !ContainsPluginNamePrefix(pluginNamePrefix))
                m_lstBlueprintPluginPrefixes.Add(pluginNamePrefix);
        }

        private bool ContainsPluginNamePrefix(string pluginNamePrefix)
        {
            foreach (string prefix in m_lstBlueprintPluginPrefixes)
                if (String.Equals(prefix, pluginNamePrefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public bool IsOfficialPlugin(string pluginName)
        {
            return ContainsPluginName(m_setOfficialPlugins, pluginName);
        }

        public bool IsCriticalPlugin(string pluginName)
        {
            return ContainsPluginName(m_setCriticalPlugins, pluginName);
        }

        public bool IsFixedOrderPlugin(string pluginName)
        {
            return ContainsPluginName(m_setFixedOrderPlugins, pluginName);
        }

        public bool IsForcedActivePlugin(string pluginName)
        {
            return ContainsPluginName(m_setForcedActivePlugins, pluginName);
        }

        public bool IsBlueprintPlugin(string pluginName)
        {
            pluginName = NormalizePluginName(pluginName);
            if (String.IsNullOrEmpty(pluginName))
                return false;
            if (m_setBlueprintPlugins.Contains(pluginName))
                return true;
            foreach (string prefix in m_lstBlueprintPluginPrefixes)
                if (pluginName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public PluginMetadata Classify(string pluginPath, PluginHeaderFlags parsedFlags, int formVersion, IList<string> masters, PluginParseStatus parseStatus)
        {
            string extension = Path.GetExtension(pluginPath);
            PluginExtensionPolicy extensionPolicy = GetExtensionPolicy(extension);
            PluginHeaderFlags effectiveFlags = parsedFlags;
            if (extensionPolicy != null)
                effectiveFlags |= extensionPolicy.ForcedFlags;

            PluginAddressClass addressClass = extensionPolicy != null && extensionPolicy.ForcedAddressClass != PluginAddressClass.None
                ? extensionPolicy.ForcedAddressClass
                : GetAddressClassFromFlags(effectiveFlags);

            bool effectiveMaster = (effectiveFlags & PluginHeaderFlags.Master) == PluginHeaderFlags.Master || addressClass == PluginAddressClass.Medium || addressClass == PluginAddressClass.Small;
            PluginSpecialFlags specialFlags = PluginSpecialFlags.None;
            if ((effectiveFlags & PluginHeaderFlags.Blueprint) == PluginHeaderFlags.Blueprint || IsBlueprintPlugin(Path.GetFileName(pluginPath)))
                specialFlags |= PluginSpecialFlags.Blueprint;
            if (IsOfficialPlugin(Path.GetFileName(pluginPath)))
                specialFlags |= PluginSpecialFlags.Official;
            if (IsCriticalPlugin(Path.GetFileName(pluginPath)))
                specialFlags |= PluginSpecialFlags.Critical;
            if (IsFixedOrderPlugin(Path.GetFileName(pluginPath)))
                specialFlags |= PluginSpecialFlags.FixedOrder;
            if (IsForcedActivePlugin(Path.GetFileName(pluginPath)))
                specialFlags |= PluginSpecialFlags.ForcedActive;

            return new PluginMetadata(extension, effectiveFlags, formVersion, masters, effectiveMaster, addressClass, specialFlags, parseStatus);
        }

        private static PluginHeaderFlags GetDefaultForcedFlags(string extension)
        {
            if (String.IsNullOrWhiteSpace(extension))
                return PluginHeaderFlags.None;
            extension = extension.TrimStart('.').ToLowerInvariant();
            if (extension == "esm")
                return PluginHeaderFlags.Master;
            if (extension == "esl")
                return PluginHeaderFlags.Master | PluginHeaderFlags.Light;
            return PluginHeaderFlags.None;
        }

        private static PluginAddressClass GetDefaultAddressClass(string extension)
        {
            if (String.IsNullOrWhiteSpace(extension))
                return PluginAddressClass.Full;
            extension = extension.TrimStart('.').ToLowerInvariant();
            if (extension == "esl")
                return PluginAddressClass.Light;
            return PluginAddressClass.Full;
        }

        private static PluginAddressClass GetAddressClassFromFlags(PluginHeaderFlags flags)
        {
            if ((flags & PluginHeaderFlags.Medium) == PluginHeaderFlags.Medium)
                return PluginAddressClass.Medium;
            if ((flags & PluginHeaderFlags.Small) == PluginHeaderFlags.Small)
                return PluginAddressClass.Small;
            if ((flags & PluginHeaderFlags.Light) == PluginHeaderFlags.Light)
                return PluginAddressClass.Light;
            return PluginAddressClass.Full;
        }

        private static void AddPluginName(HashSet<string> set, string pluginName)
        {
            pluginName = NormalizePluginName(pluginName);
            if (!String.IsNullOrEmpty(pluginName))
                set.Add(pluginName);
        }

        private static bool ContainsPluginName(HashSet<string> set, string pluginName)
        {
            pluginName = NormalizePluginName(pluginName);
            return !String.IsNullOrEmpty(pluginName) && set.Contains(pluginName);
        }

        private static string NormalizePluginName(string pluginName)
        {
            return String.IsNullOrWhiteSpace(pluginName) ? String.Empty : Path.GetFileName(pluginName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }

        private static string NormalizePluginNamePrefix(string pluginNamePrefix)
        {
            return String.IsNullOrWhiteSpace(pluginNamePrefix) ? String.Empty : Path.GetFileName(pluginNamePrefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }
    }
}
