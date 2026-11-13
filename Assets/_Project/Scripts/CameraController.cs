using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using VContainer;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Third-person inspection camera following the player, supporting free orbit and subtle landing shake.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Target")]
        [FormerlySerializedAs("target")]
        [SerializeField] private Transform _target;

        [FormerlySerializedAs("lookOffset")]
        [SerializeField] private Vector3 _lookOffset = new Vector3(0f, 1.2f, 0f);

        [Header("Distance & Orbit")]
        [FormerlySerializedAs("distance")]
        [SerializeField] private float _distance = 18.0f;

        [FormerlySerializedAs("minDistance")]
        [SerializeField] private float _minDistance = 11.0f;

        [FormerlySerializedAs("maxDistance")]
        [SerializeField] private float _maxDistance = 35.0f;

        [FormerlySerializedAs("zoomSensitivity")]
        [SerializeField] private float _zoomSensitivity = 0.1f;

        [FormerlySerializedAs("mouseSensitivity")]
        [SerializeField] private float _mouseSensitivity = 2.0f;

        [FormerlySerializedAs("minPitch")]
        [SerializeField] private float _minPitch = -40f;

        [FormerlySerializedAs("maxPitch")]
        [SerializeField] private float _maxPitch = 85f;

        [FormerlySerializedAs("positionSmoothTime")]
        [SerializeField] private float _positionSmoothTime = 0.08f;

        [Header("Landing Impact Polish")]
        [FormerlySerializedAs("landingShakeDuration")]
        [SerializeField] private float _landingShakeDuration = 0.18f;

        [FormerlySerializedAs("landingShakeMagnitude")]
        [SerializeField] private float _landingShakeMagnitude = 0.28f;

        private float _yaw;
        private float _pitch = 22f;
        private Vector3 _currentVelocity;
        private float _landingShakeTimer;
        private IcosahedronController _icoController;

        [Inject]
        public void Construct(IcosahedronController icosahedronController)
        {
            _icoController = icosahedronController;
            if (_icoController != null)
            {
                _icoController.OnFaceLanded += HandleFaceLanded;
            }
        }

        private void Start()
        {
            if (_target != null)
            {
                Vector3 angles = transform.eulerAngles;
                _yaw = angles.y;
                _pitch = angles.x;
            }

            if (_icoController == null)
            {
                _icoController = FindFirstObjectByType<IcosahedronController>();
                if (_icoController != null)
                {
                    _icoController.OnFaceLanded += HandleFaceLanded;
                }
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
            _landingShakeTimer = _landingShakeDuration;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue() * (_mouseSensitivity * 0.1f);
                    _yaw += delta.x;
                    _pitch = Mathf.Clamp(_pitch - delta.y, _minPitch, _maxPitch);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _distance = Mathf.Clamp(_distance - scroll * _zoomSensitivity, _minDistance, _maxDistance);
                }
            }

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focusPoint = _target.position + _lookOffset;

            if (_landingShakeTimer > 0f)
            {
                _landingShakeTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_landingShakeTimer / _landingShakeDuration);
                float dip = Mathf.Sin((1f - t) * Mathf.PI) * _landingShakeMagnitude;
                focusPoint += Vector3.down * dip;
            }

            Vector3 targetPosition = focusPoint - (rotation * Vector3.forward * _distance);

            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref _currentVelocity, _positionSmoothTime);
            transform.rotation = rotation;
        }

        public void SetTarget(Transform newTarget)
        {
            _target = newTarget;
        }
    }
}
