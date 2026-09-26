using SIHKH.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SIHKH.Weapons
{
    /// <summary>
    /// The gun in a player's hands. The owner asks to fire; the server decides what was hit.
    /// That split is what makes friendly fire fair: your partner's collider is just another
    /// thing the server's raycast can find.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerWeapon : NetworkBehaviour
    {
        [SerializeField] private WeaponDefinition _weapon;
        [Tooltip("Where the aim ray starts: the eyes, so shots go where the crosshair points")]
        [SerializeField] private Transform _aimOrigin;
        [Tooltip("Where the tracer starts: the gun's tip, so you see the shot leave")]
        [SerializeField] private Transform _muzzle;
        [SerializeField] private Tracer _tracerPrefab;
        [SerializeField] private ImpactFlash _impactPrefab;

        // Loose sanity bounds on client claims. Not anti-cheat, just "don't be silly".
        private const float MaxAimOriginError = 2f;   // metres between claimed and actual eye position
        private const float RateTolerance = 0.05f;    // seconds of slack on the fire rate

        private InputAction _attackAction;
        private float _ownerNextShot;
        private float _serverNextShot;

        public WeaponDefinition Current => _weapon;

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                _attackAction = InputSystem.actions.FindAction("Player/Attack");
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || _weapon == null) return;
            if (Cursor.lockState != CursorLockMode.Locked) return; // mouse is on the overlay

            if (_attackAction.IsPressed() && Time.time >= _ownerNextShot)
            {
                _ownerNextShot = Time.time + _weapon.SecondsBetweenShots;
                Fire();
            }
        }

        /// <summary>Owner only. Aims where the eyes point, with the weapon's spread applied.</summary>
        public void Fire()
        {
            Vector3 direction = _aimOrigin.forward;
            if (_weapon.SpreadDegrees > 0f)
            {
                direction = Quaternion.AngleAxis(Random.Range(0f, 360f), _aimOrigin.forward)
                          * Quaternion.AngleAxis(Random.Range(0f, _weapon.SpreadDegrees), _aimOrigin.up)
                          * direction;
            }
            FireRpc(_aimOrigin.position, direction);
        }

        [Rpc(SendTo.Server)]
        private void FireRpc(Vector3 origin, Vector3 direction, RpcParams rpc = default)
        {
            if ((origin - _aimOrigin.position).sqrMagnitude > MaxAimOriginError * MaxAimOriginError) return;
            if (Time.time < _serverNextShot - RateTolerance) return;
            _serverNextShot = Time.time + _weapon.SecondsBetweenShots;

            direction.Normalize();
            Vector3 end = origin + direction * _weapon.Range;
            bool hitSomething = false;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, _weapon.Range, ~0, QueryTriggerInteraction.Ignore))
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
                        _weapon.Damage, _weapon.DamageType, hit.point, direction, rpc.Receive.SenderClientId));
                }
            }

            // The server's copy of the gun is where everyone else sees it, so its muzzle is
            // the honest place for the tracer to start.
            ShotFxRpc(_muzzle.position, end, hitSomething);
        }

        [Rpc(SendTo.Everyone)]
        private void ShotFxRpc(Vector3 muzzle, Vector3 end, bool hitSomething)
        {
            if (_tracerPrefab != null) Instantiate(_tracerPrefab).Show(muzzle, end, _weapon.TracerColor);
            if (hitSomething && _impactPrefab != null) Instantiate(_impactPrefab).Show(end, _weapon.TracerColor);
        }
    }
}
