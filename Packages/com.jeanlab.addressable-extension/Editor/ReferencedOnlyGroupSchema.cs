using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace AddressableExtension.Editor
{
    public class ReferencedOnlyGroupSchema : AddressableAssetGroupSchema
    {
        internal static void EnforceSchemaSettings(AddressableAssetGroup group)
        {
            if (group == null) return;

            var bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundleSchema != null)
            {
                bool changed = false;

                if (bundleSchema.IncludeAddressInCatalog)  { bundleSchema.IncludeAddressInCatalog = false; changed = true; }
                if (bundleSchema.IncludeGUIDInCatalog)     { bundleSchema.IncludeGUIDInCatalog = false; changed = true; }
                if (bundleSchema.IncludeLabelsInCatalog)   { bundleSchema.IncludeLabelsInCatalog = false; changed = true; }

                if (changed)
                    EditorUtility.SetDirty(bundleSchema);
            }

        }

        public override void OnGUI()
        {
            if (Group != null)
                EnforceSchemaSettings(Group);

            EditorGUILayout.HelpBox(
                "This is a Referenced Only group. The following settings are automatically enforced:\n\n" +
                "• Include Addresses in Catalog: OFF\n" +
                "• Include GUIDs in Catalog: OFF\n" +
                "• Include Labels in Catalog: OFF",
                MessageType.Info);
        }
    }
}
