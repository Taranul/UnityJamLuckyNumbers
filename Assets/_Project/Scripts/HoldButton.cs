using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Handles continuous press-and-hold interaction for directional UI arrow buttons.
    /// </summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [FormerlySerializedAs("directionVector")]
        [SerializeField] private Vector2 _directionVector = Vector2.zero;

        [FormerlySerializedAs("controller")]
        [SerializeField] private IcosahedronController _controller;

        private bool _isHeld;

        public void Initialize(IcosahedronController targetController, Vector2 direction)
        {
            _controller = targetController;
            _directionVector = direction;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isHeld = true;
            if (_controller != null)
            {
                _controller.SetDirectionHeld(_directionVector, true);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        private void OnDisable()
        {
            Release();
        }

        private void Release()
        {
            if (_isHeld)
            {
                _isHeld = false;
                if (_controller != null)
                {
                    _controller.SetDirectionHeld(_directionVector, false);
                }
            }
        }
    }
}
