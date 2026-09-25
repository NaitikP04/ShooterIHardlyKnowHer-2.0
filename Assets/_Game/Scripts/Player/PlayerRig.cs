using SIHKH.Rail;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SIHKH.Player
{
    /// <summary>
    /// One player on the ring. The server decides which seat you get; every peer then
    /// pins you to that seat each frame, so nothing about your position is ever sent.
    /// Only your inputs travel over the network, and you own them: look direction so
    /// your partner sees where you're aiming, strafe so the server can move your cart.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerRig : NetworkBehaviour, ICartDriver
    {
        [SerializeField] private Transform _head;
        [SerializeField] private Camera _camera;
        [SerializeField, Range(10f, 180f)] private float _yawLimit = 90f;   // can't look behind you
        [SerializeField, Range(10f, 89f)] private float _pitchLimit = 60f;
        [SerializeField] private float _mouseSensitivity = 0.1f;            // degrees per pixel

        private readonly NetworkVariable<int> _seat = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // x = yaw, y = pitch, both in degrees relative to the seat's facing.
        private readonly NetworkVariable<Vector2> _look = new(
            Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // -1 left .. +1 right from the seat's point of view. Only the server reads it.
        private readonly NetworkVariable<float> _strafe = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private InputAction _lookAction;
        private InputAction _moveAction;

        public int Seat => _seat.Value;
        public Vector2 Look => _look.Value;
        public float Strafe => _strafe.Value;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _seat.Value = CartSeats.Current.Claim(OwnerClientId, this);
            }

            if (IsOwner)
            {
                _lookAction = InputSystem.actions.FindAction("Player/Look");
                _moveAction = InputSystem.actions.FindAction("Player/Move");
                _camera.enabled = true;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && CartSeats.Current != null)
            {
                CartSeats.Current.Release(OwnerClientId);
            }

            if (IsOwner)
            {
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None
                    : CursorLockMode.Locked;
            }

            // A/D slides your cart around the ring. W/S does nothing yet.
            _strafe.Value = Mathf.Clamp(_moveAction.ReadValue<Vector2>().x, -1f, 1f);

            // Don't turn the head while the player is using the mouse on the overlay.
            if (Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 delta = _lookAction.ReadValue<Vector2>() * _mouseSensitivity;
            Vector2 look = _look.Value;
            look.x = Mathf.Clamp(look.x + delta.x, -_yawLimit, _yawLimit);
            look.y = Mathf.Clamp(look.y + delta.y, -_pitchLimit, _pitchLimit);
            _look.Value = look;
        }

        // LateUpdate so the cart has already moved this frame; otherwise we'd sit one
        // frame behind it and jitter.
        private void LateUpdate()
        {
            if (!IsSpawned || _seat.Value < 0 || CartSeats.Current == null) return;

            Transform seat = CartSeats.Current.Seat(_seat.Value);
            transform.SetPositionAndRotation(seat.position, seat.rotation);

            Vector2 look = _look.Value;
            _head.localRotation = Quaternion.Euler(-look.y, look.x, 0f);
        }
    }
}
