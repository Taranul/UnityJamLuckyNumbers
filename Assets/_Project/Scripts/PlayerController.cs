using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Rigidbody-based character controller for the player inside the icosahedron (d20 die).
    /// - Stays firmly on internal surfaces when idle (active surface braking, zero drift).
    /// - Moves with WASD, naturally rolling the die underneath the player.
    /// - Cannot climb or lift higher than the adjacent side surfaces.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 5.0f;
        [SerializeField] private float acceleration = 25.0f;
        [SerializeField] private Transform cameraTransform;

        [Header("Die Interaction")]
        [Tooltip("Responsiveness multiplier for rolling the die with player movement.")]
        [SerializeField] private float rollResponsiveness = 1.6f;

        [Tooltip("Additional roll speed boost when actively running into an inclined side wall.")]
        [SerializeField] private float wallPushBoost = 0.5f;

        [Tooltip("Minimum surface normal Y component required to walk up a surface. Normal Y of side faces is ~0.75 (41.8 deg slope). Surfaces steeper than ~53 deg cannot be climbed.")]
        [SerializeField] private float minWalkableNormalY = 0.60f;

        [Tooltip("Maximum allowed height of player center relative to icosahedron center. Top of side faces is at -2.62m.")]
        [SerializeField] private float maxClimbHeight = -1.8f;

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
            Vector3 icoCenter = (icosahedronRigidbody != null) ? icosahedronRigidbody.worldCenterOfMass : Vector3.zero;
            float relativeY = transform.position.y - icoCenter.y;

            // --- 1. Player Locomotion ---
            if (_moveInput.sqrMagnitude < 0.01f)
            {
                // Active surface braking when idle: plants feet firmly on the floor face
                if (isContacting)
                {
                    Vector3 horizVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
                    Vector3 brakeForce = -horizVelocity * (acceleration * 2.0f);
                    _rigidbody.AddForce(brakeForce, ForceMode.Acceleration);
                }
            }
            else
            {
                // Moving: project direction onto walkable ground slope
                Vector3 moveDirection = _desiredDirection;
                if (isWalkableSlope)
                {
                    Vector3 projected = Vector3.ProjectOnPlane(_desiredDirection, _groundNormal).normalized;
                    // Only use slope projection if not climbing beyond side surface
                    if (relativeY < maxClimbHeight || projected.y <= 0f)
                    {
                        moveDirection = projected;
                    }
                    else
                    {
                        // At top of side surface: keep movement purely horizontal
                        moveDirection = new Vector3(_desiredDirection.x, 0f, _desiredDirection.z).normalized;
                    }
                }
                else if (isContacting && _groundNormal.y < minWalkableNormalY)
                {
                    // On steep unwalkable surfaces: do not allow upward ascent
                    moveDirection = new Vector3(_desiredDirection.x, 0f, _desiredDirection.z).normalized;
                }

                Vector3 targetVelocity = moveDirection * (_moveInput.magnitude * moveSpeed);
                Vector3 velDifference = targetVelocity - currentVelocity;

                // If at the height limit, smoothly suppress upward acceleration
                if (relativeY >= maxClimbHeight && velDifference.y > 0f)
                {
                    velDifference.y = Mathf.Lerp(velDifference.y, 0f, 0.85f);
                }

                Vector3 moveForce = velDifference * acceleration;
                _rigidbody.AddForce(moveForce, ForceMode.Acceleration);

                // Orient player along movement direction
                if (_desiredDirection.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(_desiredDirection, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 12f * Time.fixedDeltaTime);
                }
            }

            // --- 2. Height Restriction (Anti-Jitter Soft Cushion) ---
            // Progressively absorb upward velocity as player approaches or exceeds the side surface height,
            // avoiding hard velocity-zeroing discontinuities that cause 50Hz collider chatter/jitter.
            float ceilingSoftZone = 0.5f;
            float softStart = maxClimbHeight - ceilingSoftZone;
            if (relativeY > softStart)
            {
                float t = Mathf.Clamp01((relativeY - softStart) / ceilingSoftZone);
                Vector3 currentVel = _rigidbody.linearVelocity;
                if (currentVel.y > 0f)
                {
                    // Smoothly bleed off upward velocity
                    Vector3 vel = currentVel;
                    vel.y = Mathf.Lerp(vel.y, 0f, 12f * t * Time.fixedDeltaTime);
                    _rigidbody.linearVelocity = vel;

                    // Apply soft downward deceleration force proportional to intrusion depth
                    _rigidbody.AddForce(Vector3.down * (acceleration * t * 1.5f), ForceMode.Acceleration);
                }
            }

            // --- 3. Harmonious Die Rolling ---
            if (_icoController != null)
            {
                if (_moveInput.sqrMagnitude > 0.01f && isContacting)
                {
                    Vector3 horizDir = new Vector3(_desiredDirection.x, 0f, _desiredDirection.z).normalized;
                    if (horizDir.sqrMagnitude > 0.001f)
                    {
                        // World rotation axis rolling the die forward in the movement direction
                        Vector3 rollAxis = Vector3.Cross(Vector3.up, horizDir);

                        // Angular speed proportional to player movement speed (omega = v / R)
                        float floorRadius = 6.046f;
                        float baseAngularSpeed = (moveSpeed / floorRadius) * rollResponsiveness * _moveInput.magnitude;

                        if (_isPushingWall)
                        {
                            baseAngularSpeed *= (1.0f + wallPushBoost);
                        }

                        _icoController.DriveRollFromPlayer(rollAxis, baseAngularSpeed);
                    }
                    else
                    {
                        _icoController.StopRollFromPlayer();
                    }
                }
                else
                {
                    // Idle: stop active player rolling and let the die settle flat onto the nearest face
                    _icoController.StopRollFromPlayer();
                }
            }

            _isPushingWall = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            ProcessCollision(collision);
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
