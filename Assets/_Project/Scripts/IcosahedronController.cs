using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Controls the physical rotation of the icosahedron using continuous relative torque.
    /// Gravity is disabled and position is frozen so it rotates in-place.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class IcosahedronController : MonoBehaviour
    {
        [Header("Physics Settings")]
        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private float rotationAcceleration = 4.0f;
        [SerializeField] private float inputSmoothTime = 0.2f;
        [SerializeField] private float angularDamping = 1.2f;
        [SerializeField] private float maxAngularVelocity = 5.0f;

        [Header("Keyboard Debug Controls")]
        [SerializeField] private bool enableKeyboardDebug = true;

        private Vector2 _targetInput = Vector2.zero;
        private Vector2 _currentInput = Vector2.zero;
        private Vector2 _inputSmoothVelocity = Vector2.zero;
        private readonly HashSet<Vector2> _activePointerDirections = new HashSet<Vector2>();

        private void Awake()
        {
            if (targetRigidbody == null)
            {
                targetRigidbody = GetComponent<Rigidbody>();
            }

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

            targetRigidbody.useGravity = false;
            targetRigidbody.constraints = RigidbodyConstraints.FreezePosition;
            targetRigidbody.angularDamping = angularDamping;
            targetRigidbody.maxAngularVelocity = maxAngularVelocity;
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

            UpdateCombinedInput();
        }

        private void Update()
        {
            if (enableKeyboardDebug)
            {
                UpdateCombinedInput();
            }
        }

        private void UpdateCombinedInput()
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

            _targetInput = Vector2.ClampMagnitude(combined, 1f);
        }

        private void FixedUpdate()
        {
            if (targetRigidbody == null) return;

            targetRigidbody.angularDamping = angularDamping;
            targetRigidbody.maxAngularVelocity = maxAngularVelocity;

            _currentInput = Vector2.SmoothDamp(
                _currentInput,
                _targetInput,
                ref _inputSmoothVelocity,
                Mathf.Max(0.01f, inputSmoothTime),
                Mathf.Infinity,
                Time.fixedDeltaTime);

            // Up/Down: rotation around local X axis
            // Left/Right: rotation around local Z axis
            float torqueX = _currentInput.y * rotationAcceleration;
            float torqueZ = -_currentInput.x * rotationAcceleration;

            Vector3 relativeTorque = new Vector3(torqueX, 0f, torqueZ);

            if (relativeTorque.sqrMagnitude > 0.0001f)
            {
                targetRigidbody.AddRelativeTorque(relativeTorque, ForceMode.Acceleration);
            }
        }
    }
}
