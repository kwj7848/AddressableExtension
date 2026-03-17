using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using ModificationEvent = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent;

namespace AddressableExtension.Editor
{
    [InitializeOnLoad]
    internal static class AddressableAutoSimplify
    {
        internal static readonly MethodInfo RenameLabelMethod;

        static AddressableAutoSimplify()
        {
            RenameLabelMethod = typeof(AddressableAssetSettings).GetMethod(
                "RenameLabel",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string), typeof(bool) },
                null);

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                settings.OnModification += OnSettingsModify;
            }

            EditorApplication.delayCall += () =>
            {
                settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings != null)
                {
                    settings.OnModification -= OnSettingsModify;
                    settings.OnModification += OnSettingsModify;

                    if (AddressableExtensionSettingsManager.Data.enableNames)
                        SanitizeModifiedAddresses(settings);
                }
            };
        }

        private static void OnSettingsModify(AddressableAssetSettings settings, ModificationEvent evt, object data)
        {
            if (evt == ModificationEvent.EntryAdded || evt == ModificationEvent.EntryMoved)
            {
                if (!AddressableExtensionSettingsManager.Data.enableNames)
                    return;

                var entries = data as List<AddressableAssetEntry>;
                if (entries == null || entries.Count == 0)
                    return;

                var existingAddresses = new HashSet<string>();
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    foreach (var entry in group.entries)
                    {
                        if (!entries.Contains(entry))
                            existingAddresses.Add(entry.address);
                    }
                }

                foreach (var entry in entries)
                {
                    if (entry.parentGroup != null && AddressableReferencedOnlyGroupHandler.IsReferencedOnlyGroup(entry.parentGroup))
                    {
                        existingAddresses.Add(entry.address);
                        continue;
                    }
                    SimplifyAddress(entry, existingAddresses);
                    existingAddresses.Add(entry.address);
                }
            }
            else if (evt == ModificationEvent.EntryModified)
            {
                if (!AddressableExtensionSettingsManager.Data.enableNames)
                    return;

                if (!_sanitizePending)
                {
                    _sanitizePending = true;
                    EditorApplication.delayCall += () =>
                    {
                        _sanitizePending = false;
                        SanitizeModifiedAddresses(settings);
                    };
                }
            }
            else if (evt == ModificationEvent.LabelAdded)
            {
                if (!AddressableExtensionSettingsManager.Data.enableLabels)
                    return;

                var labelName = data as string;
                if (!string.IsNullOrEmpty(labelName))
                    EditorApplication.delayCall += () => SanitizeLabel(settings, labelName);
            }
        }

        private static void SanitizeLabel(AddressableAssetSettings settings, string labelName)
        {
            if (settings == null || string.IsNullOrEmpty(labelName))
                return;

            // Skip if already deleted or renamed
            if (!settings.GetLabels().Contains(labelName))
                return;

            string sanitized = SanitizeName(labelName);

            if (sanitized == labelName)
                return;

            // Check for conflicts with existing labels
            var existingLabels = new HashSet<string>(settings.GetLabels());
            existingLabels.Remove(labelName);

            string finalName = sanitized;
            if (existingLabels.Contains(finalName))
            {
                string conflictWith = finalName;
                int suffix = 1;
                while (existingLabels.Contains(finalName))
                {
                    finalName = $"{sanitized}_{suffix}";
                    suffix++;
                }
                Debug.LogWarning($"[Addressable Extension] Label \"{labelName}\" → \"{finalName}\" (conflict with existing \"{conflictWith}\")");
            }
            else
            {
                var invalidChars = labelName.Where(c => !char.IsLetterOrDigit(c) && c != '_').Distinct();
                var charList = string.Join(", ", invalidChars.Select(c => c == ' ' ? "space" : $"'{c}'"));
                Debug.Log($"[Addressable Extension] Label \"{labelName}\" → \"{finalName}\" ({charList} cannot be used in C# identifiers, replaced with '_' for code autocomplete)");
            }

            if (RenameLabelMethod != null)
            {
                RenameLabelMethod.Invoke(settings, new object[] { labelName, finalName, true });
            }
            else
            {
                Debug.LogError("[Addressable Extension] RenameLabel method not found.");
            }
        }

        private static bool _sanitizePending;
        private static bool _isSanitizing;

        private static void SanitizeModifiedAddresses(AddressableAssetSettings settings)
        {
            if (settings == null || _isSanitizing) return;

            _isSanitizing = true;
            try
            {
                var existingAddresses = new HashSet<string>();

                // First pass: collect all addresses
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    foreach (var entry in group.entries)
                        existingAddresses.Add(entry.address);
                }

                bool changed = false;
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    if (AddressableReferencedOnlyGroupHandler.IsReferencedOnlyGroup(group)) continue;

                    foreach (var entry in group.entries)
                    {
                        string sanitized = SanitizeName(entry.address);
                        if (sanitized == entry.address) continue;

                        existingAddresses.Remove(entry.address);
                        string newAddress = sanitized;
                        if (existingAddresses.Contains(newAddress))
                        {
                            int suffix = 1;
                            while (existingAddresses.Contains(newAddress))
                            {
                                newAddress = $"{sanitized}_{suffix}";
                                suffix++;
                            }
                        }

                        Debug.Log($"[Addressable Extension] Address \"{entry.address}\" → \"{newAddress}\" (auto-sanitized)");
                        entry.SetAddress(newAddress);
                        existingAddresses.Add(newAddress);
                        changed = true;
                    }
                }

                if (changed)
                    AssetDatabase.SaveAssets();
            }
            finally
            {
                _isSanitizing = false;
            }
        }

        internal static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_";

            var sanitized = Regex.Replace(name, "[^a-zA-Z0-9_]", "_");
            sanitized = Regex.Replace(sanitized, "_+", "_");
            sanitized = sanitized.Trim('_');

            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
                sanitized = "_" + sanitized;

            if (string.IsNullOrEmpty(sanitized))
                return "_";

            return sanitized;
        }

        private static void SimplifyAddress(AddressableAssetEntry entry, HashSet<string> existingAddresses)
        {
            string assetName = Path.GetFileNameWithoutExtension(entry.AssetPath);

            if (string.IsNullOrEmpty(assetName))
                return;

            string sanitized = SanitizeName(assetName);

            if (entry.address == sanitized)
                return;

            string oldAddress = entry.address;
            string newAddress = sanitized;

            if (existingAddresses.Contains(newAddress))
            {
                string conflictWith = newAddress;
                int suffix = 1;
                while (existingAddresses.Contains(newAddress))
                {
                    newAddress = $"{sanitized}_{suffix}";
                    suffix++;
                }
                Debug.Log($"[Addressable Extension] Address \"{oldAddress}\" → \"{newAddress}\" (conflict with existing \"{conflictWith}\")");
            }
            else
            {
                var invalidChars = assetName.Where(c => !char.IsLetterOrDigit(c) && c != '_').Distinct();
                var charList = string.Join(", ", invalidChars.Select(c => c == ' ' ? "space" : $"'{c}'"));
                Debug.Log($"[Addressable Extension] Address \"{oldAddress}\" → \"{newAddress}\" (simplified from asset name: {charList} cannot be used in C# identifiers, replaced with '_' for code autocomplete)");
            }

            entry.SetAddress(newAddress);
        }

        [MenuItem("Window/Asset Management/Addressables Extension/Simplify All Addresses", priority = 2053)]
        private static void SimplifyAllAddresses()
        {
            if (!AddressableExtensionSettingsManager.Data.enableNames)
            {
                Debug.LogWarning("[Addressable Extension] Addressable Names feature is disabled in settings.");
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogWarning("[Addressable Extension] Addressable settings not found.");
                return;
            }

            var existingAddresses = new HashSet<string>();
            int count = 0;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                if (AddressableReferencedOnlyGroupHandler.IsReferencedOnlyGroup(group)) continue;

                foreach (var entry in group.entries.ToList())
                {
                    string oldAddress = entry.address;
                    SimplifyAddress(entry, existingAddresses);
                    existingAddresses.Add(entry.address);

                    if (oldAddress != entry.address)
                        count++;
                }
            }

            settings.SetDirty(ModificationEvent.EntryModified, null, true, true);
            Debug.Log($"[Addressable Extension] Simplified {count} addresses.");
        }
    }
}
