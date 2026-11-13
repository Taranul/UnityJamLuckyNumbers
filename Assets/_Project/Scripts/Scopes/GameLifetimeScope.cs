using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// VContainer lifetime scope for the gameplay scene.
    /// Registers core controllers and resolves scene-level dependencies.
    /// </summary>
    public class GameLifetimeScope : LifetimeScope
    {
        [SerializeField] private IcosahedronController _icosahedronController;
        [SerializeField] private PlayerController _playerController;
        [SerializeField] private CameraController _cameraController;

        protected override void Configure(IContainerBuilder builder)
        {
            if (_icosahedronController != null)
            {
                builder.RegisterComponent(_icosahedronController);
            }
            else
            {
                builder.RegisterComponentInHierarchy<IcosahedronController>();
            }

            if (_playerController != null)
            {
                builder.RegisterComponent(_playerController);
            }
            else
            {
                builder.RegisterComponentInHierarchy<PlayerController>();
            }

            if (_cameraController != null)
            {
                builder.RegisterComponent(_cameraController);
            }
            else
            {
                builder.RegisterComponentInHierarchy<CameraController>();
            }
        }
    }
}
