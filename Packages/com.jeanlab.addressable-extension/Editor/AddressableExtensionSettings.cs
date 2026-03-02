using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace AddressableExtension.Editor
{
    [Serializable]
    internal class AddressableExtensionSettingsData
    {
        public bool enableNames = true;
        public bool enableLabels = true;
        public bool initialized = false;
    }

    internal static class AddressableExtensionSettingsManager
    {
        private const string SettingsPath = "ProjectSettings/AddressableExtensionSettings.json";

        private static AddressableExtensionSettingsData _data;

        internal static AddressableExtensionSettingsData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        internal static void Load()
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _data = JsonUtility.FromJson<AddressableExtensionSettingsData>(json);
            }
            else
            {
                _data = new AddressableExtensionSettingsData();
            }
        }

        internal static void Save()
        {
            var json = JsonUtility.ToJson(_data, true);
            File.WriteAllText(SettingsPath, json);
        }
    }

    internal class AddressableExtensionSettingsWindow : EditorWindow
    {
        [MenuItem("Window/Asset Management/Addressables Extension/Settings", priority = 2054)]
        private static void ShowWindow()
        {
            var window = GetWindow<AddressableExtensionSettingsWindow>();
            window.titleContent = new GUIContent("Addressables Extension");
            window.minSize = new Vector2(300, 150);
            window.Show();
        }

        private void OnGUI()
        {
            var data = AddressableExtensionSettingsManager.Data;

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Addressables Extension Settings", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUI.BeginChangeCheck();

            data.enableNames = EditorGUILayout.ToggleLeft(
                new GUIContent("Generate AddressableNames"),
                data.enableNames);

            var prevColor = GUI.color;
            GUI.color = Color.gray;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                "String constants for all Addressable addresses, enabling code autocomplete.\n" +
                "Also auto-simplifies new addresses.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
            GUI.color = prevColor;

            GUILayout.Space(6);

            data.enableLabels = EditorGUILayout.ToggleLeft(
                new GUIContent("Generate AddressableLabels"),
                data.enableLabels);

            prevColor = GUI.color;
            GUI.color = Color.gray;
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                "String constants for all Addressable labels, enabling code autocomplete.\n" +
                "Also auto-sanitizes new labels.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
            GUI.color = prevColor;

            if (EditorGUI.EndChangeCheck())
            {
                AddressableExtensionSettingsManager.Save();
                AddressableKeyGenerator.GenerateKeys();
            }
        }
    }

    [InitializeOnLoad]
    internal static class AddressableExtensionInitializer
    {
        static AddressableExtensionInitializer()
        {
            EditorApplication.delayCall += CheckFirstTime;
        }

        private static void CheckFirstTime()
        {
            var data = AddressableExtensionSettingsManager.Data;
            if (data.initialized)
                return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                data.initialized = true;
                AddressableExtensionSettingsManager.Save();
                return;
            }

            var changes = ScanChanges(settings);

            if (changes.Count == 0)
            {
                data.initialized = true;
                AddressableExtensionSettingsManager.Save();
                return;
            }

            AddressableExtensionSetupWindow.Show(changes);
        }

        internal struct ChangeEntry
        {
            public string category;
            public string oldValue;
            public string newValue;
            public string reason;
        }

        internal static List<ChangeEntry> ScanChanges(AddressableAssetSettings settings)
        {
            var changes = new List<ChangeEntry>();

            // Scan addresses
            var existingAddresses = new HashSet<string>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    string assetName = Path.GetFileNameWithoutExtension(entry.AssetPath);
                    string sanitized = SanitizeLabelName(assetName);
                    if (string.IsNullOrEmpty(sanitized) || entry.address == sanitized)
                    {
                        existingAddresses.Add(entry.address);
                        continue;
                    }

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
                        changes.Add(new ChangeEntry
                        {
                            category = "Address",
                            oldValue = entry.address,
                            newValue = newAddress,
                            reason = $"conflict with \"{conflictWith}\""
                        });
                    }
                    else
                    {
                        changes.Add(new ChangeEntry
                        {
                            category = "Address",
                            oldValue = entry.address,
                            newValue = newAddress,
                            reason = "simplified from asset path"
                        });
                    }
                    existingAddresses.Add(newAddress);
                }
            }

            // Scan labels
            var existingLabels = new HashSet<string>();
            foreach (var label in settings.GetLabels())
            {
                string sanitized = SanitizeLabelName(label);
                if (sanitized == label)
                {
                    existingLabels.Add(label);
                    continue;
                }

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
                    changes.Add(new ChangeEntry
                    {
                        category = "Label",
                        oldValue = label,
                        newValue = finalName,
                        reason = $"conflict with \"{conflictWith}\""
                    });
                }
                else
                {
                    changes.Add(new ChangeEntry
                    {
                        category = "Label",
                        oldValue = label,
                        newValue = finalName,
                        reason = "contains invalid identifier characters"
                    });
                }
                existingLabels.Add(finalName);
            }

            return changes;
        }

        private static string SanitizeLabelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";
            var sanitized = Regex.Replace(name, "[^a-zA-Z0-9_]", "_");
            sanitized = Regex.Replace(sanitized, "_+", "_");
            sanitized = sanitized.Trim('_');
            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
                sanitized = "_" + sanitized;
            if (string.IsNullOrEmpty(sanitized))
                return "_";
            return sanitized;
        }
    }

    internal class AddressableExtensionSetupWindow : EditorWindow
    {
        private List<AddressableExtensionInitializer.ChangeEntry> _changes;
        private Vector2 _scrollPos;

        internal static void Show(List<AddressableExtensionInitializer.ChangeEntry> changes)
        {
            var window = GetWindow<AddressableExtensionSetupWindow>(true);
            window.titleContent = new GUIContent("Addressables Extension Setup");
            window._changes = changes;
            window.minSize = new Vector2(500, 300);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (_changes == null)
            {
                Close();
                return;
            }

            GUILayout.Space(10);
            EditorGUILayout.LabelField("The following items need to be changed for code autocomplete:", EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var addressChanges = _changes.Where(c => c.category == "Address").ToList();
            var labelChanges = _changes.Where(c => c.category == "Label").ToList();

            if (addressChanges.Count > 0)
            {
                EditorGUILayout.LabelField($"Addresses ({addressChanges.Count})", EditorStyles.boldLabel);
                foreach (var change in addressChanges)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField($"\"{change.oldValue}\"  →  \"{change.newValue}\"", EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(32);
                    var prevColor = GUI.color;
                    GUI.color = Color.gray;
                    EditorGUILayout.LabelField(change.reason, EditorStyles.miniLabel);
                    GUI.color = prevColor;
                    EditorGUILayout.EndHorizontal();
                }
                GUILayout.Space(8);
            }

            if (labelChanges.Count > 0)
            {
                EditorGUILayout.LabelField($"Labels ({labelChanges.Count})", EditorStyles.boldLabel);
                foreach (var change in labelChanges)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    EditorGUILayout.LabelField($"\"{change.oldValue}\"  →  \"{change.newValue}\"", EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(32);
                    var prevColor = GUI.color;
                    GUI.color = Color.gray;
                    EditorGUILayout.LabelField(change.reason, EditorStyles.miniLabel);
                    GUI.color = prevColor;
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(100)))
            {
                var data = AddressableExtensionSettingsManager.Data;
                data.initialized = true;
                AddressableExtensionSettingsManager.Save();
                Close();
            }

            GUILayout.Space(8);

            if (GUILayout.Button("Apply", GUILayout.Width(100)))
            {
                ApplyChanges();
                var data = AddressableExtensionSettingsManager.Data;
                data.initialized = true;
                AddressableExtensionSettingsManager.Save();
                Close();
            }

            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void ApplyChanges()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var renameLabelMethod = typeof(AddressableAssetSettings).GetMethod(
                "RenameLabel",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string), typeof(bool) },
                null);

            foreach (var change in _changes.Where(c => c.category == "Address"))
            {
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    foreach (var entry in group.entries)
                    {
                        if (entry.address == change.oldValue)
                        {
                            entry.SetAddress(change.newValue);
                            break;
                        }
                    }
                }
            }

            foreach (var change in _changes.Where(c => c.category == "Label"))
            {
                if (renameLabelMethod != null)
                    renameLabelMethod.Invoke(settings, new object[] { change.oldValue, change.newValue, true });
            }

            int addrCount = _changes.Count(c => c.category == "Address");
            int labelCount = _changes.Count(c => c.category == "Label");
            Debug.Log($"[Addressable Extension] Setup complete: {addrCount} addresses, {labelCount} labels updated.");
        }
    }
}
