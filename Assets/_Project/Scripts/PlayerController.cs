using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using VContainer;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Rigidbody-based character controller for the player inside the icosahedron die.
    /// Manages decoupled locomotion, ground adhesion, soap-like corner deflection, and roll drive.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [FormerlySerializedAs("moveSpeed")]
        [SerializeField] private float _moveSpeed = 10.0f;

        [FormerlySerializedAs("acceleration")]
        [SerializeField] private float _acceleration = 25.0f;

        [FormerlySerializedAs("cameraTransform")]
        [SerializeField] private Transform _cameraTransform;

        [Header("Ground & Gravity Settings")]
        [FormerlySerializedAs("gravityMultiplier")]
        [SerializeField] private float _gravityMultiplier = 2.5f;

        [FormerlySerializedAs("groundStickStrength")]
        [SerializeField] private float _groundStickStrength = 25.0f;

        [FormerlySerializedAs("airControl")]
        [SerializeField] private float _airControl = 1.0f;

        [FormerlySerializedAs("minWalkableNormalY")]
        [SerializeField] private float _minWalkableNormalY = 0.60f;

        [FormerlySerializedAs("maxClimbHeight")]
        [SerializeField] private float _maxClimbHeight = -1.8f;

        [Header("Die Interaction & 3-Tier Drive")]
        [FormerlySerializedAs("minRollSlopeAngle")]
        [SerializeField] private float _minRollSlopeAngle = 12.0f;

        [FormerlySerializedAs("maxRollSlopeAngle")]
        [SerializeField] private float _maxRollSlopeAngle = 38.0f;

        [FormerlySerializedAs("heightTorqueMultiplier")]
        [SerializeField] private float _heightTorqueMultiplier = 0.45f;

        [FormerlySerializedAs("rollResponsiveness")]
        [SerializeField] private float _rollResponsiveness = 1.2f;

        [FormerlySerializedAs("wallPushBoost")]
        [SerializeField] private float _wallPushBoost = 0.8f;

        [FormerlySerializedAs("minImpactSpeedForKick")]
        [SerializeField] private float _minImpactSpeedForKick = 2.0f;

        [FormerlySerializedAs("wallImpactKickStrength")]
        [SerializeField] private float _wallImpactKickStrength = 1.4f;

        [FormerlySerializedAs("icosahedronRigidbody")]
        [SerializeField] private Rigidbody _icosahedronRigidbody;

        [Header("Input")]
        [FormerlySerializedAs("moveActionReference")]
        [SerializeField] private InputActionReference _moveActionReference;

        private Rigidbody _rigidbody;
        private CapsuleCollider _capsuleCollider;
        private IcosahedronController _icoController;
        private InputAction _fallbackMoveAction;
        private Vector2 _moveInput;
        private Vector3 _desiredDirection = Vector3.zero;
        private float _lastIcoContactTime = -100f;
        private float _lastGroundedTime = -100f;
        private Vector3 _floorNormal = Vector3.up;
        private bool _hasFloorContact;
        private readonly List<Vector3> _wallNormals = new List<Vector3>();
        private bool _isPushingWall;
        private float _wallPushDot;

        [Inject]
        public void Construct(IcosahedronController icosahedronController)
        {
            _icoController = icosahedronController;
            if (_icosahedronRigidbody == null && _icoController != null)
            {
                _icosahedronRigidbody = _icoController.TargetRigidbody ?? _icoController.GetComponent<Rigidbody>();
            }
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _capsuleCollider = GetComponent<CapsuleCollider>();
            ConfigurePhysics();

            if (_icoController == null)
            {
                if (_icosahedronRigidbody != null)
                {
                    _icoController = _icosahedronRigidbody.GetComponent<IcosahedronController>();
                }
                else
                {
                    _icoController = FindFirstObjectByType<IcosahedronController>();
                    if (_icoController != null)
                    {
                        _icosahedronRigidbody = _icoController.TargetRigidbody ?? _icoController.GetComponent<Rigidbody>();
                    }
                }
            }

            if (_cameraTransform == null && Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }

            if (_moveActionReference == null)
            {
                _fallbackMoveAction = new InputAction("Move", InputActionType.Value);
                _fallbackMoveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
            }
        }

        private void OnEnable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Enable();
            }

            _fallbackMoveAction?.Enable();
        }

        private void OnDisable()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveActionReference.action.Disable();
            }

            _fallbackMoveAction?.Disable();
        }

        private void OnDestroy()
        {
            _fallbackMoveAction?.Dispose();
        }

        private void ConfigurePhysics()
        {
            if (_rigidbody == null) return;

            _rigidbody.useGravity = true;
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.solverIterations = 16;
            _rigidbody.solverVelocityIterations = 8;
            _rigidbody.maxDepenetrationVelocity = 4.0f;

            if (_capsuleCollider != null)
            {
                _capsuleCollider.contactOffset = 0.005f;
                PhysicsMaterial playerMat = new PhysicsMaterial("PlayerSoapMat")
                {
                    dynamicFriction = 0.0f,
                    staticFriction = 0.0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounciness = 0.0f,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
                _capsuleCollider.material = playerMat;
            }
        }

        private void Update()
        {
            ReadInput();
        }

        private void ReadInput()
        {
            if (_moveActionReference != null && _moveActionReference.action != null)
            {
                _moveInput = _moveActionReference.action.ReadValue<Vector2>();
            }
            else if (_fallbackMoveAction != null)
            {
                _moveInput = _fallbackMoveAction.ReadValue<Vector2>();
            }
            else
            {
                Keyboard kb = Keyboard.current;
                Vector2 kbInput = Vector2.zero;
                if (kb != null)
                {
                    if (kb.wKey.isPressed) kbInput.y += 1f;
                    if (kb.sKey.isPressed) kbInput.y -= 1f;
                    if (kb.aKey.isPressed) kbInput.x -= 1f;
                    if (kb.dKey.isPressed) kbInput.x += 1f;
                }
                _moveInput = kbInput;
            }

            _moveInput = Vector2.ClampMagnitude(_moveInput, 1.0f);
            if (_moveInput.sqrMagnitude < 0.0025f)
            {
                _moveInput = Vector2.zero;
            }
        }

        private void FixedUpdate()
        {
            if (_rigidbody == null) return;

            Vector3 camForward = Vector3.forward;
            Vector3 camRight = Vector3.right;

            if (_cameraTransform != null)
            {
                camForward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
                camRight = Vector3.ProjectOnPlane(_cameraTransform.right, Vector3.up).normalized;
            }

            _desiredDirection = (camForward * _moveInput.y + camRight * _moveInput.x).normalized;

            bool isContacting = (Time.time - _lastIcoContactTime) < (Time.fixedDeltaTime * 2.5f);
            bool isGrounded = (Time.time - _lastGroundedTime) < (Time.fixedDeltaTime * 2.5f);
            bool isWalkableSlope = isGrounded && (_floorNormal.y >= _minWalkableNormalY);

            Vector3 currentVelocity = _rigidbody.linearVelocity;
            Vector3 currentHorizontalVel = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

            Vector3 icoCenter = (_icosahedronRigidbody != null) ? _icosahedronRigidbody.worldCenterOfMass : Vector3.zero;
            float relativeY = transform.position.y - icoCenter.y;

            if (_gravityMultiplier > 1.0f)
            {
                _rigidbody.AddForce(Physics.gravity * (_gravityMultiplier - 1.0f), ForceMode.Acceleration);
            }

            if (isContacting && isWalkableSlope)
            {
                _rigidbody.AddForce(-_floorNormal * _groundStickStrength, ForceMode.Acceleration);

                float liftOffSpeed = Vector3.Dot(_rigidbody.linearVelocity, _floorNormal);
                if (liftOffSpeed > 0.01f && _floorNormal.y >= 0.50f)
                {
                    Vector3 upwardLift = _floorNormal * liftOffSpeed;
                    if (upwardLift.y > 0.01f)
                    {
                        _rigidbody.linearVelocity -= upwardLift;
                    }
                }
            }

            Vector3 surfaceVel = (_icosahedronRigidbody != null)
                ? _icosahedronRigidbody.GetPointVelocity(transform.position)
                : Vector3.zero;

            Vector3 icoHorizVel = (_icosahedronRigidbody != null)
                ? new Vector3(_icosahedronRigidbody.linearVelocity.x, 0f, _icosahedronRigidbody.linearVelocity.z)
                : Vector3.zero;

            if (_moveInput.sqrMagnitude < 0.001f)
            {
                if (isContacting)
                {
                    Vector3 relativeVel = currentVelocity - surfaceVel;

                    if (isWalkableSlope)
                    {
                        Vector3 totalGravityAcc = Physics.gravity * _gravityMultiplier;
                        Vector3 downhillGravity = Vector3.ProjectOnPlane(totalGravityAcc, _floorNormal);
                        _rigidbody.AddForce(-downhillGravity, ForceMode.Acceleration);

                        float brakeRate = _acceleration * 3.5f;
                        Vector3 newRelativeVel = Vector3.MoveTowards(relativeVel, Vector3.zero, brakeRate * Time.fixedDeltaTime);

                        if (newRelativeVel.sqrMagnitude < 0.04f)
                        {
                            newRelativeVel = Vector3.zero;
                        }

                        _rigidbody.linearVelocity = surfaceVel + newRelativeVel;
                    }
                    else
                    {
                        Vector3 relHoriz = new Vector3(relativeVel.x, 0f, relativeVel.z);
                        Vector3 newRelHoriz = Vector3.MoveTowards(relHoriz, Vector3.zero, _acceleration * 3.0f * Time.fixedDeltaTime);
                        _rigidbody.linearVelocity = new Vector3(surfaceVel.x + newRelHoriz.x, currentVelocity.y, surfaceVel.z + newRelHoriz.z);
                    }
                }
                else
                {
                    Vector3 relAirHoriz = currentHorizontalVel - icoHorizVel;
                    Vector3 dampedAir = Vector3.MoveTowards(relAirHoriz, Vector3.zero, 15.0f * Time.fixedDeltaTime);
                    _rigidbody.linearVelocity = new Vector3(icoHorizVel.x + dampedAir.x, currentVelocity.y, icoHorizVel.z + dampedAir.z);
                }
            }
            else
            {
                Vector3 effectiveMoveDir = _desiredDirection;

                if (_isPushingWall && _wallNormals.Count > 0)
                {
                    if (_wallNormals.Count == 1)
                    {
                        Vector3 wallN = _wallNormals[0];
                        if (Vector3.Dot(effectiveMoveDir, -wallN) > 0.01f)
                        {
                            Vector3 deflected = Vector3.ProjectOnPlane(effectiveMoveDir, wallN);
                            if (deflected.sqrMagnitude > 0.001f)
                            {
                                effectiveMoveDir = deflected.normalized;
                            }
                        }
                    }
                    else
                    {
                        Vector3 n1 = _wallNormals[0];
                        Vector3 n2 = _wallNormals[1];
                        Vector3 seam = Vector3.Cross(n1, n2);
                        if (seam.sqrMagnitude > 0.001f)
                        {
                            seam.Normalize();
                            if (Vector3.Dot(seam, effectiveMoveDir) < 0f) seam = -seam;

                            if (Vector3.Dot(effectiveMoveDir, -n1) > 0f && Vector3.Dot(effectiveMoveDir, -n2) > 0f)
                            {
                                Vector3 seamMove = Vector3.Project(effectiveMoveDir, seam);
                                if (seamMove.sqrMagnitude > 0.001f)
                                {
                                    effectiveMoveDir = seamMove.normalized;
                                }
                            }
                            else
                            {
                                Vector3 wallToDeflect = (Vector3.Dot(effectiveMoveDir, -n1) > 0f) ? n1 : n2;
                                Vector3 deflected = Vector3.ProjectOnPlane(effectiveMoveDir, wallToDeflect);
                                if (deflected.sqrMagnitude > 0.001f)
                                {
                                    effectiveMoveDir = deflected.normalized;
                                }
                            }
                        }
                    }
                }

                if (isWalkableSlope)
                {
                    Vector3 moveOnSlope = Vector3.ProjectOnPlane(effectiveMoveDir, _floorNormal).normalized;
                    Vector3 targetHorizontalVel = icoHorizVel + moveOnSlope * (_moveInput.magnitude * _moveSpeed);
                    Vector3 horizDifference = targetHorizontalVel - currentHorizontalVel;
                    Vector3 moveForce = horizDifference * _acceleration;

                    _rigidbody.AddForce(new Vector3(moveForce.x, moveOnSlope.y * _acceleration * 0.5f, moveForce.z), ForceMode.Acceleration);
                }
                else
                {
                    Vector3 targetHorizontalVel = icoHorizVel + effectiveMoveDir * (_moveInput.magnitude * _moveSpeed);
                    Vector3 relCurrent = currentHorizontalVel - icoHorizVel;
                    Vector3 relTarget = effectiveMoveDir * (_moveInput.magnitude * _moveSpeed);

                    float airAccel = _acceleration * Mathf.Max(1.0f, _airControl);
                    if (Vector3.Dot(relCurrent, relTarget) < 0f)
                    {
                        airAccel *= 1.5f;
                    }

                    Vector3 newHorizVel = Vector3.MoveTowards(currentHorizontalVel, targetHorizontalVel, airAccel * Time.fixedDeltaTime);
                    _rigidbody.linearVelocity = new Vector3(newHorizVel.x, currentVelocity.y, newHorizVel.z);
                }

                if (effectiveMoveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(effectiveMoveDir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 14f * Time.fixedDeltaTime);
                }
            }

            float ceilingSoftZone = 0.5f;
            float softStart = _maxClimbHeight - ceilingSoftZone;
            if (relativeY > softStart)
            {
                float t = Mathf.Clamp01((relativeY - softStart) / ceilingSoftZone);
                Vector3 currentVel = _rigidbody.linearVelocity;
                if (currentVel.y > 0f)
                {
                    Vector3 vel = currentVel;
                    vel.y = Mathf.Lerp(vel.y, 0f, 12f * t * Time.fixedDeltaTime);
                    _rigidbody.linearVelocity = vel;

                    _rigidbody.AddForce(Vector3.down * (_acceleration * t * 1.5f), ForceMode.Acceleration);
                }
            }

            if (_icoController != null)
            {
                if (_moveInput.sqrMagnitude < 0.001f || _desiredDirection.sqrMagnitude < 0.001f)
                {
                    _icoController.StopRollFromPlayer();
                }
                else
                {
                    float currentSlopeAngle = Vector3.Angle(_floorNormal, Vector3.up);
                    float angleFactor = 0f;
                    if (isWalkableSlope && currentSlopeAngle > _minRollSlopeAngle)
                    {
                        angleFactor = Mathf.InverseLerp(_minRollSlopeAngle, _maxRollSlopeAngle, currentSlopeAngle);
                    }

                    float wallPushFactor = _isPushingWall ? (_wallPushDot * _wallPushBoost * _moveInput.magnitude) : 0f;
                    if (_isPushingWall && _moveInput.sqrMagnitude > 0.1f)
                    {
                        wallPushFactor = Mathf.Max(wallPushFactor, 0.6f * _moveInput.magnitude);
                    }

                    float baseDrive = Mathf.Max(angleFactor, wallPushFactor);
                    if (angleFactor > 0.05f && wallPushFactor > 0.05f)
                    {
                        baseDrive = angleFactor + wallPushFactor;
                    }

                    if (_moveInput.sqrMagnitude > 0.05f)
                    {
                        baseDrive = Mathf.Max(baseDrive, 0.50f * _moveInput.magnitude);
                    }

                    float lowestFloorY = _icoController.LowestInteriorFloorY;
                    float climbHeight = Mathf.Max(0f, transform.position.y - (lowestFloorY + 1.0f));
                    float heightMultiplier = 1.0f + (climbHeight * _heightTorqueMultiplier);

                    float totalEffort = baseDrive * heightMultiplier;

                    if (totalEffort > 0.02f && isContacting)
                    {
                        Vector3 horizDir = new Vector3(_desiredDirection.x, 0f, _desiredDirection.z).normalized;
                        Vector3 rollAxis = Vector3.Cross(Vector3.up, horizDir);

                        float floorRadius = 6.046f;
                        float baseAngularSpeed = (_moveSpeed / floorRadius) * _rollResponsiveness * totalEffort;
                        baseAngularSpeed = Mathf.Min(baseAngularSpeed, 4.0f);

                        _icoController.DriveRollFromPlayer(rollAxis, baseAngularSpeed, totalEffort, horizDir);
                    }
                    else
                    {
                        _icoController.StopRollFromPlayer();
                    }
                }
            }

            _isPushingWall = false;
            _wallPushDot = 0f;
        }

        private void OnCollisionEnter(Collision collision)
        {
            ProcessCollision(collision);
            CheckWallImpactKick(collision);
        }

        private void CheckWallImpactKick(Collision collision)
        {
            if (_icoController == null) return;
            if (_moveInput.sqrMagnitude < 0.05f || _desiredDirection.sqrMagnitude < 0.01f) return;

            Rigidbody otherRb = collision.rigidbody ?? collision.gameObject.GetComponentInParent<Rigidbody>();
            if (otherRb != _icosahedronRigidbody) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < _minImpactSpeedForKick) return;

            int count = collision.contactCount;
            for (int i = 0; i < count; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                if (contact.normal.y < 0.65f)
                {
                    Vector3 hitDirection = -contact.normal;
                    if (Vector3.Dot(_desiredDirection, hitDirection) > 0.25f)
                    {
                        Vector3 horizHit = new Vector3(hitDirection.x, 0f, hitDirection.z);
                        if (horizHit.sqrMagnitude > 0.01f)
                        {
                            Vector3 rollAxis = Vector3.Cross(Vector3.up, horizHit.normalized);
                            float kickImpulse = (impactSpeed - _minImpactSpeedForKick) * _wallImpactKickStrength;
                            _icoController.ApplyWallImpactKick(rollAxis, kickImpulse);
                            break;
                        }
                    }
                }
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            ProcessCollision(collision);
        }

        private void ProcessCollision(Collision collision)
        {
            Rigidbody otherRb = collision.rigidbody ?? collision.gameObject.GetComponentInParent<Rigidbody>();
            if (otherRb == null) return;

            if (_icosahedronRigidbody == null && otherRb.GetComponent<IcosahedronController>() != null)
            {
                _icosahedronRigidbody = otherRb;
                _icoController = otherRb.GetComponent<IcosahedronController>();
            }

            if (otherRb != _icosahedronRigidbody) return;

            _lastIcoContactTime = Time.time;
            _hasFloorContact = false;
            _wallNormals.Clear();

            int contactCount = collision.contactCount;
            float bestFloorY = -1f;
            Vector3 bestFloorNormal = Vector3.up;

            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);

                if (contact.normal.y >= 0.50f)
                {
                    _lastGroundedTime = Time.time;
                    if (contact.normal.y > bestFloorY)
                    {
                        bestFloorY = contact.normal.y;
                        bestFloorNormal = contact.normal;
                        _hasFloorContact = true;
                    }
                }

                if (contact.normal.y < 0.85f)
                {
                    if (_moveInput.sqrMagnitude < 0.01f)
                    {
                        float normalSpeed = Vector3.Dot(_rigidbody.linearVelocity, contact.normal);
                        if (normalSpeed < 0f)
                        {
                            _rigidbody.linearVelocity -= contact.normal * (normalSpeed * 0.85f);
                        }
                    }

                    bool isDuplicate = false;
                    for (int w = 0; w < _wallNormals.Count; w++)
                    {
                        if (Vector3.Dot(_wallNormals[w], contact.normal) > 0.92f)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }

                    if (!isDuplicate && _wallNormals.Count < 4)
                    {
                        _wallNormals.Add(contact.normal);
                    }

                    if (_moveInput.sqrMagnitude > 0.01f && _desiredDirection.sqrMagnitude > 0.01f)
                    {
                        float pushDot = Vector3.Dot(_desiredDirection, -contact.normal);
                        if (pushDot > 0.10f)
                        {
                            _isPushingWall = true;
                            _wallPushDot = Mathf.Clamp01(_wallPushDot + pushDot);
                        }
                    }
                }
            }

            if (_hasFloorContact)
            {
                _floorNormal = bestFloorNormal.normalized;
            }
        }
    }
}
