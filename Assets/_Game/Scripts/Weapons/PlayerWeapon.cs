using SIHKH.Core;
using SIHKH.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SIHKH.Weapons
{
    /// <summary>
    /// The gun in a player's hands. The owner asks to fire; the server decides what was hit.
    /// That split is what makes friendly fire fair: your partner's collider is just another
    /// thing the server's raycast can find.
    ///
    /// Every player always has the default weapon. If they're holding a one-off and have it
    /// selected, that one's numbers are used instead. Which one-off you hold is the item's
    /// business (OneOffWeapon); whether you've selected it is yours.
    /// </summary>
    [RequireComponent(typeof(NetworkObject), typeof(PlayerRig))]
    public class PlayerWeapon : NetworkBehaviour
    {
        [SerializeField] private WeaponDefinition _defaultWeapon;
        [Tooltip("Where the aim ray starts: the eyes, so shots go where the crosshair points")]
        [SerializeField] private Transform _aimOrigin;
        [Tooltip("Where the tracer starts: the gun's tip, so you see the shot leave")]
        [SerializeField] private Transform _muzzle;
        [SerializeField] private Renderer _defaultGunVisual;
        [SerializeField] private Tracer _tracerPrefab;
        [SerializeField] private ImpactFlash _impactPrefab;
        [SerializeField, Min(0.1f)] private float _pickupReach = 2.5f;

        // Loose sanity bounds on client claims. Not anti-cheat, just "don't be silly".
        private const float MaxAimOriginError = 2f;   // metres between claimed and actual eye position
        private const float RateTolerance = 0.05f;    // seconds of slack on the fire rate

        // Owner's choice between the default gun and the one-off they're holding.
        private readonly NetworkVariable<bool> _useOneOff = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerRig _rig;
        private InputAction _attackAction;
        private InputAction _interactAction;
        private InputAction _throwAction;
        private InputAction _swapAction;
        private float _ownerNextShot;
        private float _serverNextShot;

        /// <summary>The one-off this player is holding, if any. Same answer on every peer.</summary>
        public OneOffWeapon Held => OneOffWeapon.HeldBy(OwnerClientId);

        /// <summary>The numbers currently in use. Same answer on every peer.</summary>
        public WeaponDefinition Current
        {
            get
            {
                OneOffWeapon held = _useOneOff.Value ? Held : null;
                return held != null ? held.Definition : _defaultWeapon;
            }
        }

        public override void OnNetworkSpawn()
        {
            _rig = GetComponent<PlayerRig>();
            if (IsOwner)
            {
                var actions = InputSystem.actions;
                _attackAction = actions.FindAction("Player/Attack");
                _interactAction = actions.FindAction("Player/Interact");
                _throwAction = actions.FindAction("Player/Throw");
                _swapAction = actions.FindAction("Player/Swap");
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;

            // Everyone: hide the default gun while a one-off is in hand and selected.
            if (_defaultGunVisual != null) _defaultGunVisual.enabled = Current == _defaultWeapon;

            if (!IsOwner) return;
            if (Cursor.lockState != CursorLockMode.Locked) return; // mouse is on the overlay

            if (_interactAction.WasPressedThisFrame()) TryPickUpNearby();
            if (_throwAction.WasPressedThisFrame() && Held != null) ThrowRpc(_aimOrigin.forward);
            if (_swapAction.WasPressedThisFrame() && Held != null) _useOneOff.Value = !_useOneOff.Value;

            if (_attackAction.IsPressed() && Time.time >= _ownerNextShot)
            {
                _ownerNextShot = Time.time + Current.SecondsBetweenShots;
                Fire();
            }
        }

        private void TryPickUpNearby()
        {
            OneOffWeapon item = OneOffWeapon.NearestOnGround(transform.position, _pickupReach);
            if (item == null) return;
            _useOneOff.Value = true; // picking something up means you want to use it
            PickUpRpc(item.NetworkObject);
        }

        /// <summary>Owner only. Aims where the eyes point, with the weapon's spread applied.</summary>
        public void Fire()
        {
            WeaponDefinition weapon = Current;
            Vector3 direction = _aimOrigin.forward;
            if (weapon.SpreadDegrees > 0f)
            {
                direction = Quaternion.AngleAxis(Random.Range(0f, 360f), _aimOrigin.forward)
                          * Quaternion.AngleAxis(Random.Range(0f, weapon.SpreadDegrees), _aimOrigin.up)
                          * direction;
            }
            FireRpc(_aimOrigin.position, direction);
        }

        [Rpc(SendTo.Server)]
        private void PickUpRpc(NetworkObjectReference itemRef)
        {
            if (Held != null) return; // one at a time
            if (itemRef.TryGet(out NetworkObject obj) && obj.TryGetComponent(out OneOffWeapon item))
            {
                item.TryPickUp(_rig);
            }
        }

        [Rpc(SendTo.Server)]
        private void ThrowRpc(Vector3 direction)
        {
            Held?.TryThrow(_rig, direction);
        }

        [Rpc(SendTo.Server)]
        private void FireRpc(Vector3 origin, Vector3 direction, RpcParams rpc = default)
        {
            WeaponDefinition weapon = Current;
            if ((origin - _aimOrigin.position).sqrMagnitude > MaxAimOriginError * MaxAimOriginError) return;
            if (Time.time < _serverNextShot - RateTolerance) return;
            _serverNextShot = Time.time + weapon.SecondsBetweenShots;

            direction.Normalize();
            Vector3 end = origin + direction * weapon.Range;
            bool hitSomething = false;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, weapon.Range, ~0, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                hitSomething = true;

                // Friendly fire yes, self fire no: a ray leaving your own head can only clip
                // your own body through spread, and that shouldn't count.
                var hitObject = hit.collider.GetComponentInParent<NetworkObject>();
                bool self = hitObject != null && hitObject == NetworkObject;

                if (!self && hit.collider.GetComponentInParent<IDamageable>() is { } target)
                {
                    target.TakeDamage(new DamageInfo(
                        weapon.Damage, weapon.DamageType, hit.point, direction, rpc.Receive.SenderClientId));
                }
            }

            // The server's copy of the gun is where everyone else sees it, so its muzzle is
            // the honest place for the tracer to start.
            ShotFxRpc(_muzzle.position, end, hitSomething, weapon.TracerColor);
        }

        [Rpc(SendTo.Everyone)]
        private void ShotFxRpc(Vector3 muzzle, Vector3 end, bool hitSomething, Color color)
        {
            if (_tracerPrefab != null) Instantiate(_tracerPrefab).Show(muzzle, end, color);
            if (hitSomething && _impactPrefab != null) Instantiate(_impactPrefab).Show(end, color);
        }
    }
}
