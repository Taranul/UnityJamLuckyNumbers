using UnityEngine;
using UnityEngine.EventSystems;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Handles continuous press-and-hold for UI directional arrow buttons.
    /// Resets if released or if the pointer exits the button area to prevent stuck input.
    /// </summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private Vector2 directionVector = Vector2.zero;
        [SerializeField] private IcosahedronController controller;

        private bool _isHeld;

        public void Initialize(IcosahedronController targetController, Vector2 direction)
        {
            controller = targetController;
            directionVector = direction;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isHeld = true;
            if (controller != null)
            {
                controller.SetDirectionHeld(directionVector, true);
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
                if (controller != null)
                {
                    controller.SetDirectionHeld(directionVector, false);
                }
            }
        }
    }
}
