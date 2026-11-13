using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LuckyNumbers.PhysicsPrototype.Editor
{
    /// <summary>
    /// Editor utility for constructing or verifying prototype scene GameObjects without overriding Inspector values.
    /// </summary>
    public static class PrototypeBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Gameplay.unity";

        [MenuItem("Prototype/Build Prototype Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            ConfigureCollisionLayers();

            Material interiorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/IcosahedronInterior.mat");
            Material exteriorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/IcosahedronExterior.mat");
            Material edgeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/IcosahedronEdge.mat");
            Material playerMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/PlayerMaterial.mat");
            PhysicsMaterial interiorPhysMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/_Project/Materials/InteriorPhysicMaterial.physicMaterial");

            GameObject icosahedron = GameObject.Find("Icosahedron");
            if (icosahedron == null)
            {
                icosahedron = new GameObject("Icosahedron");
                icosahedron.transform.position = Vector3.zero;
                icosahedron.transform.rotation = Quaternion.identity;
            }

            Rigidbody icoRb = icosahedron.GetComponent<Rigidbody>();
            if (icoRb == null)
            {
                icoRb = icosahedron.AddComponent<Rigidbody>();
                icoRb.mass = 200f;
                icoRb.useGravity = true;
                icoRb.constraints = RigidbodyConstraints.None;
                icoRb.angularDamping = 1.8f;
                icoRb.maxAngularVelocity = 6.0f;
                icoRb.interpolation = RigidbodyInterpolation.Interpolate;
                icoRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                icoRb.solverIterations = 16;
                icoRb.solverVelocityIterations = 8;
                icoRb.maxDepenetrationVelocity = 8.0f;
            }

            IcosahedronController icoController = icosahedron.GetComponent<IcosahedronController>();
            if (icoController == null)
            {
                icoController = icosahedron.AddComponent<IcosahedronController>();
            }

            IcosahedronGenerator icoGen = icosahedron.GetComponent<IcosahedronGenerator>();
            if (icoGen == null)
            {
                icoGen = icosahedron.AddComponent<IcosahedronGenerator>();
                SerializedObject genSo = new SerializedObject(icoGen);
                genSo.FindProperty("interiorMaterial").objectReferenceValue = interiorMat;
                genSo.FindProperty("exteriorMaterial").objectReferenceValue = exteriorMat;
                genSo.FindProperty("edgeMaterial").objectReferenceValue = edgeMat;
                genSo.FindProperty("interiorPhysicMaterial").objectReferenceValue = interiorPhysMat;
                genSo.ApplyModifiedProperties();
                icoGen.BuildIcosahedron();
            }

            float floorY = -icoGen.FaceCenterDistance;
            Vector3 spawnPos = new Vector3(0f, floorY + 1.05f, 0f);

            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                player.name = "Player";
                player.transform.position = spawnPos;
                player.transform.rotation = Quaternion.identity;
            }

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer < 0) playerLayer = 8;
            player.layer = playerLayer;

            MeshRenderer playerRenderer = player.GetComponent<MeshRenderer>();
            if (playerRenderer != null && playerMat != null && playerRenderer.sharedMaterial == null)
            {
                playerRenderer.sharedMaterial = playerMat;
            }

            CapsuleCollider playerCol = player.GetComponent<CapsuleCollider>();
            if (playerCol == null)
            {
                playerCol = player.AddComponent<CapsuleCollider>();
                playerCol.center = Vector3.zero;
                playerCol.height = 2f;
                playerCol.radius = 0.5f;
                playerCol.material = interiorPhysMat;
                playerCol.contactOffset = 0.005f;
            }

            Rigidbody playerRb = player.GetComponent<Rigidbody>();
            if (playerRb == null)
            {
                playerRb = player.AddComponent<Rigidbody>();
                playerRb.mass = 70f;
                playerRb.useGravity = true;
                playerRb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                playerRb.interpolation = RigidbodyInterpolation.Interpolate;
                playerRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                playerRb.solverIterations = 16;
                playerRb.solverVelocityIterations = 8;
                playerRb.maxDepenetrationVelocity = 8.0f;
            }

            PlayerController playerCtrl = player.GetComponent<PlayerController>();
            if (playerCtrl == null)
            {
                playerCtrl = player.AddComponent<PlayerController>();
            }

            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                CameraController camCtrl = mainCam.GetComponent<CameraController>();
                if (camCtrl == null)
                {
                    camCtrl = mainCam.gameObject.AddComponent<CameraController>();
                    camCtrl.SetTarget(player.transform);
                }
            }

            GameObject uiCanvasObj = GameObject.Find("DebugRotationUI");
            if (uiCanvasObj == null)
            {
                uiCanvasObj = new GameObject("DebugRotationUI");
            }

            DebugRotationUI debugUI = uiCanvasObj.GetComponent<DebugRotationUI>();
            if (debugUI == null)
            {
                debugUI = uiCanvasObj.AddComponent<DebugRotationUI>();
                debugUI.BuildUI();
            }

            GameObject scopeObj = GameObject.Find("GameLifetimeScope");
            if (scopeObj == null)
            {
                scopeObj = new GameObject("GameLifetimeScope");
            }
            GameLifetimeScope scope = scopeObj.GetComponent<GameLifetimeScope>();
            if (scope == null)
            {
                scope = scopeObj.AddComponent<GameLifetimeScope>();
            }

            GameObject plane = GameObject.Find("Plane");
            if (plane != null)
            {
                MeshCollider planeCol = plane.GetComponent<MeshCollider>();
                if (planeCol != null && planeCol.material == null)
                {
                    planeCol.material = interiorPhysMat;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("[PrototypeBuilder] Successfully verified physics prototype scene without overriding inspector properties.");
        }

        private static void ConfigureCollisionLayers()
        {
            int defaultLayer = 0;
            int playerLayer = LayerMask.NameToLayer("Player");
            int exteriorLayer = LayerMask.NameToLayer("ExteriorShell");
            int interiorLayer = LayerMask.NameToLayer("InteriorSurface");

            if (playerLayer < 0) playerLayer = 8;
            if (exteriorLayer < 0) exteriorLayer = 9;
            if (interiorLayer < 0) interiorLayer = 10;

            Physics.IgnoreLayerCollision(exteriorLayer, playerLayer, true);
            Physics.IgnoreLayerCollision(exteriorLayer, interiorLayer, true);
            Physics.IgnoreLayerCollision(exteriorLayer, defaultLayer, false);

            Physics.IgnoreLayerCollision(interiorLayer, playerLayer, false);
            Physics.IgnoreLayerCollision(interiorLayer, defaultLayer, true);
            Physics.IgnoreLayerCollision(interiorLayer, exteriorLayer, true);

            Physics.IgnoreLayerCollision(playerLayer, exteriorLayer, true);
            Physics.IgnoreLayerCollision(playerLayer, interiorLayer, false);
            Physics.IgnoreLayerCollision(playerLayer, defaultLayer, false);
        }
    }
}
