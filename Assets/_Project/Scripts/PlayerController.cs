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
        private bool _isGroundedOnIcosahedron = false;
        private Vector3 _groundNormal = Vector3.up;
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
            _rigidbody.maxDepenetrationVelocity = 4.0f;

            // Solid friction on capsule to provide natural footing traction
            if (_capsuleCollider != null)
            {
                PhysicsMaterial playerMat = new PhysicsMaterial("PlayerFrictionMat")
                {
                    dynamicFriction = 0.6f,
                    staticFriction = 0.8f,
                    frictionCombine = PhysicsMaterialCombine.Average,
                    bounciness = 0.0f
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
            bool isWalkableSlope = _isGroundedOnIcosahedron && (_groundNormal.y >= minWalkableNormalY);

            Vector3 currentVelocity = _rigidbody.linearVelocity;
            Vector3 currentHorizontalVel = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

            Vector3 icoCenter = (icosahedronRigidbody != null) ? icosahedronRigidbody.worldCenterOfMass : Vector3.zero;
            float relativeY = transform.position.y - icoCenter.y;

            // --- 1. Enhanced Snappy Character Gravity ---
            // Scaled gravity gives the character solid, satisfying weight inside the large 12m die,
            // eliminating sluggish floatiness.
            if (gravityMultiplier > 1.0f)
            {
                _rigidbody.AddForce(Physics.gravity * (gravityMultiplier - 1.0f), ForceMode.Acceleration);
            }

            // --- 2. Ground Snapping & Anti-Launch Adhesion ---
            if (isContacting && isWalkableSlope)
            {
                // Firm downward adhesion along surface normal
                Vector3 stickForce = -_groundNormal * groundStickStrength;
                _rigidbody.AddForce(stickForce, ForceMode.Acceleration);

                // Anti-launch / anti-bounce: cancel any velocity component lifting away from the surface
                float normalSpeed = Vector3.Dot(_rigidbody.linearVelocity, _groundNormal);
                if (normalSpeed > 0.01f)
                {
                    _rigidbody.linearVelocity -= _groundNormal * normalSpeed;
                }
            }

            // --- 3. Decoupled Locomotion (Relative to Moving Die) ---
            Vector3 icoHorizVel = (icosahedronRigidbody != null) 
                ? new Vector3(icosahedronRigidbody.linearVelocity.x, 0f, icosahedronRigidbody.linearVelocity.z) 
                : Vector3.zero;

            if (_moveInput.sqrMagnitude < 0.01f)
            {
                // Active surface braking when idle: quickly extinguishes relative drift to stay planted with moving floor
                if (isContacting)
                {
                    Vector3 relativeHorizVel = currentHorizontalVel - icoHorizVel;
                    Vector3 brakeForce = -relativeHorizVel * (acceleration * 2.0f);
                    _rigidbody.AddForce(brakeForce, ForceMode.Acceleration);
                }
            }
            else
            {
                Vector3 targetHorizontalVel = icoHorizVel + _desiredDirection * (_moveInput.magnitude * moveSpeed);
                Vector3 horizDifference = targetHorizontalVel - currentHorizontalVel;

                // Air control: damp horizontal acceleration when airborne so player cannot fly in midair
                float currentAccel = isContacting ? acceleration : (acceleration * airControl);
                Vector3 moveForce = horizDifference * currentAccel;

                // Strictly horizontal locomotion force - NEVER inject upward Y velocity
                _rigidbody.AddForce(new Vector3(moveForce.x, 0f, moveForce.z), ForceMode.Acceleration);

                // Orient player along movement direction
                if (_desiredDirection.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(_desiredDirection, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 12f * Time.fixedDeltaTime);
                }
            }

            // --- 2. Height Restriction (Anti-Jitter Soft Cushion) ---
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

            // --- 3. 3-Tier Die Rolling (Surface Angle, Relative Climb Height, Wall Push & Impact) ---
            if (_icoController != null)
            {
                // Tier 1: Surface Normal Angle (0 on flat floor < 12 deg, scales up to 1.0 on slope)
                float currentSlopeAngle = Vector3.Angle(_groundNormal, Vector3.up);
                float angleFactor = 0f;
                if (isWalkableSlope && currentSlopeAngle > minRollSlopeAngle)
                {
                    angleFactor = Mathf.InverseLerp(minRollSlopeAngle, maxRollSlopeAngle, currentSlopeAngle);
                }

                // Tier 2: Wall Push Factor (active when walking/pushing into an inclined side wall)
                float wallPushFactor = _isPushingWall ? (_wallPushDot * wallPushBoost * _moveInput.magnitude) : 0f;

                // Base Drive: climbing slope OR pushing wall drives the roll!
                // If on flat floor and NOT pushing a wall -> baseDrive = 0 (die stays still).
                // If climbing slope or pushing wall -> baseDrive > 0 (die rolls!).
                float baseDrive = Mathf.Max(angleFactor, wallPushFactor);
                if (angleFactor > 0.05f && wallPushFactor > 0.05f)
                {
                    baseDrive = angleFactor + wallPushFactor;
                }

                // If player is actively pressing movement keys AND the die is already rolling/tilting:
                // Maintain roll momentum so the die doesn't stall or snap backwards halfway through!
                if (_moveInput.sqrMagnitude > 0.2f && _icoController.CurrentTiltAngle > 6.0f)
                {
                    baseDrive = Mathf.Max(baseDrive, 0.45f);
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
                    baseAngularSpeed = Mathf.Min(baseAngularSpeed, 3.8f);

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
            _isGroundedOnIcosahedron = false;

            int contactCount = collision.contactCount;
            Vector3 accumulatedGroundNormal = Vector3.zero;
            int groundContacts = 0;

            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);

                if (contact.normal.y > 0.25f)
                {
                    _isGroundedOnIcosahedron = true;
                    accumulatedGroundNormal += contact.normal;
                    groundContacts++;
                }

                // Detect if player is walking directly into an inclined side wall
                if (_moveInput.sqrMagnitude > 0.01f && _desiredDirection.sqrMagnitude > 0.01f)
                {
                    float pushDot = Vector3.Dot(_desiredDirection, -contact.normal);
                    if (pushDot > 0.15f && contact.normal.y < 0.95f)
                    {
                        _isPushingWall = true;
                        _wallPushDot = Mathf.Max(_wallPushDot, pushDot);
                    }
                }
            }

            if (groundContacts > 0)
            {
                _groundNormal = (accumulatedGroundNormal / groundContacts).normalized;
            }
        }
    }
}
