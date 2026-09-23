using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Haven.Framework.Tests
{
    public sealed class NetworkPresentationAssetTests
    {
        [Test]
        public void NetworkPlayerUsesSurvivalCharacterAndUrpMaterials()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Network/HavenPlayer.prefab");
            Assert.IsNotNull(player);
            Assert.IsNull(player.GetComponent<MeshFilter>(), "Legacy capsule MeshFilter is still on the network root.");
            Assert.IsNull(player.GetComponent<MeshRenderer>(), "Legacy capsule MeshRenderer is still on the network root.");
            Assert.IsNotNull(player.GetComponentInChildren<Animator>(true));
            Assert.That(File.ReadAllText("Assets/Prefabs/Network/HavenPlayer.prefab"),
                Does.Contain("_synchronizeRotation: 1"), "NetworkTransform must replicate character rotation.");
            var renderers = player.GetComponentsInChildren<Renderer>(true);
            Assert.IsNotEmpty(renderers);
            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                    Assert.IsTrue(material && material.shader && material.shader.isSupported &&
                        material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal),
                        $"{renderer.name} has a missing or non-URP material.");
            }
        }

        [Test]
        public void LobbyAndWorldAssetsHaveNoKnownPresentationRegressions()
        {
            var lobby = File.ReadAllText("Assets/Scenes/FrameworkDemo.unity");
            Assert.That(lobby, Does.Contain("m_Name: LobbyPresentation"));
            Assert.That(lobby, Does.Not.Contain("Server-authoritative Test Ground"));

            var grass = File.ReadAllText("Assets/Art/all/SurvivalEngine/Materials/FX/Grass.shader");
            Assert.That(grass, Does.Contain("Assets/Art/all/SurvivalEngine/Materials/FX/GrassPass.hlsl"));
            Assert.That(grass, Does.Not.Contain("Assets/SurvivalEngine/Materials/FX/GrassPass.hlsl"));

            var world = File.ReadAllText("Assets/Scenes/WorldGenMap.unity");
            var ids = Regex.Matches(world, @"propertyPath: unique_id\s+value:\s*([^\r\n]*)")
                .Select(match => match.Groups[1].Value.Trim()).ToArray();
            Assert.Greater(ids.Length, 0, "WorldGenMap has no scene interaction IDs.");
            Assert.That(ids, Has.None.Empty);
            Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count(),
                "WorldGenMap contains duplicate serialized interaction IDs.");
        }
    }
}
