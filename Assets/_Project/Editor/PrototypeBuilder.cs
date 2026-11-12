using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace LuckyNumbers.PhysicsPrototype.Editor
{
    public static class PrototypeBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Gameplay.unity";

        [MenuItem("Prototype/Build Prototype Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 1. Configure collision layers in Physics
            ConfigureCollisionLayers();

            // 2. Load materials
            Material interiorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/IcosahedronInterior.mat");
            Material exteriorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/IcosahedronExterior.mat");
            Material edgeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/IcosahedronEdge.mat");
            Material playerMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/PlayerMaterial.mat");
            PhysicsMaterial interiorPhysMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/_Project/Materials/InteriorPhysicMaterial.physicMaterial");
            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Project/InputSystem_Actions.inputactions");

            // 3. Find or Create Icosahedron
            GameObject icosahedron = GameObject.Find("Icosahedron");
            if (icosahedron == null)
            {
                icosahedron = new GameObject("Icosahedron");
            }
            icosahedron.transform.position = Vector3.zero;
            icosahedron.transform.rotation = Quaternion.identity;

            Rigidbody icoRb = icosahedron.GetComponent<Rigidbody>();
            if (icoRb == null) icoRb = icosahedron.AddComponent<Rigidbody>();
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

            IcosahedronController icoController = icosahedron.GetComponent<IcosahedronController>();
            if (icoController == null) icoController = icosahedron.AddComponent<IcosahedronController>();
            SerializedObject icoCtrlSo = new SerializedObject(icoController);
            icoCtrlSo.FindProperty("targetRigidbody").objectReferenceValue = icoRb;
            icoCtrlSo.FindProperty("rollMode").enumValueIndex = 0; // SmoothCenterRoll
            icoCtrlSo.FindProperty("enableWorldTravel").boolValue = true;
            icoCtrlSo.FindProperty("tableFloorY").floatValue = -8.6f;
            icoCtrlSo.FindProperty("outerFaceRadius").floatValue = 6.424f;
            icoCtrlSo.FindProperty("travelSpeedMultiplier").floatValue = 1.0f;
            icoCtrlSo.FindProperty("rollAcceleration").floatValue = 24.0f;
            icoCtrlSo.FindProperty("angularDamping").floatValue = 1.5f;
            icoCtrlSo.FindProperty("maxAngularVelocity").floatValue = 4.0f;
            icoCtrlSo.FindProperty("settleStiffness").floatValue = 14.0f;
            icoCtrlSo.FindProperty("settleDamping").floatValue = 7.5f;
            icoCtrlSo.FindProperty("flatAngleThreshold").floatValue = 3.0f;
            icoCtrlSo.ApplyModifiedProperties();

            IcosahedronGenerator icoGen = icosahedron.GetComponent<IcosahedronGenerator>();
            if (icoGen == null) icoGen = icosahedron.AddComponent<IcosahedronGenerator>();

            // Set materials via serialized properties or fields
            SerializedObject genSo = new SerializedObject(icoGen);
            genSo.FindProperty("interiorMaterial").objectReferenceValue = interiorMat;
            genSo.FindProperty("exteriorMaterial").objectReferenceValue = exteriorMat;
            genSo.FindProperty("edgeMaterial").objectReferenceValue = edgeMat;
            genSo.FindProperty("interiorPhysicMaterial").objectReferenceValue = interiorPhysMat;
            genSo.ApplyModifiedProperties();

            icoGen.BuildIcosahedron();

            // 4. Find or Create Player inside the Icosahedron
            float floorY = -icoGen.FaceCenterDistance;
            Vector3 spawnPos = new Vector3(0f, floorY + 1.05f, 0f);

            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                player.name = "Player";
            }

            player.transform.position = spawnPos;
            player.transform.rotation = Quaternion.identity;

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer < 0) playerLayer = 8;
            player.layer = playerLayer;

            MeshRenderer playerRenderer = player.GetComponent<MeshRenderer>();
            if (playerRenderer != null && playerMat != null)
            {
                playerRenderer.sharedMaterial = playerMat;
            }

            CapsuleCollider playerCol = player.GetComponent<CapsuleCollider>();
            if (playerCol == null) playerCol = player.AddComponent<CapsuleCollider>();
            playerCol.center = new Vector3(0f, 0f, 0f);
            playerCol.height = 2f;
            playerCol.radius = 0.5f;
            playerCol.material = interiorPhysMat;
            playerCol.contactOffset = 0.005f;

            Rigidbody playerRb = player.GetComponent<Rigidbody>();
            if (playerRb == null) playerRb = player.AddComponent<Rigidbody>();
            playerRb.mass = 70f;
            playerRb.useGravity = true;
            playerRb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            playerRb.interpolation = RigidbodyInterpolation.Interpolate;
            playerRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            playerRb.solverIterations = 16;
            playerRb.solverVelocityIterations = 8;
            playerRb.maxDepenetrationVelocity = 8.0f;

            PlayerController playerCtrl = player.GetComponent<PlayerController>();
            if (playerCtrl == null) playerCtrl = player.AddComponent<PlayerController>();

            SerializedObject playerCtrlSo = new SerializedObject(playerCtrl);
            playerCtrlSo.FindProperty("icosahedronRigidbody").objectReferenceValue = icoRb;
            playerCtrlSo.FindProperty("moveSpeed").floatValue = 10.0f;
            playerCtrlSo.FindProperty("acceleration").floatValue = 30.0f;
            playerCtrlSo.FindProperty("gravityMultiplier").floatValue = 2.5f;
            playerCtrlSo.FindProperty("groundStickStrength").floatValue = 20.0f;
            playerCtrlSo.FindProperty("airControl").floatValue = 0.2f;
            playerCtrlSo.FindProperty("minRollSlopeAngle").floatValue = 10.0f;
            playerCtrlSo.FindProperty("maxRollSlopeAngle").floatValue = 35.0f;
            playerCtrlSo.FindProperty("rollResponsiveness").floatValue = 1.4f;
            playerCtrlSo.FindProperty("heightTorqueMultiplier").floatValue = 0.5f;
            playerCtrlSo.FindProperty("wallPushBoost").floatValue = 1.2f;
            playerCtrlSo.FindProperty("minImpactSpeedForKick").floatValue = 1.5f;
            playerCtrlSo.FindProperty("wallImpactKickStrength").floatValue = 1.6f;
            playerCtrlSo.FindProperty("minWalkableNormalY").floatValue = 0.55f;
            playerCtrlSo.FindProperty("maxClimbHeight").floatValue = -1.8f;

            // Find Main Camera
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                playerCtrlSo.FindProperty("cameraTransform").objectReferenceValue = mainCam.transform;

                if (inputActions != null)
                {
                    InputAction moveAct = inputActions.FindAction("Player/Move");
                    if (moveAct != null)
                    {
                        // Note: InputActionReference can be assigned if an asset exists
                    }
                }

                // 5. Setup Camera Controller
                CameraController camCtrl = mainCam.GetComponent<CameraController>();
                if (camCtrl == null) camCtrl = mainCam.gameObject.AddComponent<CameraController>();
                camCtrl.SetTarget(player.transform);
                mainCam.transform.position = spawnPos + new Vector3(0f, 6.0f, -16.0f);
                mainCam.transform.LookAt(spawnPos + Vector3.up * 1.5f);
            }
            playerCtrlSo.ApplyModifiedProperties();

            // 6. Setup Debug UI Canvas
            GameObject uiCanvasObj = GameObject.Find("DebugRotationUI");
            if (uiCanvasObj == null)
            {
                uiCanvasObj = new GameObject("DebugRotationUI");
            }

            DebugRotationUI debugUI = uiCanvasObj.GetComponent<DebugRotationUI>();
            if (debugUI == null) debugUI = uiCanvasObj.AddComponent<DebugRotationUI>();

            SerializedObject debugUiSo = new SerializedObject(debugUI);
            debugUiSo.FindProperty("controller").objectReferenceValue = icoController;
            debugUiSo.ApplyModifiedProperties();
            debugUI.BuildUI();

            // 7. Setup External World Test Object
            GameObject externalObj = GameObject.Find("ExternalTestSphere");
            if (externalObj == null)
            {
                externalObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                externalObj.name = "ExternalTestSphere";
            }
            externalObj.layer = LayerMask.NameToLayer("Default");
            externalObj.transform.position = new Vector3(0f, icoGen.InnerRadius + icoGen.WallThickness + 2.0f, 0f);
            externalObj.transform.localScale = Vector3.one * 1.5f;

            Rigidbody extRb = externalObj.GetComponent<Rigidbody>();
            if (extRb == null) extRb = externalObj.AddComponent<Rigidbody>();
            extRb.mass = 50f;
            extRb.useGravity = true;

            // 8. Position Plane table right beneath the icosahedron (outer radius 8.5m)
            GameObject plane = GameObject.Find("Plane");
            if (plane != null)
            {
                plane.transform.position = new Vector3(0f, -8.6f, 0f);
                MeshCollider planeCol = plane.GetComponent<MeshCollider>();
                if (planeCol != null) planeCol.material = interiorPhysMat;
            }

            // Mark dirty and save
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("[PrototypeBuilder] Successfully built and saved physics prototype scene.");
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

            // ExteriorShell interacts with external world (Default), NOT with Player, NOT with InteriorSurface
            Physics.IgnoreLayerCollision(exteriorLayer, playerLayer, true);
            Physics.IgnoreLayerCollision(exteriorLayer, interiorLayer, true);
            Physics.IgnoreLayerCollision(exteriorLayer, defaultLayer, false);

            // InteriorSurface interacts with Player, NOT with external world (Default), NOT with ExteriorShell
            Physics.IgnoreLayerCollision(interiorLayer, playerLayer, false);
            Physics.IgnoreLayerCollision(interiorLayer, defaultLayer, true);
            Physics.IgnoreLayerCollision(interiorLayer, exteriorLayer, true);

            // Player interacts with InteriorSurface, NOT with ExteriorShell, interacts with Default
            Physics.IgnoreLayerCollision(playerLayer, exteriorLayer, true);
            Physics.IgnoreLayerCollision(playerLayer, interiorLayer, false);
            Physics.IgnoreLayerCollision(playerLayer, defaultLayer, false);
        }
    }

    [InitializeOnLoad]
    public static class PrototypeAutoRunner
    {
        static PrototypeAutoRunner()
        {
            EditorApplication.delayCall += RunOnce;
        }

        private static void RunOnce()
        {
            if (!SessionState.GetBool("PrototypeSceneBuilt_v7", false))
            {
                SessionState.SetBool("PrototypeSceneBuilt_v7", true);
                if (!Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    PrototypeBuilder.BuildScene();
                }
            }
        }
    }
}
