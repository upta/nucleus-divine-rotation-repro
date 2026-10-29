using System;
using System.Reflection;
using Nucleus.Integrations.Unity.Managers.Core;
using Nucleus.Integrations.Unity.Managers.NetworkLoop;
using Nucleus.Integrations.Unity.Managers.Physics;
using Nucleus.Integrations.Unity.Managers.Systems;
using Nucleus.Integrations.Unity.Managers.Transports;
using Nucleus.Integrations.Unity.Physics;
using Nucleus.Integrations.Unity.Transports;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DivineRotationRepro.Editor
{
    /// <summary>
    /// Builds the repro's one scene and its Windows player: a network manager (50 Hz tick, Nucleus stepping physics, UDP),
    /// and a heavy floating body placed in the scene, turned 180 degrees, carrying NetworkRigidbody with a smoothed visual.
    /// Menu Repro > Build, or batch mode: -executeMethod DivineRotationRepro.Editor.ReproBuilder.Build
    /// </summary>
    public static class ReproBuilder
    {
        public const string ScenePath = "Assets/Repro/Repro.unity";
        public const string PlayerPath = "Builds/Repro/Repro.exe";

        [MenuItem("Repro/Build")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Camera camera = Camera.main;
            camera.transform.SetPositionAndRotation(new Vector3(30f, 15f, -30f), Quaternion.Euler(20f, -45f, 0f));

            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            water.transform.localScale = new Vector3(20f, 1f, 20f);
            UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());

            var network = new GameObject("NetworkManager");
            network.AddComponent<UnityCoreManager>();
            var loop = new SerializedObject(network.AddComponent<UnityNetworkLoopManager>());
            loop.FindProperty("_tickRate").intValue = 50;
            loop.ApplyModifiedPropertiesWithoutUndo();
            UnityTransportManager transports = network.AddComponent<UnityTransportManager>();
            network.AddComponent<UnityPhysicsManager>(); // NucleusPerTick, the default.
            network.AddComponent<UnitySystemManager>();
            SynapseTransport synapse = network.AddComponent<SynapseTransport>();

            // The hull: heavy, a ship's proportions, turned 180 degrees, as a moored ship facing out to sea.
            var hull = new GameObject("Hull");
            hull.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            Rigidbody body = hull.AddComponent<Rigidbody>();
            body.mass = 73100f;
            body.interpolation = RigidbodyInterpolation.None;
            hull.AddComponent<BoxCollider>().size = new Vector3(8f, 4f, 24f);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(hull.transform, false);
            AddBlock(visual, Vector3.zero, new Vector3(8f, 4f, 24f));
            AddBlock(visual, new Vector3(0f, 8f, 0f), new Vector3(0.5f, 12f, 0.5f));
            AddBlock(visual, new Vector3(0f, 3f, -11f), new Vector3(3f, 2f, 2f));
            NetworkRigidbody networkBody = hull.AddComponent<NetworkRigidbody>();
            networkBody.SmoothedVisual = visual;
            hull.AddComponent<Floater>();

            var driver = new SerializedObject(network.AddComponent<ReproDriver>());
            driver.FindProperty("_transports").objectReferenceValue = transports;
            driver.FindProperty("_synapse").objectReferenceValue = synapse;
            driver.FindProperty("_body").objectReferenceValue = networkBody;
            driver.ApplyModifiedPropertiesWithoutUndo();

            // Saving stamps the scene object's id.
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            RebuildPrefabCollection();

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 640;
            PlayerSettings.defaultScreenHeight = 360;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;

            var report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = PlayerPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                }
            );
            Debug.Log("REPRO BUILD " + report.summary.result + " -> " + PlayerPath);
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        private static void AddBlock(Transform parent, Vector3 position, Vector3 size)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
        }

        // Tools > Nucleus > Build > Rebuild Network Prefab Collection, which Nucleus keeps internal.
        private static void RebuildPrefabCollection()
        {
            Type.GetType(
                    "Nucleus.Integrations.Unity.Editor.NetworkPrefabCollectionBuilder, Nucleus.Integrations.Unity.Editor"
                )
                ?.GetMethod("Rebuild", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null);
        }
    }
}
