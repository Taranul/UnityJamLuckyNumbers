using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Rigidbody-based character controller for the player inside the icosahedron (d20 die).
    /// - Natural decoupled gravity: horizontal WASD movement never fights or cancels downward gravity.
    /// - Ground-sticking downforce prevents floating off slopes without artificial weight spikes.
    /// - Angle-based die rolling: flat floor is a safe zone (0% torque); climbing slopes builds leverage;
    ///   pushing walls while climbing delivers maximum rolling power.
    /// - Smooth anti-jitter cushion at height boundaries.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 10.0f;
        [SerializeField] private float acceleration = 25.0f;
        [SerializeField] private Transform cameraTransform;

        [Header("Ground & Gravity Settings")]
        [Tooltip("Enhanced downward gravity multiplier for responsive, snappy character weight (default 2.5x).")]
        [SerializeField] private float gravityMultiplier = 2.5f;

        [Tooltip("Downward adhesion force keeping feet firmly planted on slopes and floor (default 25 m/s²).")]
        [SerializeField] private float groundStickStrength = 25.0f;

        [Tooltip("Reduced horizontal acceleration multiplier while airborne (prevents floating in midair).")]
        [SerializeField] private float airControl = 0.2f;

        [Tooltip("Minimum surface normal Y component required to walk up a surface. Normal Y of side faces is ~0.75 (41.8 deg slope). Surfaces steeper than ~53 deg cannot be climbed.")]
        [SerializeField] private float minWalkableNormalY = 0.60f;

        [Tooltip("Maximum allowed height of player center relative to icosahedron center. Top of side faces is at -2.62m.")]
        [SerializeField] private float maxClimbHeight = -1.8f;

        [Header("Die Interaction & 3-Tier Drive")]
        [Tooltip("Below this slope angle (degrees), the player is in the flat floor safe zone and exerts zero rotation on the die.")]
        [SerializeField] private float minRollSlopeAngle = 12.0f;

        [Tooltip("At or above this slope angle (degrees), the player achieves 100% climbing angle leverage.")]
        [SerializeField] private float maxRollSlopeAngle = 38.0f;

        [Tooltip("How much relative climb height from the lowest floor point increases rotational torque.")]
        [SerializeField] private float heightTorqueMultiplier = 0.45f;

        [Tooltip("Responsiveness multiplier for rolling the die with player movement.")]
        [SerializeField] private float rollResponsiveness = 1.2f;

        [Tooltip("Additional roll torque multiplier when actively running into an inclined side wall.")]
        [SerializeField] private float wallPushBoost = 0.8f;

        [Tooltip("Minimum impact velocity (m/s) to trigger a wall collision impact kick.")]
        [SerializeField] private float minImpactSpeedForKick = 2.0f;

        [Tooltip("Torque impulse strength applied to the die when slamming into an interior wall.")]
        [SerializeField] private float wallImpactKickStrength = 1.4f;

        [Tooltip("Reference to the icosahedron Rigidbody. If unassigned, automatically located at runtime.")]
        [SerializeField] private Rigidbody icosahedronRigidbody;

        [Header("Input")]
        [SerializeField] private InputActionReference moveActionReference;

        private Rigidbody _rigidbody;
        private CapsuleCollider _capsuleCollider;
        private IcosahedronController _icoController;
        private InputAction _fallbackMoveAction;
        private Vector2 _moveInput;
        private Vector3 _desiredDirection = Vector3.zero;
        private float _lastIcoContactTime = -100f;
        private float _lastGroundedTime = -100f;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _floorNormal = Vector3.up;
        private bool _hasFloorContact = false;
        private readonly System.Collections.Generic.List<Vector3> _wallNormals = new System.Collections.Generic.List<Vector3>();
        private bool _isPushingWall = false;
        private float _wallPushDot = 0f;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _capsuleCollider = GetComponent<CapsuleCollider>();
            ConfigurePhysics();

            if (icosahedronRigidbody == null)
            {
                _icoController = FindFirstObjectByType<IcosahedronController>();
                if (_icoController != null)
                {
                    icosahedronRigidbody = _icoController.GetComponent<Rigidbody>();
                }
            }
            else
            {
                _icoController = icosahedronRigidbody.GetComponent<IcosahedronController>();
            }

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (moveActionReference == null)
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
            if (moveActionReference != null && moveActionReference.action != null)
            {
                moveActionReference.action.Enable();
            }

            _fallbackMoveAction?.Enable();
        }

        private void OnDisable()
        {
            if (moveActionReference != null && moveActionReference.action != null)
            {
                moveActionReference.action.Disable();
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

            // Stabilize solver contact resolution and prevent depenetration popping
            _rigidbody.solverIterations = 16;
            _rigidbody.solverVelocityIterations = 8;
            _rigidbody.maxDepenetrationVelocity = 8.0f;

            // Frictionless "soap" physics material to glide smoothly in corners and slopes
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
            if (moveActionReference != null && moveActionReference.action != null)
            {
                _moveInput = moveActionReference.action.ReadValue<Vector2>();
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
            if (_moveInput.sqrMagnitude < 0.0025f) // Small deadzone (0.05 magnitude) to prevent stick drift
            {
                _moveInput = Vector2.zero;
            }
        }

        private void FixedUpdate()
        {
            if (_rigidbody == null) return;

            Vector3 camForward = Vector3.forward;
            Vector3 camRight = Vector3.right;

            if (cameraTransform != null)
            {
                camForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                camRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            }

            _desiredDirection = (camForward * _moveInput.y + camRight * _moveInput.x).normalized;

            bool isContacting = (Time.time - _lastIcoContactTime) < (Time.fixedDeltaTime * 2.5f);
            bool isGrounded = (Time.time - _lastGroundedTime) < (Time.fixedDeltaTime * 2.5f);
            bool isWalkableSlope = isGrounded && (_floorNormal.y >= minWalkableNormalY);

            Vector3 currentVelocity = _rigidbody.linearVelocity;
            Vector3 currentHorizontalVel = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

            Vector3 icoCenter = (icosahedronRigidbody != null) ? icosahedronRigidbody.worldCenterOfMass : Vector3.zero;
            float relativeY = transform.position.y - icoCenter.y;

            // --- 1. Enhanced Snappy Character Gravity ---
            if (gravityMultiplier > 1.0f)
            {
                _rigidbody.AddForce(Physics.gravity * (gravityMultiplier - 1.0f), ForceMode.Acceleration);
            }

            // --- 2. Ground Snapping & Anti-Launch Adhesion ---
            if (isContacting && isWalkableSlope)
            {
                // Firm downward adhesion strictly along the floor support normal (-_floorNormal).
                // Perpendicular to the surface: holds feet firmly planted with zero downhill/horizontal component!
                _rigidbody.AddForce(-_floorNormal * groundStickStrength, ForceMode.Acceleration);

                // Anti-launch: cancel ONLY upward lift away from the floor.
                // NEVER strips horizontal escape velocity when moving out of a corner!
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

            // --- 3. Decoupled Locomotion & Authoritative Braking ---
            Vector3 surfaceVel = (icosahedronRigidbody != null) 
                ? icosahedronRigidbody.GetPointVelocity(transform.position) 
                : Vector3.zero;

            Vector3 icoHorizVel = (icosahedronRigidbody != null) 
                ? new Vector3(icosahedronRigidbody.linearVelocity.x, 0f, icosahedronRigidbody.linearVelocity.z) 
                : Vector3.zero;

            if (_moveInput.sqrMagnitude < 0.001f)
            {
                if (isContacting)
                {
                    Vector3 relativeVel = currentVelocity - surfaceVel;

                    if (isWalkableSlope)
                    {
                        // Counteract downhill gravity component to hold position completely stationary on slopes when idle
                        Vector3 totalGravityAcc = Physics.gravity * gravityMultiplier;
                        Vector3 downhillGravity = Vector3.ProjectOnPlane(totalGravityAcc, _floorNormal);
                        _rigidbody.AddForce(-downhillGravity, ForceMode.Acceleration);

                        // Direct snappy braking: bring relative velocity quickly to zero
                        float brakeRate = acceleration * 3.5f; // ~105 m/s²
                        Vector3 newRelativeVel = Vector3.MoveTowards(relativeVel, Vector3.zero, brakeRate * Time.fixedDeltaTime);

                        // Complete dead-stop snap threshold: eliminate residual micro-drift
                        if (newRelativeVel.sqrMagnitude < 0.04f) // < 0.2 m/s
                        {
                            newRelativeVel = Vector3.zero;
                        }

                        _rigidbody.linearVelocity = surfaceVel + newRelativeVel;
                    }
                    else
                    {
                        // On steep walls / ceiling: brake horizontal velocity relative to surface,
                        // while allowing gravity to pull player down to the floor
                        Vector3 relHoriz = new Vector3(relativeVel.x, 0f, relativeVel.z);
                        Vector3 newRelHoriz = Vector3.MoveTowards(relHoriz, Vector3.zero, acceleration * 3.0f * Time.fixedDeltaTime);
                        _rigidbody.linearVelocity = new Vector3(surfaceVel.x + newRelHoriz.x, currentVelocity.y, surfaceVel.z + newRelHoriz.z);
                    }
                }
                else
                {
                    // Airborne with no input: damp horizontal drift relative to die center
                    Vector3 relAirHoriz = currentHorizontalVel - icoHorizVel;
                    Vector3 dampedAir = Vector3.MoveTowards(relAirHoriz, Vector3.zero, 15.0f * Time.fixedDeltaTime);
                    _rigidbody.linearVelocity = new Vector3(icoHorizVel.x + dampedAir.x, currentVelocity.y, icoHorizVel.z + dampedAir.z);
                }
            }
            else
            {
                // Soap-like corner and wall deflection:
                // Smoothly slides along contacting walls and creases instead of getting caught in corners
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

                // Project onto floor slope for smooth climbing
                Vector3 moveOnSlope = isWalkableSlope
                    ? Vector3.ProjectOnPlane(effectiveMoveDir, _floorNormal).normalized
                    : effectiveMoveDir;

                Vector3 targetHorizontalVel = icoHorizVel + moveOnSlope * (_moveInput.magnitude * moveSpeed);
                Vector3 horizDifference = targetHorizontalVel - currentHorizontalVel;

                // Air control: damp horizontal acceleration when airborne so player cannot fly in midair
                float currentAccel = isContacting ? acceleration : (acceleration * airControl);
                Vector3 moveForce = horizDifference * currentAccel;

                // Apply locomotion force
                _rigidbody.AddForce(new Vector3(moveForce.x, moveOnSlope.y * currentAccel * 0.5f, moveForce.z), ForceMode.Acceleration);

                // Orient player along movement direction
                if (effectiveMoveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(effectiveMoveDir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 14f * Time.fixedDeltaTime);
                }
            }

            // --- 4. Height Restriction (Anti-Jitter Soft Cushion) ---
            float ceilingSoftZone = 0.5f;
            float softStart = maxClimbHeight - ceilingSoftZone;
            if (relativeY > softStart)
            {
                float t = Mathf.Clamp01((relativeY - softStart) / ceilingSoftZone);
                Vector3 currentVel = _rigidbody.linearVelocity;
                if (currentVel.y > 0f)
                {
                    Vector3 vel = currentVel;
                    vel.y = Mathf.Lerp(vel.y, 0f, 12f * t * Time.fixedDeltaTime);
                    _rigidbody.linearVelocity = vel;

                    _rigidbody.AddForce(Vector3.down * (acceleration * t * 1.5f), ForceMode.Acceleration);
                }
            }

            // --- 5. 3-Tier Die Rolling (Surface Angle, Relative Climb Height, Wall Push & Impact) ---
            if (_icoController != null)
            {
                // Tier 1: Surface Normal Angle (0 on flat floor < minRollSlopeAngle, scales up on slope)
                float currentSlopeAngle = Vector3.Angle(_floorNormal, Vector3.up);
                float angleFactor = 0f;
                if (isWalkableSlope && currentSlopeAngle > minRollSlopeAngle)
                {
                    angleFactor = Mathf.InverseLerp(minRollSlopeAngle, maxRollSlopeAngle, currentSlopeAngle);
                }

                // Tier 2: Wall Push Factor (active when walking/pushing into an inclined side wall or corner)
                float wallPushFactor = _isPushingWall ? (_wallPushDot * wallPushBoost * _moveInput.magnitude) : 0f;
                if (_isPushingWall && _moveInput.sqrMagnitude > 0.1f)
                {
                    wallPushFactor = Mathf.Max(wallPushFactor, 0.6f * _moveInput.magnitude);
                }

                // Base Drive: climbing slope OR pushing wall drives the roll!
                float baseDrive = Mathf.Max(angleFactor, wallPushFactor);
                if (angleFactor > 0.05f && wallPushFactor > 0.05f)
                {
                    baseDrive = angleFactor + wallPushFactor;
                }

                // Maintain roll momentum when actively pushing movement keys
                if (_moveInput.sqrMagnitude > 0.1f && (_icoController.CurrentTiltAngle > 4.0f || _isPushingWall))
                {
                    baseDrive = Mathf.Max(baseDrive, 0.55f);
                }

                // Tier 3: Height leverage multiplier (the higher up the player climbs, the more torque)
                float lowestFloorY = _icoController.LowestInteriorFloorY;
                float climbHeight = Mathf.Max(0f, transform.position.y - (lowestFloorY + 1.0f));
                float heightMultiplier = 1.0f + (climbHeight * heightTorqueMultiplier);

                float totalEffort = baseDrive * heightMultiplier;

                if (totalEffort > 0.02f && isContacting && _desiredDirection.sqrMagnitude > 0.001f)
                {
                    Vector3 horizDir = new Vector3(_desiredDirection.x, 0f, _desiredDirection.z).normalized;
                    Vector3 rollAxis = Vector3.Cross(Vector3.up, horizDir);

                    float floorRadius = 6.046f;
                    float baseAngularSpeed = (moveSpeed / floorRadius) * rollResponsiveness * totalEffort;
                    baseAngularSpeed = Mathf.Min(baseAngularSpeed, 4.0f);

                    _icoController.DriveRollFromPlayer(rollAxis, baseAngularSpeed, totalEffort, horizDir);
                }
                else
                {
                    _icoController.StopRollFromPlayer();
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

            Rigidbody otherRb = collision.rigidbody ?? collision.gameObject.GetComponentInParent<Rigidbody>();
            if (otherRb != icosahedronRigidbody) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < minImpactSpeedForKick) return;

            int count = collision.contactCount;
            for (int i = 0; i < count; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                // Steep surface: normal.y < 0.65f means wall or steep incline
                if (contact.normal.y < 0.65f)
                {
                    Vector3 hitDirection = -contact.normal;
                    Vector3 horizHit = new Vector3(hitDirection.x, 0f, hitDirection.z);
                    if (horizHit.sqrMagnitude > 0.01f)
                    {
                        Vector3 rollAxis = Vector3.Cross(Vector3.up, horizHit.normalized);
                        float kickImpulse = (impactSpeed - minImpactSpeedForKick) * wallImpactKickStrength;
                        _icoController.ApplyWallImpactKick(rollAxis, kickImpulse);
                        break;
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
            Rigidbody otherRb = collision.rigidbody;
            if (otherRb == null)
            {
                otherRb = collision.gameObject.GetComponentInParent<Rigidbody>();
            }

            if (otherRb == null) return;

            if (icosahedronRigidbody == null && otherRb.GetComponent<IcosahedronController>() != null)
            {
                icosahedronRigidbody = otherRb;
                _icoController = otherRb.GetComponent<IcosahedronController>();
            }

            if (otherRb != icosahedronRigidbody) return;

            _lastIcoContactTime = Time.time;
            _hasFloorContact = false;
            _wallNormals.Clear();

            int contactCount = collision.contactCount;
            float bestFloorY = -1f;
            Vector3 bestFloorNormal = Vector3.up;

            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);

                // Support surface under feet (floor): normal.y >= 0.50f
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

                // Steep walls and corner dihedral surfaces (normal.y < 0.85f):
                // Record for soap-like deflection and wall pushing
                if (contact.normal.y < 0.85f)
                {
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

                    // Detect if player is walking directly into an inclined side wall or corner
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
                _groundNormal = _floorNormal;
            }
        }
    }
}
