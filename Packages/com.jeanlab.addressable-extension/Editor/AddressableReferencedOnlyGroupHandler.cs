using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using ModificationEvent = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent;

namespace AddressableExtension.Editor
{
    [InitializeOnLoad]
    internal static class AddressableReferencedOnlyGroupHandler
    {
        private const string TemplateName = "Referenced Only";

        static AddressableReferencedOnlyGroupHandler()
        {
            EditorApplication.delayCall += () =>
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings != null)
                {
                    EnsureTemplateRegistered(settings);

                    settings.OnModification -= OnModification;
                    settings.OnModification += OnModification;
                }
            };
        }

        internal static bool IsReferencedOnlyGroup(AddressableAssetGroup group)
        {
            if (group == null) return false;
            return group.GetSchema<ReferencedOnlyGroupSchema>() != null;
        }

        private static void EnsureTemplateRegistered(AddressableAssetSettings settings)
        {
            var settingsPath = AssetDatabase.GetAssetPath(settings);
            if (string.IsNullOrEmpty(settingsPath))
                return;

            AddressableAssetGroupTemplate existing = null;
            foreach (var obj in settings.GroupTemplateObjects)
            {
                if (obj is AddressableAssetGroupTemplate t && t.name == TemplateName)
                {
                    existing = t;
                    break;
                }
            }

            if (existing == null)
            {
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(settingsPath);
                foreach (var sub in subAssets)
                {
                    if (sub is AddressableAssetGroupTemplate t && t.name == TemplateName)
                    {
                        existing = t;
                        settings.AddGroupTemplateObject(t);
                        AssetDatabase.SaveAssets();
                        break;
                    }
                }
            }

            if (existing == null)
            {
                try
                {
                    existing = ScriptableObject.CreateInstance<AddressableAssetGroupTemplate>();
                    existing.name = TemplateName;
                    existing.Description = "Group for referenced-only assets.";
                    AssetDatabase.AddObjectToAsset(existing, settingsPath);
                    settings.AddGroupTemplateObject(existing);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[Addressable Extension] Registered 'Referenced Only' group template.");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Addressable Extension] Template registration skipped: {e.Message}");
                    return;
                }
            }

            if (!existing.HasSchema(typeof(BundledAssetGroupSchema)))
                existing.AddSchema(typeof(BundledAssetGroupSchema));
            if (!existing.HasSchema(typeof(ContentUpdateGroupSchema)))
                existing.AddSchema(typeof(ContentUpdateGroupSchema));
            if (!existing.HasSchema(typeof(ReferencedOnlyGroupSchema)))
                existing.AddSchema(typeof(ReferencedOnlyGroupSchema));
        }


        private static void OnModification(AddressableAssetSettings settings, ModificationEvent evt, object data)
        {
            if (evt == ModificationEvent.GroupAdded)
            {
                var group = data as AddressableAssetGroup;
                if (group != null && IsReferencedOnlyGroup(group))
                {
                    EditorApplication.delayCall += () => ReferencedOnlyGroupSchema.EnforceSchemaSettings(group);
                }
            }
            else if (evt == ModificationEvent.GroupSchemaModified)
            {
                var schema = data as AddressableAssetGroupSchema;
                if (schema != null && schema.Group != null && IsReferencedOnlyGroup(schema.Group))
                {
                    if (schema is BundledAssetGroupSchema)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (schema != null && schema.Group != null)
                                ReferencedOnlyGroupSchema.EnforceSchemaSettings(schema.Group);
                        };
                    }
                }
            }
        }
    }
}
