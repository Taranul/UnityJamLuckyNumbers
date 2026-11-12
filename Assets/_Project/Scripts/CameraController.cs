using UnityEngine;
using UnityEngine.InputSystem;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Third-person inspection camera that follows the player inside the icosahedron.
    /// Allows free orbit using Right Mouse Button to inspect all interior surfaces.
    /// Features a subtle tactile impact shake when the die lands flat on a face.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1.2f, 0f);

        [Header("Distance & Orbit")]
        [SerializeField] private float distance = 18.0f;
        [SerializeField] private float minDistance = 11.0f;
        [SerializeField] private float maxDistance = 35.0f;
        [SerializeField] private float zoomSensitivity = 0.1f;
        [SerializeField] private float mouseSensitivity = 2.0f;
        [SerializeField] private float minPitch = -40f;
        [SerializeField] private float maxPitch = 85f;
        [SerializeField] private float positionSmoothTime = 0.08f;

        [Header("Landing Impact Polish")]
        [Tooltip("Duration of subtle camera shake dip when the heavy die lands flat.")]
        [SerializeField] private float landingShakeDuration = 0.18f;
        [Tooltip("Vertical dip magnitude of landing impact.")]
        [SerializeField] private float landingShakeMagnitude = 0.28f;

        private float _yaw = 0f;
        private float _pitch = 22f;
        private Vector3 _currentVelocity;
        private float _landingShakeTimer = 0f;
        private IcosahedronController _icoController;

        private void Start()
        {
            if (target != null)
            {
                Vector3 angles = transform.eulerAngles;
                _yaw = angles.y;
                _pitch = angles.x;
            }

            _icoController = FindFirstObjectByType<IcosahedronController>();
            if (_icoController != null)
            {
                _icoController.OnFaceLanded += HandleFaceLanded;
            }
        }

        private void OnDestroy()
        {
            if (_icoController != null)
            {
                _icoController.OnFaceLanded -= HandleFaceLanded;
            }
        }

        private void HandleFaceLanded(int rolledTopNumber)
        {
            _landingShakeTimer = landingShakeDuration;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue() * (mouseSensitivity * 0.1f);
                    _yaw += delta.x;
                    _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    distance = Mathf.Clamp(distance - scroll * zoomSensitivity, minDistance, maxDistance);
                }
            }

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focusPoint = target.position + lookOffset;

            // Apply subtle landing impact dip
            if (_landingShakeTimer > 0f)
            {
                _landingShakeTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_landingShakeTimer / landingShakeDuration);
                float dip = Mathf.Sin((1f - t) * Mathf.PI) * landingShakeMagnitude;
                focusPoint += Vector3.down * dip;
            }

            Vector3 targetPosition = focusPoint - (rotation * Vector3.forward * distance);

            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref _currentVelocity, positionSmoothTime);
            transform.rotation = rotation;
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }
    }
}
