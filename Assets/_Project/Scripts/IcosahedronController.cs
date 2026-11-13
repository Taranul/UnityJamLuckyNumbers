using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Controls the smooth arcade physical rolling, settling, and parabolic toss mechanics of the icosahedron die.
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
        [SerializeField] private float _maxAngularVelocity = 25.0f;

        [Header("Rolling & World Movement")]
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
        [SerializeField] private float _flatAngleThreshold = 6.0f;

        [Header("Parabolic Toss / Launch")]
        [SerializeField] private bool _enableKeyToss = true;
        [SerializeField] private Key _tossKey = Key.Space;

        [Tooltip("Base upward speed of the parabolic launch (m/s).")]
        [SerializeField] private float _tossUpwardSpeed = 18.0f;

        [Tooltip("Base horizontal speed of the parabolic launch (m/s).")]
        [SerializeField] private float _tossForwardSpeed = 8.0f;

        [Tooltip("Base horizontal launch direction. If (0,0,0), uses current travel direction or transform forward.")]
        [SerializeField] private Vector3 _tossDirection = Vector3.forward;

        [Tooltip("If true, launches in the current rolling direction rather than static toss direction when in motion.")]
        [SerializeField] private bool _useCurrentTravelDirection = true;

        [Tooltip("Maximum random horizontal deviation angle (in degrees) from the toss direction.")]
        [Range(0f, 180f)]
        [SerializeField] private float _tossHorizontalDeviationAngle = 30.0f;

        [Tooltip("Random percentage variation in toss speed (+-). E.g. 0.15 = +-15%.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _tossSpeedRandomVariation = 0.15f;

        [Header("Random Powerful Torque")]
        [Tooltip("Minimum random angular velocity applied during toss (rad/s).")]
        [SerializeField] private float _minTossTorqueSpeed = 8.0f;

        [Tooltip("Maximum random angular velocity applied during toss (rad/s).")]
        [SerializeField] private float _maxTossTorqueSpeed = 18.0f;

        [Tooltip("Optional reference to interior player Rigidbody so both launch together smoothly.")]
        [SerializeField] private Rigidbody _playerRigidbody;

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
        public bool IsGroundedOnTable => _isGroundedOnTable;

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
        private bool _isGroundedOnTable = true;

        private void Awake()
        {
            if (_targetRigidbody == null)
            {
                _targetRigidbody = GetComponent<Rigidbody>();
            }

            var faceData = IcosahedronGenerator.ComputeCanonicalFaceData();
            _canonicalFaceNormals = faceData.normals;
            _canonicalFaceNumbers = faceData.numbers;

            float initialTargetY = _tableFloorY + _outerFaceRadius;
            _isGroundedOnTable = transform.position.y <= (initialTargetY + 0.15f);
            _wasRestingFlat = _isGroundedOnTable;
            IsRestingFlat = _isGroundedOnTable;

            if (_playerRigidbody == null)
            {
                PlayerController pc = FindFirstObjectByType<PlayerController>();
                if (pc != null)
                {
                    _playerRigidbody = pc.GetComponent<Rigidbody>();
                }
            }

            ConfigureRigidbody();

            if (_isGroundedOnTable && IsRestingFlat && _targetRigidbody != null)
            {
                _targetRigidbody.isKinematic = true;
            }
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

            if (_targetRigidbody.mass < 1000f)
            {
                _targetRigidbody.mass = 50000f;
            }

            _targetRigidbody.centerOfMass = Vector3.zero;
            _targetRigidbody.angularDamping = _angularDamping;
            _targetRigidbody.maxAngularVelocity = _maxAngularVelocity;
            _targetRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _targetRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _targetRigidbody.solverIterations = 16;
            _targetRigidbody.solverVelocityIterations = 8;
            _targetRigidbody.maxDepenetrationVelocity = 8.0f;
        }

        /// <summary>
        /// Shoots the die up into a parabolic trajectory with random deviation and powerful random torque.
        /// </summary>
        [ContextMenu("Toss Die")]
        public void TossDie()
        {
            TossDieInternal();
        }

        /// <summary>
        /// Shoots the die with customized launch parameters.
        /// </summary>
        public void TossDie(Vector3 customDirection, float upwardSpeed, float forwardSpeed)
        {
            TossDieInternal(customDirection, upwardSpeed, forwardSpeed);
        }

        private void TossDieInternal(Vector3? customDir = null, float? customUp = null, float? customForward = null)
        {
            if (_targetRigidbody == null) return;

            if (_targetRigidbody.isKinematic)
            {
                _targetRigidbody.isKinematic = false;
            }
            IsRestingFlat = false;
            _wasRestingFlat = false;
            _isGroundedOnTable = false;
            _isPlayerRolling = false;

            Vector3 baseDir;
            if (customDir.HasValue && customDir.Value.sqrMagnitude > 0.001f)
            {
                baseDir = customDir.Value.normalized;
            }
            else if (_useCurrentTravelDirection && CurrentTravelVelocity.sqrMagnitude > 0.1f)
            {
                baseDir = CurrentTravelVelocity.normalized;
            }
            else if (_tossDirection.sqrMagnitude > 0.001f)
            {
                baseDir = _tossDirection.normalized;
            }
            else
            {
                baseDir = transform.forward;
            }

            baseDir = Vector3.ProjectOnPlane(baseDir, Vector3.up).normalized;
            if (baseDir.sqrMagnitude < 0.001f) baseDir = Vector3.forward;

            float randomAngle = UnityEngine.Random.Range(-_tossHorizontalDeviationAngle, _tossHorizontalDeviationAngle);
            Vector3 launchHorizDir = Quaternion.AngleAxis(randomAngle, Vector3.up) * baseDir;

            float speedVariation = 1.0f + UnityEngine.Random.Range(-_tossSpeedRandomVariation, _tossSpeedRandomVariation);
            float upSpeed = (customUp ?? _tossUpwardSpeed) * speedVariation;
            float fwdSpeed = (customForward ?? _tossForwardSpeed) * speedVariation;

            Vector3 launchVelocity = (launchHorizDir * fwdSpeed) + (Vector3.up * upSpeed);
            _targetRigidbody.linearVelocity = launchVelocity;

            if (_playerRigidbody != null)
            {
                _playerRigidbody.linearVelocity = launchVelocity;
            }

            Vector3 randomTorqueAxis = UnityEngine.Random.onUnitSphere;
            float randomTorqueSpeed = UnityEngine.Random.Range(_minTossTorqueSpeed, _maxTossTorqueSpeed);
            Vector3 launchAngularVel = randomTorqueAxis * randomTorqueSpeed;

            _targetRigidbody.angularVelocity = launchAngularVel;
            _currentRollAngularVel = launchAngularVel;
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

        private void Update()
        {
            UpdateCurrentFaces();
            HandleTossInput();
        }

        private void HandleTossInput()
        {
            if (!_enableKeyToss) return;
            Keyboard kb = Keyboard.current;
            if (kb != null && kb[_tossKey].wasPressedThisFrame)
            {
                TossDie();
            }
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

            bool nowFlat = _isGroundedOnTable && !_isPlayerRolling && CurrentTiltAngle < _flatAngleThreshold && _currentRollAngularVel.sqrMagnitude < 0.05f;

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

                if (_targetRigidbody != null && !_targetRigidbody.isKinematic && !_isPlayerRolling)
                {
                    _targetRigidbody.linearVelocity = Vector3.zero;
                    _targetRigidbody.angularVelocity = Vector3.zero;
                    _targetRigidbody.isKinematic = true;
                }
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

            bool isActivelyRolling = _isPlayerRolling && _playerRollSpeed > 0.01f;
            Vector3 desiredRollAxis = _playerRollAxis;
            float desiredRollSpeed = _playerRollSpeed;

            if (isActivelyRolling)
            {
                if (_targetRigidbody.isKinematic)
                {
                    _targetRigidbody.isKinematic = false;
                }
                IsRestingFlat = false;

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
                float brakeSpeed = Mathf.Max(4f, _coastFriction * 1.5f);
                _currentRollAngularVel = Vector3.MoveTowards(_currentRollAngularVel, Vector3.zero, brakeSpeed * dt);

                if (_isGroundedOnTable && _enableTableRestingStability && !IsRestingFlat && _currentRollAngularVel.magnitude < 0.8f)
                {
                    ApplyTableRestingStability(dt);
                }
            }

            if (IsRestingFlat && !isActivelyRolling)
            {
                _currentRollAngularVel = Vector3.zero;
            }

            if (_enableWorldTravel)
            {
                Vector3 rollLinearVel = Vector3.Cross(_currentRollAngularVel, Vector3.up) * (_outerFaceRadius * _travelSpeedMultiplier);

                float targetY = _tableFloorY + _outerFaceRadius;
                float currentY = transform.position.y;
                bool isFallingOrGrounded = _targetRigidbody.linearVelocity.y <= 0.1f;
                _isGroundedOnTable = (currentY <= (targetY + 0.15f)) && isFallingOrGrounded;

                if (!_isGroundedOnTable)
                {
                    if (_targetRigidbody.isKinematic)
                    {
                        _targetRigidbody.isKinematic = false;
                    }
                    IsRestingFlat = false;
                }

                if (_targetRigidbody.isKinematic)
                {
                    CurrentTravelVelocity = Vector3.zero;
                }
                else
                {
                    float yVel;
                    if (!_isGroundedOnTable)
                    {
                        yVel = _targetRigidbody.linearVelocity.y;
                    }
                    else
                    {
                        float heightDiff = targetY - currentY;
                        float surfaceYVel = heightDiff * 14.0f;
                        yVel = Mathf.MoveTowards(_targetRigidbody.linearVelocity.y, surfaceYVel, 60.0f * dt);
                        if (currentY < targetY)
                        {
                            yVel = Mathf.Max(yVel, surfaceYVel);
                        }
                    }

                    if (!_isGroundedOnTable)
                    {
                        _currentRollAngularVel = _targetRigidbody.angularVelocity;
                        CurrentTravelVelocity = _targetRigidbody.linearVelocity;
                    }
                    else if (isActivelyRolling)
                    {
                        _targetRigidbody.linearVelocity = new Vector3(rollLinearVel.x, yVel, rollLinearVel.z);
                        _targetRigidbody.angularVelocity = _currentRollAngularVel;
                        CurrentTravelVelocity = _targetRigidbody.linearVelocity;
                    }
                    else
                    {
                        Vector3 horizVel = new Vector3(_targetRigidbody.linearVelocity.x, 0f, _targetRigidbody.linearVelocity.z);
                        Vector3 dampedHoriz;
                        if (IsRestingFlat || horizVel.sqrMagnitude < 0.04f)
                        {
                            dampedHoriz = Vector3.zero;
                        }
                        else
                        {
                            dampedHoriz = Vector3.MoveTowards(horizVel, Vector3.zero, 60f * dt);
                        }

                        _targetRigidbody.linearVelocity = new Vector3(dampedHoriz.x, yVel, dampedHoriz.z);
                        _targetRigidbody.angularVelocity = _currentRollAngularVel;
                        CurrentTravelVelocity = _targetRigidbody.linearVelocity;
                    }
                }
            }
        }

        private void ApplyTableRestingStability(float dt)
        {
            if (_canonicalFaceNormals == null || _canonicalFaceNormals.Length == 0) return;

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

            Quaternion alignRot = Quaternion.FromToRotation(bestNormal, Vector3.down);
            Quaternion targetRotation = alignRot * transform.rotation;

            float currentTilt = CurrentTiltAngle;
            float settleSpeed = Mathf.Clamp(_settleStiffness * 2.5f, 25f, 75f) + _settleDamping;

            if (currentTilt < _flatAngleThreshold)
            {
                Quaternion nextRot = Quaternion.RotateTowards(transform.rotation, targetRotation, (settleSpeed + 15f) * dt);
                if (_targetRigidbody != null && !_targetRigidbody.isKinematic) _targetRigidbody.MoveRotation(nextRot);
                else transform.rotation = nextRot;
                _currentRollAngularVel = Vector3.zero;

                if (currentTilt < 0.8f)
                {
                    if (_targetRigidbody != null && !_targetRigidbody.isKinematic) _targetRigidbody.MoveRotation(targetRotation);
                    else transform.rotation = targetRotation;
                    IsRestingFlat = true;
                    _wasRestingFlat = true;
                    OnFaceLanded?.Invoke(CurrentTopFaceNumber);

                    if (_targetRigidbody != null && !_targetRigidbody.isKinematic)
                    {
                        _targetRigidbody.linearVelocity = Vector3.zero;
                        _targetRigidbody.angularVelocity = Vector3.zero;
                        _targetRigidbody.isKinematic = true;
                    }
                }
            }
            else
            {
                Quaternion nextRot = Quaternion.RotateTowards(transform.rotation, targetRotation, settleSpeed * dt);
                if (_targetRigidbody != null && !_targetRigidbody.isKinematic) _targetRigidbody.MoveRotation(nextRot);
                else transform.rotation = nextRot;
                _currentRollAngularVel = Vector3.zero;
            }
        }
    }
}
