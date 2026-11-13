using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace LuckyNumbers.PhysicsPrototype
{
    public enum RollMode
    {
        SmoothCenterRoll,
        EdgePivotTumble
    }

    /// <summary>
    /// Controls the physical rotation, vector momentum, settling, and world travel of the icosahedron die.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class IcosahedronController : MonoBehaviour
    {
        [Header("Smooth Momentum & Vector Dynamics")]
        [FormerlySerializedAs("targetRigidbody")]
        [SerializeField] private Rigidbody _targetRigidbody;

        [FormerlySerializedAs("rollAcceleration")]
        [SerializeField] private float _rollAcceleration = 20.0f;

        [FormerlySerializedAs("reverseBrakeDeceleration")]
        [SerializeField] private float _reverseBrakeDeceleration = 30.0f;

        [FormerlySerializedAs("steeringTurnRate")]
        [SerializeField] private float _steeringTurnRate = 200.0f;

        [FormerlySerializedAs("coastFriction")]
        [SerializeField] private float _coastFriction = 3.5f;

        [FormerlySerializedAs("maxRollSpeed")]
        [SerializeField] private float _maxRollSpeed = 3.5f;

        [FormerlySerializedAs("angularDamping")]
        [SerializeField] private float _angularDamping = 1.5f;

        [FormerlySerializedAs("maxAngularVelocity")]
        [SerializeField] private float _maxAngularVelocity = 6.0f;

        [Header("Rolling & World Movement")]
        [FormerlySerializedAs("rollMode")]
        [SerializeField] private RollMode _rollMode = RollMode.SmoothCenterRoll;

        [FormerlySerializedAs("enableWorldTravel")]
        [SerializeField] private bool _enableWorldTravel = true;

        [FormerlySerializedAs("tableFloorY")]
        [SerializeField] private float _tableFloorY = -8.6f;

        [FormerlySerializedAs("outerFaceRadius")]
        [SerializeField] private float _outerFaceRadius = 6.424f;

        [FormerlySerializedAs("travelSpeedMultiplier")]
        [SerializeField] private float _travelSpeedMultiplier = 1.0f;

        [Header("Tabletop Settling")]
        [FormerlySerializedAs("enableTableRestingStability")]
        [SerializeField] private bool _enableTableRestingStability = true;

        [FormerlySerializedAs("settleStiffness")]
        [SerializeField] private float _settleStiffness = 14.0f;

        [FormerlySerializedAs("settleDamping")]
        [SerializeField] private float _settleDamping = 7.5f;

        [FormerlySerializedAs("flatAngleThreshold")]
        [SerializeField] private float _flatAngleThreshold = 3.0f;

        [Header("Debug Rotation Controls")]
        [FormerlySerializedAs("enableKeyboardDebug")]
        [SerializeField] private bool _enableKeyboardDebug = true;

        [FormerlySerializedAs("debugRotationSpeed")]
        [SerializeField] private float _debugRotationSpeed = 2.5f;

        public RollMode CurrentRollMode => _rollMode;
        public Rigidbody TargetRigidbody => _targetRigidbody;
        public float LowestInteriorFloorY => transform.position.y - (_outerFaceRadius - 0.5f);
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
        /// Fired when the die lands flat onto a new face.
        /// </summary>
        public event Action<int> OnFaceLanded;

        private Vector3[] _canonicalFaceNormals;
        private int[] _canonicalFaceNumbers;
        private Vector3 _currentRollAngularVel = Vector3.zero;
        private bool _isPlayerRolling;
        private Vector3 _playerRollAxis = Vector3.zero;
        private float _playerRollSpeed;
        private bool _wasRestingFlat = true;
        private readonly HashSet<Vector2> _activePointerDirections = new HashSet<Vector2>();

        private void Awake()
        {
            if (_targetRigidbody == null)
            {
                _targetRigidbody = GetComponent<Rigidbody>();
            }

            var faceData = IcosahedronGenerator.ComputeCanonicalFaceData();
            _canonicalFaceNormals = faceData.normals;
            _canonicalFaceNumbers = faceData.numbers;

            ConfigureRigidbody();
        }

        private void OnValidate()
        {
            if (_targetRigidbody == null)
            {
                _targetRigidbody = GetComponent<Rigidbody>();
            }

            if (_targetRigidbody != null)
            {
                ConfigureRigidbody();
            }
        }

        private void ConfigureRigidbody()
        {
            if (_targetRigidbody == null) return;

            _targetRigidbody.angularDamping = _angularDamping;
            _targetRigidbody.maxAngularVelocity = _maxAngularVelocity;
            _targetRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _targetRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _targetRigidbody.solverIterations = 16;
            _targetRigidbody.solverVelocityIterations = 8;
            _targetRigidbody.maxDepenetrationVelocity = 8.0f;
        }

        /// <summary>
        /// Drives die rotation around a world axis driven by player movement and climb effort.
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
            _playerRollSpeed = Mathf.Clamp(targetAngularSpeed, 0f, _maxRollSpeed);
            CurrentRollEffort = Mathf.Clamp01(effort);

            ComputeTargetAdjacentFace(movementDir);
        }

        /// <summary>
        /// Stops active player rolling drive, allowing tabletop settling to align the nearest face flat.
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
            _rollMode = (_rollMode == RollMode.SmoothCenterRoll) ? RollMode.EdgePivotTumble : RollMode.SmoothCenterRoll;
        }

        public void SetRollMode(RollMode mode)
        {
            _rollMode = mode;
        }

        /// <summary>
        /// Applies an angular impulse kick when the player impacts an interior wall.
        /// </summary>
        public void ApplyWallImpactKick(Vector3 rollAxis, float angularImpulse)
        {
            if (!_isPlayerRolling) return;

            if (rollAxis.sqrMagnitude > 0.001f && angularImpulse > 0.01f)
            {
                _currentRollAngularVel += rollAxis.normalized * angularImpulse;
                _currentRollAngularVel = Vector3.ClampMagnitude(_currentRollAngularVel, _maxRollSpeed * 1.5f);
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

            bool nowFlat = CurrentTiltAngle < _flatAngleThreshold && _currentRollAngularVel.sqrMagnitude < 0.05f;

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

        private void ComputeTargetAdjacentFace(Vector3 worldMovementDir)
        {
            if (_canonicalFaceNormals == null || _canonicalFaceNormals.Length == 0) return;

            Vector3 localMoveDir = transform.InverseTransformDirection(worldMovementDir);
            localMoveDir = new Vector3(localMoveDir.x, 0f, localMoveDir.z).normalized;

            float bestDot = -999f;
            int bestAdjNumber = CurrentBottomFaceNumber;

            for (int i = 0; i < _canonicalFaceNormals.Length; i++)
            {
                Vector3 localNormal = _canonicalFaceNormals[i];
                float dotWithDown = Vector3.Dot(localNormal, Vector3.down);

                if (dotWithDown > 0.65f && dotWithDown < 0.85f)
                {
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
            TargetTopFaceNumber = 21 - bestAdjNumber;
        }

        private void FixedUpdate()
        {
            if (_targetRigidbody == null) return;

            float dt = Time.fixedDeltaTime;

            Vector2 debugInput = GetDebugDirectionInput();
            bool hasDebugInput = debugInput.sqrMagnitude > 0.01f;

            bool isActivelyRolling = false;
            Vector3 desiredRollAxis = Vector3.zero;
            float desiredRollSpeed = 0f;

            if (hasDebugInput)
            {
                Vector3 localRollAxis = new Vector3(debugInput.y, 0f, -debugInput.x).normalized;
                desiredRollAxis = transform.TransformDirection(localRollAxis);
                desiredRollSpeed = _debugRotationSpeed;
                isActivelyRolling = true;
            }
            else if (_isPlayerRolling && _playerRollSpeed > 0.01f)
            {
                desiredRollAxis = _playerRollAxis;
                desiredRollSpeed = _playerRollSpeed;
                isActivelyRolling = true;
            }

            if (isActivelyRolling)
            {
                Vector3 targetRollVel = desiredRollAxis * desiredRollSpeed;
                float dot = (_currentRollAngularVel.sqrMagnitude > 0.001f)
                    ? Vector3.Dot(_currentRollAngularVel.normalized, desiredRollAxis)
                    : 1f;

                if (dot >= 0.05f && _currentRollAngularVel.sqrMagnitude > 0.01f)
                {
                    Vector3 currentAxis = _currentRollAngularVel.normalized;
                    Vector3 steeredAxis = Vector3.RotateTowards(currentAxis, desiredRollAxis, _steeringTurnRate * Mathf.Deg2Rad * dt, 0f).normalized;
                    targetRollVel = steeredAxis * desiredRollSpeed;
                }

                float currentAccel = (dot < -0.05f) ? _reverseBrakeDeceleration : _rollAcceleration;
                _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, targetRollVel, currentAccel * dt);
                _currentRollAngularVel = Vector3.ClampMagnitude(_currentRollAngularVel, _maxRollSpeed);
            }
            else
            {
                float currentSpeed = _currentRollAngularVel.magnitude;
                if (currentSpeed > 0.01f)
                {
                    Vector3 currentAxis = _currentRollAngularVel / currentSpeed;
                    float coastedSpeed = Mathf.MoveTowards(currentSpeed, 0f, _coastFriction * dt);
                    _currentRollAngularVel = currentAxis * coastedSpeed;
                }
                else
                {
                    _currentRollAngularVel = Vector3.zero;
                }

                if (_enableTableRestingStability && _canonicalFaceNormals != null && _canonicalFaceNormals.Length > 0 && currentSpeed < 0.8f)
                {
                    ApplyTableRestingStability(dt);
                }
            }

            _targetRigidbody.angularVelocity = _currentRollAngularVel;

            if (_enableWorldTravel)
            {
                Vector3 rollLinearVel = Vector3.Cross(_currentRollAngularVel, Vector3.up) * (_outerFaceRadius * _travelSpeedMultiplier);
                float dynamicRadius = ComputeLowestPointOffset();

                float targetY = (_rollMode == RollMode.SmoothCenterRoll)
                    ? _tableFloorY + Mathf.Lerp(_outerFaceRadius, dynamicRadius, 0.25f)
                    : _tableFloorY + dynamicRadius;

                float yVel = (targetY - transform.position.y) * 12.0f;

                if (isActivelyRolling || _currentRollAngularVel.sqrMagnitude > 0.01f)
                {
                    _targetRigidbody.linearVelocity = new Vector3(rollLinearVel.x, yVel, rollLinearVel.z);
                }
                else
                {
                    Vector3 horizVel = new Vector3(_targetRigidbody.linearVelocity.x, 0f, _targetRigidbody.linearVelocity.z);
                    Vector3 dampedHoriz = Vector3.MoveTowards(horizVel, Vector3.zero, 15f * dt);
                    _targetRigidbody.linearVelocity = new Vector3(dampedHoriz.x, yVel, dampedHoriz.z);
                }

                CurrentTravelVelocity = _targetRigidbody.linearVelocity;
            }
        }

        private float ComputeLowestPointOffset()
        {
            float tiltRad = CurrentTiltAngle * Mathf.Deg2Rad;
            float cosTilt = Mathf.Max(0.72f, Mathf.Cos(tiltRad));
            return _outerFaceRadius / cosTilt;
        }

        private Vector2 GetDebugDirectionInput()
        {
            Vector2 combined = Vector2.zero;
            foreach (Vector2 dir in _activePointerDirections)
            {
                combined += dir;
            }

            if (_enableKeyboardDebug)
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

            Vector3 errorAxis = Vector3.Cross(bestNormal, Vector3.down);
            float errorMagnitude = errorAxis.magnitude;

            if (errorMagnitude < 0.0001f)
            {
                _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, Vector3.zero, 15f * dt);
                return;
            }

            float errorAngleRad = Mathf.Asin(Mathf.Clamp01(errorMagnitude));
            Vector3 errorDir = errorAxis / errorMagnitude;

            float stiffness = _settleStiffness;
            if (CurrentTiltAngle > 20.9f)
            {
                stiffness *= 1.3f;
            }

            Vector3 targetSettleVel = errorDir * (errorAngleRad * stiffness);
            targetSettleVel = Vector3.ClampMagnitude(targetSettleVel, 2.0f);

            _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, targetSettleVel, _settleDamping * dt);

            if (errorAngleRad < (_flatAngleThreshold * Mathf.Deg2Rad) && _currentRollAngularVel.magnitude < 0.35f)
            {
                _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, Vector3.zero, 20f * dt);
            }
        }
    }
}
