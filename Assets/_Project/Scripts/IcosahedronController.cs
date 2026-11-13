using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumbers.PhysicsPrototype
{
    public enum RollMode
    {
        SmoothCenterRoll,
        EdgePivotTumble
    }

    /// <summary>
    /// Controls the physical rotation and world travel of the icosahedron (d20 die).
    /// - Vector Momentum Model: Smooth arcade-style rolling momentum without physics collision jitter.
    /// - Vector Alignment & Deceleration: If opposing input is given, momentum smoothly brakes to 0
    ///   before reversing direction. Aligned input smoothly steers the roll axis.
    /// - 3-tier drive: surface angle leverage, climb height leverage, and wall collision/push boosts.
    /// - World travel: rolls across the floor/table with either SmoothCenterRoll or EdgePivotTumble.
    /// - Landing event: fires OnFaceLanded when the die cleanly lands flat.
    /// - Tabletop settling when idle (no pendulum oscillation).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class IcosahedronController : MonoBehaviour
    {
        [Header("Smooth Momentum & Vector Dynamics")]
        [SerializeField] private Rigidbody targetRigidbody;
        [Tooltip("Rate at which the die accelerates up to target roll speed (rad/s²).")]
        [SerializeField] private float rollAcceleration = 20.0f;
        [Tooltip("Braking deceleration when player pushes against current roll direction (rad/s²).")]
        [SerializeField] private float reverseBrakeDeceleration = 30.0f;
        [Tooltip("Steering turn rate when redirecting the roll direction (deg/sec).")]
        [SerializeField] private float steeringTurnRate = 200.0f;
        [Tooltip("Natural rolling friction / deceleration when no player torque is applied (rad/s²).")]
        [SerializeField] private float coastFriction = 3.5f;
        [Tooltip("Maximum angular roll speed (rad/s).")]
        [SerializeField] private float maxRollSpeed = 3.5f;
        [SerializeField] private float angularDamping = 1.5f;
        [SerializeField] private float maxAngularVelocity = 6.0f;

        [Header("Rolling & World Movement")]
        [SerializeField] private RollMode rollMode = RollMode.SmoothCenterRoll;
        [Tooltip("Allows the die to physically travel across the floor/table when rolling.")]
        [SerializeField] private bool enableWorldTravel = true;
        [Tooltip("Y position of the external table plane surface.")]
        [SerializeField] private float tableFloorY = -8.6f;
        [Tooltip("Outer radius of the icosahedron (from center to face center).")]
        [SerializeField] private float outerFaceRadius = 6.424f;
        [Tooltip("Linear travel speed multiplier relative to angular roll speed (v = omega * R).")]
        [SerializeField] private float travelSpeedMultiplier = 1.0f;

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

        public RollMode CurrentRollMode => rollMode;
        public float LowestInteriorFloorY => transform.position.y - (outerFaceRadius - 0.5f);
        public Vector3 CurrentTravelVelocity { get; private set; } = Vector3.zero;
        public Vector3 CurrentRollAngularVelocity => _currentRollAngularVel;
        public float CurrentRollSpeed => _currentRollAngularVel.magnitude;

        public int CurrentBottomFaceNumber { get; private set; } = 1;
        public int CurrentTopFaceNumber { get; private set; } = 20;
        public int TargetTopFaceNumber { get; private set; } = 20;
        public int TargetBottomFaceNumber { get; private set; } = 1;
        public float CurrentRollEffort { get; private set; } = 0f;
        public float CurrentTiltAngle { get; private set; } = 0f;
        public bool IsRestingFlat { get; private set; } = true;

        /// <summary>
        /// Fired when the die lands flat onto a new face (passed face is the face-up top number).
        /// </summary>
        public event System.Action<int> OnFaceLanded;

        private Vector3[] _canonicalFaceNormals;
        private int[] _canonicalFaceNumbers;

        // Vector momentum model
        private Vector3 _currentRollAngularVel = Vector3.zero;

        // Player roll state
        private bool _isPlayerRolling = false;
        private Vector3 _playerRollAxis = Vector3.zero;
        private float _playerRollSpeed = 0f;
        private bool _wasRestingFlat = true;

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
            IgnoreFloorColliderSnagging();
        }

        private void IgnoreFloorColliderSnagging()
        {
            GameObject planeObj = GameObject.Find("Plane");
            if (planeObj != null)
            {
                Collider[] planeCols = planeObj.GetComponentsInChildren<Collider>();
                Collider[] allCols = GetComponentsInChildren<Collider>();
                foreach (Collider c in allCols)
                {
                    if (c.gameObject.name == "ExteriorCollider")
                    {
                        foreach (Collider pc in planeCols)
                        {
                            Physics.IgnoreCollision(c, pc, true);
                        }
                    }
                }
            }
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
            targetRigidbody.maxDepenetrationVelocity = 8.0f;
        }

        /// <summary>
        /// Commands the icosahedron to roll around a specified world axis at a specified angular speed,
        /// driven by player movement and leverage inside the die.
        /// </summary>
        public void DriveRollFromPlayer(Vector3 worldRollAxis, float targetAngularSpeed, float effort, Vector3 movementDir)
        {
            if (worldRollAxis.sqrMagnitude < 0.001f || targetAngularSpeed <= 0f)
            {
                StopRollFromPlayer();
                return;
            }

            _isPlayerRolling = true;
            _playerRollAxis = worldRollAxis.normalized;
            _playerRollSpeed = Mathf.Clamp(targetAngularSpeed, 0f, maxRollSpeed);
            CurrentRollEffort = Mathf.Clamp01(effort);

            ComputeTargetAdjacentFace(movementDir);
        }

        /// <summary>
        /// Stops active player rolling drive, allowing tabletop settling to smoothly align the nearest face flat.
        /// </summary>
        public void StopRollFromPlayer()
        {
            _isPlayerRolling = false;
            _playerRollAxis = Vector3.zero;
            _playerRollSpeed = 0f;
            CurrentRollEffort = 0f;
            TargetTopFaceNumber = CurrentTopFaceNumber;
            TargetBottomFaceNumber = CurrentBottomFaceNumber;
        }

        public void ToggleRollMode()
        {
            rollMode = (rollMode == RollMode.SmoothCenterRoll) ? RollMode.EdgePivotTumble : RollMode.SmoothCenterRoll;
        }

        public void SetRollMode(RollMode mode)
        {
            rollMode = mode;
        }

        /// <summary>
        /// Applies an immediate angular impulse kick when the player collides with an interior wall.
        /// </summary>
        public void ApplyWallImpactKick(Vector3 rollAxis, float angularImpulse)
        {
            // Only apply impact kicks if the player is actively providing roll input!
            // Prevents accidental rotation from passive player bouncing or falling.
            if (!_isPlayerRolling) return;

            if (rollAxis.sqrMagnitude > 0.001f && angularImpulse > 0.01f)
            {
                _currentRollAngularVel += rollAxis.normalized * angularImpulse;
                _currentRollAngularVel = Vector3.ClampMagnitude(_currentRollAngularVel, maxRollSpeed * 1.5f);
            }
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
            
            bool nowFlat = CurrentTiltAngle < flatAngleThreshold && _currentRollAngularVel.sqrMagnitude < 0.05f;
            
            // Landing impact event: triggers when transitioning from rolling/tilting into resting flat
            if (!_wasRestingFlat && nowFlat)
            {
                OnFaceLanded?.Invoke(CurrentTopFaceNumber);
            }

            _wasRestingFlat = nowFlat;
            IsRestingFlat = nowFlat;

            if (IsRestingFlat)
            {
                TargetTopFaceNumber = CurrentTopFaceNumber;
                TargetBottomFaceNumber = CurrentBottomFaceNumber;
            }
        }

        /// <summary>
        /// Determines which of the 3 adjacent faces around the current floor the player is steering toward.
        /// </summary>
        private void ComputeTargetAdjacentFace(Vector3 worldMovementDir)
        {
            if (_canonicalFaceNormals == null || _canonicalFaceNormals.Length == 0) return;

            Vector3 localMoveDir = transform.InverseTransformDirection(worldMovementDir);
            localMoveDir = new Vector3(localMoveDir.x, 0f, localMoveDir.z).normalized;

            // In local canonical coordinates, the bottom face normal is canonical[0] = (0, -1, 0)
            // Adjacent faces to canonical[0] have dot product ~0.745 with Vector3.down
            float bestDot = -999f;
            int bestAdjNumber = CurrentBottomFaceNumber;

            for (int i = 0; i < _canonicalFaceNormals.Length; i++)
            {
                // Find adjacent faces (separated by 41.81 degrees from bottom face normal)
                Vector3 localNormal = _canonicalFaceNormals[i];
                float dotWithDown = Vector3.Dot(localNormal, Vector3.down);

                if (dotWithDown > 0.65f && dotWithDown < 0.85f)
                {
                    // Project local normal on horizontal plane to see its direction
                    Vector3 faceDir = new Vector3(localNormal.x, 0f, localNormal.z).normalized;
                    float alignDot = Vector3.Dot(faceDir, localMoveDir);
                    if (alignDot > bestDot)
                    {
                        bestDot = alignDot;
                        bestAdjNumber = _canonicalFaceNumbers[i];
                    }
                }
            }

            TargetBottomFaceNumber = bestAdjNumber;
            TargetTopFaceNumber = 21 - bestAdjNumber; // Standard DnD opposite faces sum to 21
        }

        private void FixedUpdate()
        {
            if (targetRigidbody == null) return;

            float dt = Time.fixedDeltaTime;

            // 1. Process Debug UI buttons / Keyboard arrows
            Vector2 debugInput = GetDebugDirectionInput();
            bool hasDebugInput = debugInput.sqrMagnitude > 0.01f;

            bool isActivelyRolling = false;
            Vector3 desiredRollAxis = Vector3.zero;
            float desiredRollSpeed = 0f;

            if (hasDebugInput)
            {
                // Up/Down: rotate around local X (roll forward/backward)
                // Left/Right: rotate around local Z (roll left/right)
                Vector3 localRollAxis = new Vector3(debugInput.y, 0f, -debugInput.x).normalized;
                desiredRollAxis = transform.TransformDirection(localRollAxis);
                desiredRollSpeed = debugRotationSpeed;
                isActivelyRolling = true;
            }
            else if (_isPlayerRolling && _playerRollSpeed > 0.01f)
            {
                desiredRollAxis = _playerRollAxis;
                desiredRollSpeed = _playerRollSpeed;
                isActivelyRolling = true;
            }

            // 2. Smooth Vector Momentum & Responsive Turnaround
            if (isActivelyRolling)
            {
                Vector3 targetRollVel = desiredRollAxis * desiredRollSpeed;
                float dot = (_currentRollAngularVel.sqrMagnitude > 0.001f)
                    ? Vector3.Dot(_currentRollAngularVel.normalized, desiredRollAxis)
                    : 1f;

                if (dot >= 0.05f && _currentRollAngularVel.sqrMagnitude > 0.01f)
                {
                    Vector3 currentAxis = _currentRollAngularVel.normalized;
                    Vector3 steeredAxis = Vector3.RotateTowards(currentAxis, desiredRollAxis, steeringTurnRate * Mathf.Deg2Rad * dt, 0f).normalized;
                    targetRollVel = steeredAxis * desiredRollSpeed;
                }

                // Responsive turnaround: accelerate toward desired direction.
                // If reversing, accelerate quickly using reverseBrakeDeceleration to eliminate turnaround delay.
                float currentAccel = (dot < -0.05f) ? reverseBrakeDeceleration : rollAcceleration;
                _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, targetRollVel, currentAccel * dt);
                _currentRollAngularVel = Vector3.ClampMagnitude(_currentRollAngularVel, maxRollSpeed);
            }
            else
            {
                // 3. Natural Rolling Friction (Coasting Deceleration)
                float currentSpeed = _currentRollAngularVel.magnitude;
                if (currentSpeed > 0.01f)
                {
                    Vector3 currentAxis = _currentRollAngularVel / currentSpeed;
                    float coastedSpeed = Mathf.MoveTowards(currentSpeed, 0f, coastFriction * dt);
                    _currentRollAngularVel = currentAxis * coastedSpeed;
                }
                else
                {
                    _currentRollAngularVel = Vector3.zero;
                }

                // 4. Critically damped tabletop settling when coasted down to low speed
                if (enableTableRestingStability && _canonicalFaceNormals != null && _canonicalFaceNormals.Length > 0 && currentSpeed < 0.8f)
                {
                    ApplyTableRestingStability(dt);
                }
            }

            // Apply computed angular velocity directly to Rigidbody
            targetRigidbody.angularVelocity = _currentRollAngularVel;

            // 5. World Travel across Floor/Table Plane
            if (enableWorldTravel)
            {
                // Forward linear travel matching rolling without slipping: v = omega x Vector3.up
                Vector3 rollLinearVel = Vector3.Cross(_currentRollAngularVel, Vector3.up) * (outerFaceRadius * travelSpeedMultiplier);
                float dynamicRadius = ComputeLowestPointOffset();

                float targetY = (rollMode == RollMode.SmoothCenterRoll)
                    ? tableFloorY + Mathf.Lerp(outerFaceRadius, dynamicRadius, 0.25f)
                    : tableFloorY + dynamicRadius;

                float yVel = (targetY - transform.position.y) * 12.0f;

                if (isActivelyRolling || _currentRollAngularVel.sqrMagnitude > 0.01f)
                {
                    targetRigidbody.linearVelocity = new Vector3(rollLinearVel.x, yVel, rollLinearVel.z);
                }
                else
                {
                    Vector3 horizVel = new Vector3(targetRigidbody.linearVelocity.x, 0f, targetRigidbody.linearVelocity.z);
                    Vector3 dampedHoriz = Vector3.MoveTowards(horizVel, Vector3.zero, 15f * dt);
                    targetRigidbody.linearVelocity = new Vector3(dampedHoriz.x, yVel, dampedHoriz.z);
                }

                CurrentTravelVelocity = targetRigidbody.linearVelocity;
            }
        }

        private float ComputeLowestPointOffset()
        {
            float tiltRad = CurrentTiltAngle * Mathf.Deg2Rad;
            float cosTilt = Mathf.Max(0.72f, Mathf.Cos(tiltRad));
            return outerFaceRadius / cosTilt;
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

        private void ApplyTableRestingStability(float dt)
        {
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

            // Alignment error axis is the cross product between the closest face normal and Vector3.down
            Vector3 errorAxis = Vector3.Cross(bestNormal, Vector3.down);
            float errorMagnitude = errorAxis.magnitude;

            if (errorMagnitude < 0.0001f)
            {
                // Perfectly aligned with horizontal floor
                _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, Vector3.zero, 15f * dt);
                return;
            }

            float errorAngleRad = Mathf.Asin(Mathf.Clamp01(errorMagnitude));
            Vector3 errorDir = errorAxis / errorMagnitude;

            // Over-the-ridge tipping assistance
            float stiffness = settleStiffness;
            if (CurrentTiltAngle > 20.9f)
            {
                stiffness *= 1.3f;
            }

            // Target settling angular velocity bringing face to flat
            Vector3 targetSettleVel = errorDir * (errorAngleRad * stiffness);
            targetSettleVel = Vector3.ClampMagnitude(targetSettleVel, 2.0f);

            _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, targetSettleVel, settleDamping * dt);

            // Dead stop when very close to flat and angular speed is low
            if (errorAngleRad < (flatAngleThreshold * Mathf.Deg2Rad) && _currentRollAngularVel.magnitude < 0.35f)
            {
                _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, Vector3.zero, 20f * dt);
            }
        }
    }
}
