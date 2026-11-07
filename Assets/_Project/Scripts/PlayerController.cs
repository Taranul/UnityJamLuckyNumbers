using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Simple Rigidbody-based character controller for the player inside the icosahedron.
    /// Freezes tipping rotations (X and Z) so the character remains standing upright.
    /// Handles basic 2D movement (WASD) using the new Input System.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 5.0f;
        [SerializeField] private float acceleration = 20.0f;
        [SerializeField] private Transform cameraTransform;

        [Header("Input")]
        [SerializeField] private InputActionReference moveActionReference;

        private Rigidbody _rigidbody;
        private InputAction _fallbackMoveAction;
        private Vector2 _moveInput;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            ConfigurePhysics();

            if (moveActionReference == null)
            {
                // Fallback WASD action if no action reference is provided
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
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
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
                // Direct keyboard fallback
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

            Vector3 desiredDirection = (camForward * _moveInput.y + camRight * _moveInput.x).normalized;
            Vector3 targetVelocity = desiredDirection * (_moveInput.magnitude * moveSpeed);

            Vector3 currentHorizontalVel = new Vector3(_rigidbody.linearVelocity.x, 0f, _rigidbody.linearVelocity.z);
            Vector3 velocityDifference = targetVelocity - currentHorizontalVel;

            Vector3 force = velocityDifference * acceleration;
            _rigidbody.AddForce(new Vector3(force.x, 0f, force.z), ForceMode.Acceleration);

            // Orient the character towards movement direction when moving
            if (desiredDirection.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(desiredDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 12f * Time.fixedDeltaTime);
            }
        }
    }
}
