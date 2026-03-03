using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace AddressableExtension.Editor
{
    [InitializeOnLoad]
    internal static class AddressableKeyGenerator
    {
        private const string GeneratedKeysPath =
            "Library/com.jeanlab.addressable-extension/generated-keys.txt";

        private static HashSet<string> _generatedAddresses = new HashSet<string>();
        private static HashSet<string> _generatedLabels = new HashSet<string>();
        private static bool _needsGeneration;

        private static List<string> _addedAddresses = new List<string>();
        private static List<string> _removedAddresses = new List<string>();
        private static List<string> _addedLabels = new List<string>();
        private static List<string> _removedLabels = new List<string>();

        private static Type _groupsWindowType;
        private static EditorWindow _trackedWindow;
        private static Button _generateButton;

        private static readonly Color NormalColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);
        private static readonly Color NormalHoverColor = new Color(0.3f, 0.3f, 0.3f, 0.9f);
        private static readonly Color DirtyColor = new Color(0.2f, 0.4f, 0.7f, 0.6f);
        private static readonly Color DirtyHoverColor = new Color(0.2f, 0.4f, 0.7f, 1f);

        static AddressableKeyGenerator()
        {
            LoadGeneratedKeys();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                _groupsWindowType = asm.GetType("UnityEditor.AddressableAssets.GUI.AddressableAssetsWindow");
                if (_groupsWindowType != null) break;
            }

            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(GeneratedKeysPath))
                    SyncGeneratedKeys();

                CheckForChanges();

                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings != null)
                {
                    settings.OnModification -= OnSettingsModification;
                    settings.OnModification += OnSettingsModification;
                }
            };

            EditorApplication.update += TrackGroupsWindow;
        }

        private static void OnSettingsModification(
            AddressableAssetSettings settings,
            AddressableAssetSettings.ModificationEvent evt,
            object data)
        {
            if (evt == AddressableAssetSettings.ModificationEvent.EntryAdded ||
                evt == AddressableAssetSettings.ModificationEvent.EntryMoved ||
                evt == AddressableAssetSettings.ModificationEvent.EntryRemoved ||
                evt == AddressableAssetSettings.ModificationEvent.EntryModified ||
                evt == AddressableAssetSettings.ModificationEvent.LabelAdded ||
                evt == AddressableAssetSettings.ModificationEvent.LabelRemoved)
            {
                EditorApplication.delayCall += CheckForChanges;
            }
        }

        internal static void CheckForChanges()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                _needsGeneration = false;
                ClearDiff();
                UpdateButtonStyle();
                return;
            }

            var settingsData = AddressableExtensionSettingsManager.Data;

            var currentAddresses = new HashSet<string>();
            var currentLabels = new HashSet<string>();

            if (settingsData.enableNames)
                CollectAddresses(settings, currentAddresses);

            if (settingsData.enableLabels)
            {
                currentLabels = new HashSet<string>(settings.GetLabels());
            }

            _addedAddresses = currentAddresses.Except(_generatedAddresses).OrderBy(s => s).ToList();
            _removedAddresses = _generatedAddresses.Except(currentAddresses).OrderBy(s => s).ToList();
            _addedLabels = currentLabels.Except(_generatedLabels).OrderBy(s => s).ToList();
            _removedLabels = _generatedLabels.Except(currentLabels).OrderBy(s => s).ToList();

            _needsGeneration = _addedAddresses.Count > 0 || _removedAddresses.Count > 0 ||
                               _addedLabels.Count > 0 || _removedLabels.Count > 0;

            UpdateButtonStyle();
        }

        private static void ClearDiff()
        {
            _addedAddresses.Clear();
            _removedAddresses.Clear();
            _addedLabels.Clear();
            _removedLabels.Clear();
        }

        private static void TrackGroupsWindow()
        {
            if (_groupsWindowType == null) return;

            var windows = Resources.FindObjectsOfTypeAll(_groupsWindowType);
            var current = windows.Length > 0 ? windows[0] as EditorWindow : null;

            if (current == null)
            {
                _trackedWindow = null;
                _generateButton = null;
                return;
            }

            // Strip dirty marker (*) that Addressables adds to the window title
            if (current.titleContent.text.Contains("*"))
            {
                current.titleContent.text = current.titleContent.text.Replace(" *", "").Replace("*", "");
            }

            if (current != _trackedWindow)
            {
                _trackedWindow = current;
                _generateButton = null;
                EditorApplication.delayCall += InjectButton;
            }
            else if (_generateButton == null &&
                     _trackedWindow.rootVisualElement?.Q<Button>("ae-generate-btn") == null)
            {
                EditorApplication.delayCall += InjectButton;
            }
        }

        private static void InjectButton()
        {
            if (_trackedWindow == null) return;

            var root = _trackedWindow.rootVisualElement;
            if (root == null) return;

            var existing = root.Q<Button>("ae-generate-btn");
            if (existing != null)
            {
                _generateButton = existing;
                return;
            }

            _generateButton = new Button(OnButtonClicked)
            {
                name = "ae-generate-btn",
                text = "Generate Keys",
                style =
                {
                    position = Position.Absolute,
                    bottom = 6,
                    right = 6,
                    height = 24,
                    paddingLeft = 10,
                    paddingRight = 10,
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    fontSize = 11,
                    color = Color.white,
                }
            };

            _generateButton.RegisterCallback<MouseEnterEvent>(_ => OnButtonHover(true));
            _generateButton.RegisterCallback<MouseLeaveEvent>(_ => OnButtonHover(false));

            root.Add(_generateButton);
            UpdateButtonStyle();
        }

        private static void OnButtonHover(bool hovering)
        {
            if (_generateButton == null) return;

            if (_needsGeneration)
                _generateButton.style.backgroundColor = hovering ? DirtyHoverColor : DirtyColor;
            else
                _generateButton.style.backgroundColor = hovering ? NormalHoverColor : NormalColor;
        }

        private static string BuildSummaryText()
        {
            var parts = new List<string>();
            if (_addedAddresses.Count > 0) parts.Add($"+{_addedAddresses.Count}A");
            if (_removedAddresses.Count > 0) parts.Add($"-{_removedAddresses.Count}A");
            if (_addedLabels.Count > 0) parts.Add($"+{_addedLabels.Count}L");
            if (_removedLabels.Count > 0) parts.Add($"-{_removedLabels.Count}L");

            if (parts.Count == 0)
                return "Generate Keys";

            return "Generate Keys (" + string.Join(" ", parts) + ")";
        }

        private static string BuildTooltipText()
        {
            if (!_needsGeneration)
                return "All keys are up to date.";

            var sb = new StringBuilder();

            if (_addedAddresses.Count > 0)
            {
                sb.AppendLine("Addresses added:");
                foreach (var addr in _addedAddresses)
                    sb.AppendLine("  + " + addr);
            }

            if (_removedAddresses.Count > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("Addresses removed:");
                foreach (var addr in _removedAddresses)
                    sb.AppendLine("  - " + addr);
            }

            if (_addedLabels.Count > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("Labels added:");
                foreach (var label in _addedLabels)
                    sb.AppendLine("  + " + label);
            }

            if (_removedLabels.Count > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("Labels removed:");
                foreach (var label in _removedLabels)
                    sb.AppendLine("  - " + label);
            }

            return sb.ToString().TrimEnd();
        }

        private static void UpdateButtonStyle()
        {
            if (_generateButton == null) return;

            _generateButton.text = BuildSummaryText();
            _generateButton.tooltip = BuildTooltipText();
            _generateButton.SetEnabled(_needsGeneration);

            if (_needsGeneration)
                _generateButton.style.backgroundColor = DirtyColor;
            else
                _generateButton.style.backgroundColor = NormalColor;
        }

        private static void OnButtonClicked()
        {
            GenerateKeys();
        }

        [MenuItem("Window/Asset Management/Addressables Extension/Generate Keys", priority = 2052)]
        internal static void GenerateKeys()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogWarning("[Addressable Extension] Addressable settings not found.");
                return;
            }

            // Save pending asset changes so the Source Generator reads the latest data
            AssetDatabase.SaveAssets();
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();

            var settingsData = AddressableExtensionSettingsManager.Data;

            _generatedAddresses.Clear();
            _generatedLabels.Clear();

            if (settingsData.enableNames)
                CollectAddresses(settings, _generatedAddresses);

            if (settingsData.enableLabels)
            {
                foreach (var label in settings.GetLabels())
                    _generatedLabels.Add(label);
            }

            SaveGeneratedKeys();
            _needsGeneration = false;
            ClearDiff();
            UpdateButtonStyle();

            Debug.Log("[Addressable Extension] Keys generated.");
        }

        private static void CollectAddresses(AddressableAssetSettings settings, HashSet<string> dest)
        {
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (AssetDatabase.IsValidFolder(entry.AssetPath))
                    {
                        var children = new List<AddressableAssetEntry>();
                        entry.GatherAllAssets(children, false, true, false);
                        foreach (var child in children)
                            dest.Add(child.address);
                    }
                    else
                    {
                        dest.Add(entry.address);
                    }
                }
            }
        }

        private static void SyncGeneratedKeys()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return;

            var settingsData = AddressableExtensionSettingsManager.Data;

            _generatedAddresses.Clear();
            _generatedLabels.Clear();

            if (settingsData.enableNames)
                CollectAddresses(settings, _generatedAddresses);

            if (settingsData.enableLabels)
            {
                foreach (var label in settings.GetLabels())
                    _generatedLabels.Add(label);
            }

        }

        private static void SaveGeneratedKeys()
        {
            var dir = Path.GetDirectoryName(GeneratedKeysPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            foreach (var addr in _generatedAddresses)
                sb.AppendLine("A:" + addr);
            foreach (var label in _generatedLabels)
                sb.AppendLine("L:" + label);

            File.WriteAllText(GeneratedKeysPath, sb.ToString());
        }

        private static void LoadGeneratedKeys()
        {
            _generatedAddresses.Clear();
            _generatedLabels.Clear();

            if (!File.Exists(GeneratedKeysPath)) return;

            foreach (var line in File.ReadAllLines(GeneratedKeysPath))
            {
                if (line.StartsWith("A:"))
                    _generatedAddresses.Add(line.Substring(2));
                else if (line.StartsWith("L:"))
                    _generatedLabels.Add(line.Substring(2));
            }
        }
    }
}
