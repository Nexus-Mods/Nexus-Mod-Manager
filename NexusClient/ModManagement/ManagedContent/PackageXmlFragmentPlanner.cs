using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Nexus.Client.Games;
using Nexus.Client.Games.ManagedContent;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement.ManagedContent
{
    /// <summary>Combines a package's ordered XML records into one native generated file, without live game reads/writes.</summary>
    internal sealed class PackageXmlFragmentPlanner
    {
        private const int MaximumFragmentBytes = 1024 * 1024;
        private const int MaximumTotalBytes = 8 * 1024 * 1024;
        private readonly PackageXmlFragmentRule _rule;

        /// <summary>Binds an immutable configured scope.</summary>
        internal PackageXmlFragmentPlanner(PackageXmlFragmentRule rule)
        {
            _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        }

        /// <summary>Plans exact complete-package inputs before any output or native owner registration is changed.</summary>
        internal PackageGeneratedFilePlan Build(IMod mod, ModInstallContext context,
            IReadOnlyList<KeyValuePair<string, string>> selectedFiles)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            if (context == null || context.InstallRoot != ModInstallRoot.Data || mod.HasInstallScript)
                throw new InvalidDataException("Package XML generation supports only non-scripted Data-root basic installation.");
            List<string> inventory = mod.GetFileList();
            if (inventory == null || inventory.Count > 100000 || selectedFiles == null || selectedFiles.Count > 100000)
                throw new InvalidDataException("The package inventory/selection is unavailable or exceeds its bound.");
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in inventory)
            {
                string[] parts;
                if (!ManagedContentPath.TryParse(path, out parts)) throw new InvalidDataException("Unsafe package archive path.");
                string canonical = String.Join("\\", parts);
                if (sources.ContainsKey(canonical)) throw new InvalidDataException("Ambiguous case/separator aliases in package archive paths.");
                sources.Add(canonical, path);
            }
            var selected = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> file in selectedFiles)
            {
                string[] parts;
                if (!ManagedContentPath.TryParse(file.Key, out parts)) throw new InvalidDataException("Unsafe package selection path.");
                string canonical = String.Join("\\", parts);
                if (!sources.ContainsKey(canonical) || selected.ContainsKey(canonical))
                    throw new InvalidDataException("Package selection is not a unique subset of the current archive.");
                selected.Add(canonical, file);
            }
            string prefix = _rule.SourceFolder + "\\";
            if (sources.ContainsKey(_rule.SourceFolder)) throw new InvalidDataException("The fragment sourceFolder is an archive file.");
            List<string> fragments = sources.Keys.Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ThenBy(path => path, StringComparer.Ordinal).ToList();
            if (fragments.Count > 32) throw new InvalidDataException("A package contains more than 32 XML fragments.");
            if (fragments.Count == 0) return new PackageGeneratedFilePlan(new string[0], new BasicInstallGeneratedFile[0]);
            if (fragments.Any(path => !selected.ContainsKey(path) || !String.IsNullOrEmpty(selected[path].Value) ||
                !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("All fragment inputs must be selected exactly once without destination overrides and must be XML files.");

            var output = new XElement(_rule.DocumentRoot);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            int totalBytes = 0;
            foreach (string path in fragments)
            {
                byte[] bytes = ReadFragment(mod, sources[path]);
                totalBytes = checked(totalBytes + bytes.Length);
                if (totalBytes > MaximumTotalBytes) throw new InvalidDataException("Package XML inputs exceed 8 MiB.");
                XElement root;
                try
                {
                    using (var input = new MemoryStream(bytes, false))
                    {
                        using (XmlReader reader = CreateReader(input))
                        {
                            int nodes = 0;
                            while (reader.Read())
                                if (reader.Depth > 64 || ++nodes > 65536)
                                    throw new InvalidDataException("A fragment exceeds the XML depth/node bound.");
                        }
                        input.Position = 0;
                        using (XmlReader reader = CreateReader(input))
                            root = XDocument.Load(reader).Root;
                    }
                }
                catch (XmlException error) { throw new InvalidDataException("A package fragment is not supported XML: " + path, error); }
                if (root == null || root.Name != XName.Get(_rule.DocumentRoot) || root.HasAttributes ||
                    root.Nodes().OfType<XText>().Any(text => !String.IsNullOrWhiteSpace(text.Value)))
                    throw new InvalidDataException("A fragment has an unexpected root, root attributes or root text.");
                List<XElement> records = root.Elements().ToList();
                if (records.Count == 0) throw new InvalidDataException("Empty XML fragments are not supported.");
                foreach (XElement record in records)
                {
                    string identity = (string)record.Attribute(_rule.IdentityAttribute);
                    if (record.Name != XName.Get(_rule.RecordElement) || String.IsNullOrWhiteSpace(identity) || identity.Length > 256 ||
                        identity != identity.Trim() || identity.Any(Char.IsControl) || !identities.Add(identity))
                        throw new InvalidDataException("Fragment records require the configured element and a unique bounded identity; duplicate identities have no implicit precedence.");
                    if (identities.Count > 4096) throw new InvalidDataException("Package XML output exceeds 4096 records.");
                    output.Add(new XElement(record));
                }
            }
            using (var stream = new BoundedOutputStream())
            {
                using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false, true), Indent = true, IndentChars = "  ", NewLineChars = "\n",
                    NewLineHandling = NewLineHandling.Entitize, CloseOutput = false
                }))
                    new XDocument(new XDeclaration("1.0", "utf-8", null), output).Save(writer);
                if (stream.Length > MaximumTotalBytes) throw new InvalidDataException("Generated package XML exceeds 8 MiB.");
                return new PackageGeneratedFilePlan(fragments.Select(path => sources[path]), new[]
                {
                    new BasicInstallGeneratedFile(sources[fragments[0]], _rule.DestinationPath, stream.ToArray())
                });
            }
        }

        /// <summary>Creates a bounded reader with external entities and DTDs disabled.</summary>
        private static XmlReader CreateReader(Stream stream)
        {
            return XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumFragmentBytes
            });
        }

        /// <summary>Stops serialization before output allocation can exceed the generated-file limit.</summary>
        private sealed class BoundedOutputStream : MemoryStream
        {
            /// <summary>Rejects an output buffer before extending the bounded stream.</summary>
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Position + count > MaximumTotalBytes) throw new InvalidDataException("Generated package XML exceeds 8 MiB.");
                base.Write(buffer, offset, count);
            }

            /// <summary>Preserves the same bound for single-byte writes.</summary>
            public override void WriteByte(byte value)
            {
                if (Position >= MaximumTotalBytes) throw new InvalidDataException("Generated package XML exceeds 8 MiB.");
                base.WriteByte(value);
            }
        }

        /// <summary>Bounds the archive stream before decoding instead of materializing arbitrary input bytes.</summary>
        private static byte[] ReadFragment(IMod mod, string path)
        {
            using (Stream stream = mod.GetFileStream(path))
            {
                if (stream == null) throw new InvalidDataException("A package XML fragment stream is unavailable.");
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (output.Length + read > MaximumFragmentBytes) throw new InvalidDataException("A package XML fragment exceeds 1 MiB.");
                        output.Write(buffer, 0, read);
                    }
                    return output.ToArray();
                }
            }
        }
    }
}
