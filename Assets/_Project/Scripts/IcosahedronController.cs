using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Controls the physical rotation of the icosahedron (d20 die).
    /// Gravity is disabled and position is frozen so it rotates in-place.
    /// Provides controlled, natural rolling driven by player movement and debug controls,
    /// with critically damped tabletop settling when idle (no pendulum oscillations or swinging).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class IcosahedronController : MonoBehaviour
    {
        [Header("Physics Settings")]
        [SerializeField] private Rigidbody targetRigidbody;
        [Tooltip("Acceleration used to smoothly bring rotation up to the target rolling speed.")]
        [SerializeField] private float rollAcceleration = 16.0f;
        [SerializeField] private float angularDamping = 2.0f;
        [SerializeField] private float maxAngularVelocity = 6.0f;

        [Header("Tabletop Settling (Anti-Swinging)")]
        [Tooltip("Simulates resting on a flat table: smoothly settles the nearest face flat against the bottom when idle.")]
        [SerializeField] private bool enableTableRestingStability = true;
        [Tooltip("Restoring torque strength bringing the nearest face flat.")]
        [SerializeField] private float settleStiffness = 14.0f;
        [Tooltip("Damping torque to prevent oscillation. Critically damped when ~ 2 * sqrt(settleStiffness).")]
        [SerializeField] private float settleDamping = 7.5f;
        [Tooltip("Angle threshold (degrees) below which the die is considered flat.")]
        [SerializeField] private float flatAngleThreshold = 3.0f;

        [Header("Debug Rotation Controls")]
        [SerializeField] private bool enableKeyboardDebug = true;
        [SerializeField] private float debugRotationSpeed = 2.5f;

        public int CurrentBottomFaceNumber { get; private set; } = 1;
        public int CurrentTopFaceNumber { get; private set; } = 20;
        public float CurrentTiltAngle { get; private set; } = 0f;
        public bool IsRestingFlat { get; private set; } = true;

        private Vector3[] _canonicalFaceNormals;
        private int[] _canonicalFaceNumbers;

        // Player roll state
        private bool _isPlayerRolling = false;
        private Vector3 _playerRollAxis = Vector3.zero;
        private float _playerRollSpeed = 0f;

        // UI button state
        private readonly HashSet<Vector2> _activePointerDirections = new HashSet<Vector2>();

        private void Awake()
        {
            if (targetRigidbody == null)
            {
                targetRigidbody = GetComponent<Rigidbody>();
            }

            var faceData = IcosahedronGenerator.ComputeCanonicalFaceData();
            _canonicalFaceNormals = faceData.normals;
            _canonicalFaceNumbers = faceData.numbers;

            ConfigureRigidbody();
        }

        private void OnValidate()
        {
            if (targetRigidbody == null)
            {
                targetRigidbody = GetComponent<Rigidbody>();
            }

            if (targetRigidbody != null)
            {
                ConfigureRigidbody();
            }
        }

        private void ConfigureRigidbody()
        {
            if (targetRigidbody == null) return;

            targetRigidbody.angularDamping = angularDamping;
            targetRigidbody.maxAngularVelocity = maxAngularVelocity;

            // Anti-jitter: Interpolate transforms smoothly between physics frames
            targetRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            targetRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Stabilize solver contact resolution between moving bodies
            targetRigidbody.solverIterations = 16;
            targetRigidbody.solverVelocityIterations = 8;
            targetRigidbody.maxDepenetrationVelocity = 4.0f;
        }

        /// <summary>
        /// Commands the icosahedron to roll around a specified world axis at a specified angular speed,
        /// driven by player movement inside the die.
        /// </summary>
        public void DriveRollFromPlayer(Vector3 worldRollAxis, float targetAngularSpeed)
        {
            if (worldRollAxis.sqrMagnitude < 0.001f || targetAngularSpeed <= 0f)
            {
                StopRollFromPlayer();
                return;
            }

            _isPlayerRolling = true;
            _playerRollAxis = worldRollAxis.normalized;
            _playerRollSpeed = Mathf.Clamp(targetAngularSpeed, 0f, maxAngularVelocity);
        }

        /// <summary>
        /// Stops active player rolling drive, allowing tabletop settling to smoothly align the nearest face flat.
        /// </summary>
        public void StopRollFromPlayer()
        {
            _isPlayerRolling = false;
            _playerRollAxis = Vector3.zero;
            _playerRollSpeed = 0f;
        }

        public void SetDirectionHeld(Vector2 direction, bool isHeld)
        {
            if (isHeld)
            {
                _activePointerDirections.Add(direction);
            }
            else
            {
                _activePointerDirections.Remove(direction);
            }
        }

        private void Update()
        {
            UpdateCurrentFaces();
        }

        private void UpdateCurrentFaces()
        {
            if (_canonicalFaceNormals == null || _canonicalFaceNormals.Length == 0) return;

            float maxBottomDot = -1f;
            int bestBottomIndex = 0;
            float maxTopDot = -1f;
            int bestTopIndex = 0;

            for (int i = 0; i < _canonicalFaceNormals.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_canonicalFaceNormals[i]);
                float bottomDot = Vector3.Dot(worldNormal, Vector3.down);
                if (bottomDot > maxBottomDot)
                {
                    maxBottomDot = bottomDot;
                    bestBottomIndex = i;
                }

                float topDot = Vector3.Dot(worldNormal, Vector3.up);
                if (topDot > maxTopDot)
                {
                    maxTopDot = topDot;
                    bestTopIndex = i;
                }
            }

            if (_canonicalFaceNumbers != null && _canonicalFaceNumbers.Length > bestBottomIndex)
            {
                CurrentBottomFaceNumber = _canonicalFaceNumbers[bestBottomIndex];
                CurrentTopFaceNumber = _canonicalFaceNumbers[bestTopIndex];
            }

            CurrentTiltAngle = Mathf.Acos(Mathf.Clamp(maxBottomDot, -1f, 1f)) * Mathf.Rad2Deg;
            IsRestingFlat = CurrentTiltAngle < flatAngleThreshold && (targetRigidbody == null || targetRigidbody.angularVelocity.sqrMagnitude < 0.05f);
        }

        private void FixedUpdate()
        {
            if (targetRigidbody == null) return;

            targetRigidbody.angularDamping = angularDamping;
            targetRigidbody.maxAngularVelocity = maxAngularVelocity;

            // 1. Process Debug UI buttons / Keyboard arrows
            Vector2 debugInput = GetDebugDirectionInput();
            bool hasDebugInput = debugInput.sqrMagnitude > 0.01f;

            bool isActivelyRolling = false;
            Vector3 targetRollAxis = Vector3.zero;
            float targetRollSpeed = 0f;

            if (hasDebugInput)
            {
                // Up/Down: rotate around local X (roll forward/backward)
                // Left/Right: rotate around local Z (roll left/right)
                Vector3 localRollAxis = new Vector3(debugInput.y, 0f, -debugInput.x).normalized;
                targetRollAxis = transform.TransformDirection(localRollAxis);
                targetRollSpeed = debugRotationSpeed;
                isActivelyRolling = true;
            }
            else if (_isPlayerRolling && _playerRollSpeed > 0.01f)
            {
                targetRollAxis = _playerRollAxis;
                targetRollSpeed = _playerRollSpeed;
                isActivelyRolling = true;
            }

            if (isActivelyRolling)
            {
                Vector3 targetAngularVel = targetRollAxis * targetRollSpeed;
                Vector3 currentAngularVel = targetRigidbody.angularVelocity;
                Vector3 torque = (targetAngularVel - currentAngularVel) * rollAcceleration;
                targetRigidbody.AddTorque(torque, ForceMode.Acceleration);
            }
            else
            {
                // 3. Critically damped tabletop settling when idle (no swinging / no harmonic bouncing)
                if (enableTableRestingStability && _canonicalFaceNormals != null && _canonicalFaceNormals.Length > 0)
                {
                    ApplyTableRestingStability();
                }
            }
        }

        private Vector2 GetDebugDirectionInput()
        {
            Vector2 combined = Vector2.zero;
            foreach (Vector2 dir in _activePointerDirections)
            {
                combined += dir;
            }

            if (enableKeyboardDebug)
            {
                Keyboard kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.upArrowKey.isPressed) combined.y += 1f;
                    if (kb.downArrowKey.isPressed) combined.y -= 1f;
                    if (kb.leftArrowKey.isPressed) combined.x -= 1f;
                    if (kb.rightArrowKey.isPressed) combined.x += 1f;
                }
            }

            return Vector2.ClampMagnitude(combined, 1f);
        }

        private void ApplyTableRestingStability()
        {
            if (targetRigidbody == null) return;

            // Find canonical face normal whose world outward normal is closest to Vector3.down
            Vector3 bestNormal = Vector3.down;
            float maxDot = -1f;

            for (int i = 0; i < _canonicalFaceNormals.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_canonicalFaceNormals[i]);
                float dot = Vector3.Dot(worldNormal, Vector3.down);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    bestNormal = worldNormal;
                }
            }

            // The alignment error axis is the cross product between the closest face normal and Vector3.down
            Vector3 errorAxis = Vector3.Cross(bestNormal, Vector3.down);
            float errorMagnitude = errorAxis.magnitude;

            if (errorMagnitude < 0.0001f)
            {
                // Perfectly aligned with horizontal floor
                targetRigidbody.angularVelocity = Vector3.MoveTowards(targetRigidbody.angularVelocity, Vector3.zero, 15f * Time.fixedDeltaTime);
                return;
            }

            float errorAngleRad = Mathf.Asin(Mathf.Clamp01(errorMagnitude));
            Vector3 errorDir = errorAxis / errorMagnitude;

            // Critically damped settling: torque = k * theta - c * omega (c ~ 2*sqrt(k) prevents any pendulum swing)
            Vector3 settleTorque = errorDir * (errorAngleRad * settleStiffness) - targetRigidbody.angularVelocity * settleDamping;
            targetRigidbody.AddTorque(settleTorque, ForceMode.Acceleration);

            // Once the face is very close to flat and angular speed is low, rapidly bring rotation to a firm dead stop
            if (errorAngleRad < (flatAngleThreshold * Mathf.Deg2Rad) && targetRigidbody.angularVelocity.magnitude < 0.3f)
            {
                targetRigidbody.angularVelocity = Vector3.MoveTowards(targetRigidbody.angularVelocity, Vector3.zero, 25f * Time.fixedDeltaTime);
            }
        }
    }
}
