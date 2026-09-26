using System.Collections;
using System.Collections.Generic;
using SIHKH.Core;
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
        /// <summary>Every spawned player on this peer. Enemies pick targets from here.</summary>
        public static readonly List<PlayerRig> Active = new();

        [SerializeField] private Transform _head;
        [SerializeField] private Camera _camera;
        [SerializeField, Range(10f, 180f)] private float _yawLimit = 90f;   // can't look behind you
        [SerializeField, Range(10f, 89f)] private float _pitchLimit = 60f;
        [SerializeField] private float _mouseSensitivity = 0.1f;            // degrees per pixel
        // Whitebox death: stand there for a moment, then come back. A real run-over loop
        // replaces this once waves exist.
        [SerializeField, Min(0f)] private float _respawnSeconds = 3f;

        // Whitebox tint per seat: blue side, orange side. Applied with a property block so
        // every player shares one material instead of each spawning its own copy.
        [SerializeField] private Color[] _seatColors =
        {
            new(0.25f, 0.5f, 0.95f),
            new(0.95f, 0.55f, 0.2f),
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly NetworkVariable<int> _seat = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // x = yaw, y = pitch, both in degrees relative to the seat's facing.
        private readonly NetworkVariable<Vector2> _look = new(
            Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // -1 left .. +1 right from the seat's point of view. Only the server reads it.
        private readonly NetworkVariable<float> _strafe = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // Toggled with S. Dug in: the cart won't budge, so a pulling partner snaps the chain.
        private readonly NetworkVariable<bool> _planted = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private InputAction _lookAction;
        private InputAction _moveAction;
        private bool _plantKeyHeld;
        private Health _health;

        public int Seat => _seat.Value;
        public Transform Head => _head;
        public Camera Camera => _camera;
        public Vector2 Look => _look.Value;
        public float Strafe => _strafe.Value;
        public bool Planted => _planted.Value;

        public override void OnNetworkSpawn()
        {
            Active.Add(this);
            _health = GetComponent<Health>();

            if (IsServer)
            {
                _seat.Value = CartSeats.Current.Claim(OwnerClientId, this);
                if (_health != null) _health.Died += OnDied;
            }

            // Late joiners get the seat with the spawn payload; the host's own player gets
            // it a frame later via the change callback. Handle both.
            _seat.OnValueChanged += (_, _) => ApplyTint();
            _planted.OnValueChanged += (_, _) => ApplyTint();
            ApplyTint();

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
            Active.Remove(this);

            if (IsServer)
            {
                if (_health != null) _health.Died -= OnDied;
                if (CartSeats.Current != null) CartSeats.Current.Release(OwnerClientId);
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

            Vector2 move = _moveAction.ReadValue<Vector2>();

            // A/D slides your cart along your side of the oval.
            _strafe.Value = Mathf.Clamp(move.x, -1f, 1f);

            // S toggles planting. Edge-detected by hand because Move is an axis, not a button.
            bool plantKey = move.y < -0.5f;
            if (plantKey && !_plantKeyHeld) _planted.Value = !_planted.Value;
            _plantKeyHeld = plantKey;

            // Don't turn the head while the player is using the mouse on the overlay.
            if (Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 delta = _lookAction.ReadValue<Vector2>() * _mouseSensitivity;
            Vector2 look = _look.Value;
            look.x = Mathf.Clamp(look.x + delta.x, -_yawLimit, _yawLimit);
            look.y = Mathf.Clamp(look.y + delta.y, -_pitchLimit, _pitchLimit);
            _look.Value = look;
        }

        private void OnDied(DamageInfo killingBlow) => StartCoroutine(RespawnAfterDelay());

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(_respawnSeconds);
            if (IsSpawned) _health.ResetToFull();
        }

        private void ApplyTint()
        {
            int seat = _seat.Value;
            if (seat < 0 || seat >= _seatColors.Length) return;

            // Planted reads as a darker version of your colour, so it's visible from across
            // the gap without any UI.
            Color c = _planted.Value ? _seatColors[seat] * 0.45f : _seatColors[seat];
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, c);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponent<KeepMaterialColor>() != null) continue;
                r.SetPropertyBlock(block);
            }
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
