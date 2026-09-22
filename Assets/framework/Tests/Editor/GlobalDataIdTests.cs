using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Haven.Framework.Tests
{
    public sealed class GlobalDataIdTests
    {
        [Test]
        public void EverySurvivalEngineCraftDataId_IsGloballyUnique()
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new List<string>();
            var missing = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (!asset || !InheritsFrom(asset.GetType(), "SurvivalEngine.CraftData"))
                    continue;

                var serialized = new SerializedObject(asset);
                var idProperty = serialized.FindProperty("id");
                var id = idProperty?.stringValue?.Trim() ?? string.Empty;
                if (id.Length == 0)
                {
                    missing.Add(path);
                    continue;
                }
                if (owners.TryGetValue(id, out var previous))
                    duplicates.Add($"'{id}': {previous} <-> {path}");
                else
                    owners.Add(id, path);
            }

            Assert.That(missing, Is.Empty, "CraftData assets with an empty global id:\n" + string.Join("\n", missing));
            Assert.That(duplicates, Is.Empty, "Duplicate global CraftData ids:\n" + string.Join("\n", duplicates));
            Assert.That(owners.Count, Is.GreaterThan(0), "No SurvivalEngine CraftData assets were discovered.");
        }

        private static bool InheritsFrom(Type type, string fullName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, fullName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
