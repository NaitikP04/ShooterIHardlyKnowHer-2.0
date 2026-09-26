using System.Collections.Generic;
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
    /// Inventory: slot 0 is the default weapon everyone always has; slots 1..N hold one-offs.
    /// Which item sits in which slot is the item's business (OneOffWeapon, server-owned);
    /// which slot is selected is the owner's.
    /// </summary>
    [RequireComponent(typeof(NetworkObject), typeof(PlayerRig))]
    public class PlayerWeapon : NetworkBehaviour
    {
        [SerializeField] private WeaponDefinition _defaultWeapon;
        [SerializeField, Range(1, 8)] private int _oneOffSlots = 3;
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

        // 0 = default weapon, 1..N = one-off slots. Owner's choice.
        private readonly NetworkVariable<int> _selectedSlot = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly List<OneOffWeapon> _heldScratch = new();

        private PlayerRig _rig;
        private InputAction _attackAction;
        private InputAction _interactAction;
        private InputAction _throwAction;
        private InputAction _swapAction;
        private float _ownerNextShot;
        private float _serverNextShot;

        public int OneOffSlots => _oneOffSlots;
        public int SelectedSlot => _selectedSlot.Value;
        public WeaponDefinition DefaultWeapon => _defaultWeapon;

        /// <summary>The one-off in the selected slot, if any. Same answer on every peer.</summary>
        public OneOffWeapon SelectedOneOff =>
            _selectedSlot.Value > 0 ? OneOffWeapon.HeldIn(OwnerClientId, _selectedSlot.Value) : null;

        /// <summary>The numbers currently in use. Same answer on every peer.</summary>
        public WeaponDefinition Current => SelectedOneOff != null ? SelectedOneOff.Definition : _defaultWeapon;

        /// <summary>
        /// Owner-side: the item E would act on right now, and what E would do. Null when
        /// there's nothing. Drives the on-screen prompt and the E key itself, so they agree.
        /// </summary>
        public OneOffWeapon PromptTarget(out string action)
        {
            action = null;
            if (OneOffWeapon.FirstFreeSlot(OwnerClientId, _oneOffSlots) < 0) return null; // hands full

            foreach (var item in OneOffWeapon.All)
            {
                if (item.IsCatchableBy(_rig))
                {
                    action = "Catch!";
                    return item;
                }
            }

            OneOffWeapon ground = OneOffWeapon.NearestOnGround(transform.position, _pickupReach);
            if (ground != null)
            {
                action = "Pick up";
                return ground;
            }
            return null;
        }

        /// <summary>Definition in each slot (null = empty). For HUDs.</summary>
        public WeaponDefinition SlotDefinition(int slot)
        {
            if (slot == 0) return _defaultWeapon;
            OneOffWeapon item = OneOffWeapon.HeldIn(OwnerClientId, slot);
            return item != null ? item.Definition : null;
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

            // Everyone: the default gun is only in hand when it's the selection.
            if (_defaultGunVisual != null) _defaultGunVisual.enabled = SelectedOneOff == null;

            if (!IsOwner) return;

            // A selected slot that emptied (thrown, or never filled) falls back to the default.
            if (_selectedSlot.Value > 0 && SelectedOneOff == null) _selectedSlot.Value = 0;

            if (Cursor.lockState != CursorLockMode.Locked) return; // mouse is on the overlay

            if (_interactAction.WasPressedThisFrame()) UseInteract();
            if (_throwAction.WasPressedThisFrame() && SelectedOneOff != null) ThrowRpc(_selectedSlot.Value, _aimOrigin.forward);
            if (_swapAction.WasPressedThisFrame()) CycleSelection();

            if (_attackAction.IsPressed() && Time.time >= _ownerNextShot)
            {
                _ownerNextShot = Time.time + Current.SecondsBetweenShots;
                Fire();
            }
        }

        private void CycleSelection()
        {
            // Default -> each occupied one-off slot in order -> default.
            OneOffWeapon.HeldBy(OwnerClientId, _heldScratch);
            if (_heldScratch.Count == 0) return;

            int current = _selectedSlot.Value;
            foreach (var item in _heldScratch)
            {
                if (item.Slot > current)
                {
                    _selectedSlot.Value = item.Slot;
                    return;
                }
            }
            _selectedSlot.Value = 0;
        }

        private void UseInteract()
        {
            OneOffWeapon item = PromptTarget(out string action);
            if (item == null) return;

            // Predict the slot the server will choose so the item is in hand immediately.
            _selectedSlot.Value = OneOffWeapon.FirstFreeSlot(OwnerClientId, _oneOffSlots);
            if (action == "Catch!") CatchRpc(item.NetworkObject);
            else PickUpRpc(item.NetworkObject);
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
            int slot = OneOffWeapon.FirstFreeSlot(OwnerClientId, _oneOffSlots);
            if (slot < 0) return;
            if (itemRef.TryGet(out NetworkObject obj) && obj.TryGetComponent(out OneOffWeapon item))
            {
                item.TryPickUp(_rig, slot);
            }
        }

        [Rpc(SendTo.Server)]
        private void CatchRpc(NetworkObjectReference itemRef)
        {
            int slot = OneOffWeapon.FirstFreeSlot(OwnerClientId, _oneOffSlots);
            if (slot < 0) return;
            if (itemRef.TryGet(out NetworkObject obj) && obj.TryGetComponent(out OneOffWeapon item))
            {
                item.TryCatch(_rig, slot);
            }
        }

        [Rpc(SendTo.Server)]
        private void ThrowRpc(int slot, Vector3 direction)
        {
            OneOffWeapon.HeldIn(OwnerClientId, slot)?.TryThrow(_rig, direction);
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
