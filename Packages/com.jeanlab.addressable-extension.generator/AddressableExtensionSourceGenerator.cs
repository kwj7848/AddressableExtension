using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AddressableExtensionGenerator
{
    [Generator]
    public class AddressableExtensionSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context) { }

        public void Execute(GeneratorExecutionContext context)
        {
            var assemblyName = context.Compilation.AssemblyName ?? "";
            if (assemblyName != "Assembly-CSharp")
                return;

            var root = FindProjectRoot(context);
            if (root == null)
            {
                GenerateEmpty(context, "// Could not find project root");
                return;
            }

            var dataDir = Path.Combine(root, "Assets", "AddressableAssetsData");
            if (!Directory.Exists(dataDir))
            {
                GenerateEmpty(context, "// AddressableAssetsData folder not found");
                return;
            }

            bool enableNames = true;
            bool enableLabels = true;
            var extSettingsPath = Path.Combine(root, "ProjectSettings", "AddressableExtensionSettings.json");
            if (File.Exists(extSettingsPath))
                ParseExtensionSettings(File.ReadAllText(extSettingsPath), out enableNames, out enableLabels);

            var labels = new HashSet<string>();
            var addresses = new HashSet<string>();
            var folderChildAddresses = new HashSet<string>();

            if (enableLabels)
            {
                var settingsPath = Path.Combine(dataDir, "AddressableAssetSettings.asset");
                if (File.Exists(settingsPath))
                    ParseLabelsFromSettings(File.ReadAllText(settingsPath), labels);
            }

            if (enableNames)
            {
                var groupsPath = Path.Combine(dataDir, "AssetGroups");
                if (Directory.Exists(groupsPath))
                {
                    var groupEntries = new List<GroupEntry>();
                    foreach (var file in Directory.GetFiles(groupsPath, "*.asset"))
                        ParseGroupEntries(File.ReadAllText(file), groupEntries);

                    var assetsDir = Path.Combine(root, "Assets");
                    var guids = new HashSet<string>(groupEntries.Select(e => e.Guid));
                    var guidPaths = ResolveGuidPaths(assetsDir, guids);

                    foreach (var entry in groupEntries)
                    {
                        string assetPath;
                        if (guidPaths.TryGetValue(entry.Guid, out assetPath) &&
                            Directory.Exists(assetPath))
                        {
                            bool hasChildren = false;
                            foreach (var file in Directory.GetFiles(assetPath, "*", SearchOption.AllDirectories))
                            {
                                var fileName = Path.GetFileName(file);
                                if (fileName.EndsWith(".meta") || fileName.StartsWith(".")) continue;
                                hasChildren = true;
                                var relativePath = file
                                    .Substring(assetPath.Length)
                                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    .Replace(Path.DirectorySeparatorChar, '/');
                                folderChildAddresses.Add(entry.Address + "/" + relativePath);
                            }
                            if (!hasChildren)
                                addresses.Add(entry.Address);
                        }
                        else
                        {
                            addresses.Add(entry.Address);
                        }
                    }
                }
            }

            var source = GenerateSource(labels, addresses, folderChildAddresses, enableNames, enableLabels);
            context.AddSource("AddressableExtension.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }

        private string FindProjectRoot(GeneratorExecutionContext context)
        {
            foreach (var tree in context.Compilation.SyntaxTrees)
            {
                var filePath = tree.FilePath;
                if (string.IsNullOrEmpty(filePath)) continue;

                var dir = Path.GetDirectoryName(filePath);
                while (!string.IsNullOrEmpty(dir))
                {
                    if (Directory.Exists(Path.Combine(dir, "Assets")) &&
                        Directory.Exists(Path.Combine(dir, "ProjectSettings")))
                        return dir;

                    var parent = Path.GetDirectoryName(dir);
                    if (parent == dir) break;
                    dir = parent;
                }
            }
            return null;
        }

        private void GenerateEmpty(GeneratorExecutionContext context, string comment)
        {
            var source = "// <auto-generated/>\n" + comment +
                "\n\nnamespace AddressableExtension\n{\n" +
                "    public static class AddressableLabels { }\n" +
                "    public static class AddressableNames { }\n}\n";
            context.AddSource("AddressableExtension.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }

        private void ParseLabelsFromSettings(string content, HashSet<string> labels)
        {
            bool inLabels = false;
            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("m_LabelNames:"))
                {
                    inLabels = true;
                }
                else if (inLabels)
                {
                    if (line.StartsWith("- "))
                    {
                        var label = line.Substring(2).Trim();
                        label = StripYamlQuotes(label);
                        if (!string.IsNullOrEmpty(label))
                            labels.Add(label);
                    }
                    else if (!string.IsNullOrEmpty(line) && !line.StartsWith("-"))
                    {
                        inLabels = false;
                    }
                }
            }
        }

        private struct GroupEntry
        {
            public string Guid;
            public string Address;
        }

        private void ParseGroupEntries(string content, List<GroupEntry> entries)
        {
            string currentGuid = null;
            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("- m_GUID:"))
                {
                    currentGuid = line.Substring("- m_GUID:".Length).Trim();
                }
                else if (line.StartsWith("m_Address:") && currentGuid != null)
                {
                    var address = line.Substring("m_Address:".Length).Trim();
                    address = StripYamlQuotes(address);
                    if (!string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(currentGuid))
                        entries.Add(new GroupEntry { Guid = currentGuid, Address = address });
                    currentGuid = null;
                }
            }
        }

        private Dictionary<string, string> ResolveGuidPaths(string assetsDir, HashSet<string> guids)
        {
            var result = new Dictionary<string, string>();
            if (guids.Count == 0) return result;

            foreach (var metaFile in Directory.GetFiles(assetsDir, "*.meta", SearchOption.AllDirectories))
            {
                foreach (var rawLine in File.ReadAllText(metaFile).Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("guid:"))
                    {
                        var guid = line.Substring(5).Trim();
                        if (guids.Contains(guid))
                        {
                            result[guid] = metaFile.Substring(0, metaFile.Length - 5);
                            if (result.Count == guids.Count) return result;
                        }
                        break;
                    }
                }
            }
            return result;
        }

        private void ParseExtensionSettings(string json, out bool enableNames, out bool enableLabels)
        {
            enableNames = true;
            enableLabels = true;
            foreach (var rawLine in json.Split('\n'))
            {
                var line = rawLine.Trim().Trim(',');
                if (line.Contains("\"enableNames\""))
                    enableNames = line.Contains("true");
                else if (line.Contains("\"enableLabels\""))
                    enableLabels = line.Contains("true");
            }
        }

        private string GenerateSource(HashSet<string> labels, HashSet<string> addresses, HashSet<string> folderChildAddresses, bool enableNames = true, bool enableLabels = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine();
            sb.AppendLine("namespace AddressableExtension");
            sb.AppendLine("{");

            sb.AppendLine("    public static class AddressableLabels");
            sb.AppendLine("    {");
            if (enableLabels)
            {
                var labelFields = ResolveFieldNames(labels);
                foreach (var kv in labelFields.OrderBy(p => p.Value))
                    sb.AppendLine($"        public const string {kv.Value} = \"{Escape(kv.Key)}\";");
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    public static class AddressableNames");
            sb.AppendLine("    {");
            if (enableNames)
            {
                var entries = addresses
                    .Select(a => new AddressEntry { RemainingPath = a, FullAddress = a, KeepExtension = false })
                    .Concat(folderChildAddresses
                        .Select(a => new AddressEntry { RemainingPath = a, FullAddress = a, KeepExtension = true }))
                    .ToList();
                GenerateAddressNode(sb, entries, "        ");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private struct AddressEntry
        {
            public string RemainingPath;
            public string FullAddress;
            public bool KeepExtension;
        }

        private void GenerateAddressNode(StringBuilder sb, List<AddressEntry> entries, string indent)
        {
            var leaves = new List<AddressEntry>();
            var groups = new SortedDictionary<string, List<AddressEntry>>();

            foreach (var entry in entries)
            {
                int sep = entry.RemainingPath.IndexOf('/');
                if (sep < 0)
                {
                    leaves.Add(entry);
                }
                else
                {
                    var folder = entry.RemainingPath.Substring(0, sep);
                    var rest = entry.RemainingPath.Substring(sep + 1);
                    if (!groups.ContainsKey(folder))
                        groups[folder] = new List<AddressEntry>();
                    groups[folder].Add(new AddressEntry { RemainingPath = rest, FullAddress = entry.FullAddress, KeepExtension = entry.KeepExtension });
                }
            }

            var groupFieldNames = ResolveFieldNames(groups.Keys);
            var usedNames = new HashSet<string>(groupFieldNames.Values);

            var leafFields = new List<KeyValuePair<string, string>>();
            var sortedLeaves = leaves.OrderBy(l => l.RemainingPath).ToList();

            foreach (var leaf in sortedLeaves)
            {
                var display = leaf.KeepExtension ? leaf.RemainingPath : StripExtension(leaf.RemainingPath);
                var name = SanitizeIdentifier(display);
                if (display == name && !usedNames.Contains(name))
                {
                    leafFields.Add(new KeyValuePair<string, string>(name, leaf.FullAddress));
                    usedNames.Add(name);
                }
            }

            foreach (var leaf in sortedLeaves)
            {
                if (leafFields.Any(f => f.Value == leaf.FullAddress)) continue;
                var display = leaf.KeepExtension ? leaf.RemainingPath : StripExtension(leaf.RemainingPath);
                var name = SanitizeIdentifier(display);
                if (!usedNames.Contains(name))
                {
                    leafFields.Add(new KeyValuePair<string, string>(name, leaf.FullAddress));
                    usedNames.Add(name);
                }
                else
                {
                    int i = 1;
                    while (usedNames.Contains(name + "_" + i)) i++;
                    var unique = name + "_" + i;
                    leafFields.Add(new KeyValuePair<string, string>(unique, leaf.FullAddress));
                    usedNames.Add(unique);
                }
            }

            foreach (var kv in leafFields.OrderBy(f => f.Key))
                sb.AppendLine(indent + "public const string " + kv.Key + " = \"" + Escape(kv.Value) + "\";");

            foreach (var kv in groups)
            {
                var className = groupFieldNames[kv.Key];
                sb.AppendLine();
                sb.AppendLine(indent + "public static class " + className);
                sb.AppendLine(indent + "{");
                GenerateAddressNode(sb, kv.Value, indent + "    ");
                sb.AppendLine(indent + "}");
            }
        }

        private static string StripYamlQuotes(string value)
        {
            if (value.Length >= 2)
            {
                if ((value[0] == '\'' && value[value.Length - 1] == '\'') ||
                    (value[0] == '"' && value[value.Length - 1] == '"'))
                    return value.Substring(1, value.Length - 2);
            }
            return value;
        }

        private static string StripExtension(string fileName)
        {
            int dot = fileName.LastIndexOf('.');
            return dot > 0 ? fileName.Substring(0, dot) : fileName;
        }

        private Dictionary<string, string> ResolveFieldNames(IEnumerable<string> names)
        {
            var result = new Dictionary<string, string>();
            var used = new HashSet<string>();
            var sorted = names.OrderBy(n => n).ToList();

            foreach (var name in sorted)
            {
                var id = SanitizeIdentifier(name);
                if (name == id) { result[name] = id; used.Add(id); }
            }
            foreach (var name in sorted)
            {
                if (result.ContainsKey(name)) continue;
                var id = SanitizeIdentifier(name);
                if (!used.Contains(id)) { result[name] = id; used.Add(id); }
                else
                {
                    int i = 1;
                    while (used.Contains(id + "_" + i)) i++;
                    var unique = id + "_" + i;
                    result[name] = unique;
                    used.Add(unique);
                }
            }
            return result;
        }

        private static string SanitizeIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_Empty";
            var id = Regex.Replace(name, "[^a-zA-Z0-9_]", "_");
            if (char.IsDigit(id[0])) id = "_" + id;
            if (_keywords.Contains(id.ToLower())) id = "@" + id;
            return id;
        }

        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                 .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

        private static readonly HashSet<string> _keywords = new HashSet<string>
        {
            "abstract","as","base","bool","break","byte","case","catch","char","checked",
            "class","const","continue","decimal","default","delegate","do","double","else",
            "enum","event","explicit","extern","false","finally","fixed","float","for",
            "foreach","goto","if","implicit","in","int","interface","internal","is","lock",
            "long","namespace","new","null","object","operator","out","override","params",
            "private","protected","public","readonly","ref","return","sbyte","sealed","short",
            "sizeof","stackalloc","static","string","struct","switch","this","throw","true",
            "try","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual",
            "void","volatile","while"
        };
    }
}
