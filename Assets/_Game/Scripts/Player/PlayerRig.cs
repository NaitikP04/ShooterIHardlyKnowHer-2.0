using SIHKH.Rail;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SIHKH.Player
{
    /// <summary>
    /// One player on the cart. The server decides which seat you get; every peer then
    /// pins you to that seat each frame, so nothing about your position is ever sent.
    /// Only your look direction travels over the network, and you own it: the other
    /// player needs to see where you're aiming, but nobody else may steer your head.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerRig : NetworkBehaviour
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

        private InputAction _lookAction;

        public int Seat => _seat.Value;
        public Vector2 Look => _look.Value;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _seat.Value = CartSeats.Current.Claim(OwnerClientId);
            }

            if (IsOwner)
            {
                _lookAction = InputSystem.actions.FindAction("Player/Look");
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
