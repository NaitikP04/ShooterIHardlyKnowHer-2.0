using SIHKH.Core;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Weapons
{
    /// <summary>
    /// A server-simulated projectile that every peer can draw without position updates.
    /// The server publishes a ballistic segment (origin, velocity, start time); peers
    /// evaluate it against the shared clock. Each bounce is a new segment. The server
    /// alone sweeps for hits, applies damage and decides when it dies.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class Projectile : NetworkBehaviour
    {
        public struct Segment : INetworkSerializable
        {
            public Vector3 Origin;
            public Vector3 Velocity;
            public double StartTime;

            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref Origin);
                s.SerializeValue(ref Velocity);
                s.SerializeValue(ref StartTime);
            }
        }

        [SerializeField, Min(0.01f)] private float _radius = 0.18f;
        [SerializeField, Min(0.5f), Tooltip("Despawns after this many seconds no matter what")]
        private float _maxLifetime = 8f;

        private readonly NetworkVariable<Segment> _segment = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Set by the server before Spawn, from the weapon that fired it.
        private float _damage;
        private DamageType _damageType;
        private int _bouncesLeft;
        private int _bouncesAtLaunch;
        private float _restitution;
        private float _gravityScale;
        private ulong _shooter;
        private NetworkObject _shooterObject;

        private double _spawnTime;
        private Vector3 _lastServerPosition;
        private Vector3 _launchOrigin;
        private Vector3 _launchVelocity;

        /// <summary>Server only, before Spawn.</summary>
        public void Configure(WeaponDefinition weapon, ulong shooter, NetworkObject shooterObject)
        {
            _damage = weapon.Damage;
            _damageType = weapon.DamageType;
            _bouncesLeft = weapon.Bounces;
            _bouncesAtLaunch = weapon.Bounces;
            _restitution = weapon.Restitution;
            _gravityScale = weapon.GravityScale;
            _shooter = shooter;
            _shooterObject = shooterObject;
        }

        /// <summary>Server only, before Spawn. The segment itself is written on spawn, when
        /// the NetworkVariable is allowed to carry an initial value.</summary>
        public void Launch(Vector3 origin, Vector3 velocity)
        {
            _launchOrigin = origin;
            _launchVelocity = velocity;
            transform.position = origin;
        }

        public override void OnNetworkSpawn()
        {
            _spawnTime = NetworkManager.ServerTime.Time;
            if (IsServer)
            {
                _segment.Value = new Segment { Origin = _launchOrigin, Velocity = _launchVelocity, StartTime = _spawnTime };
                _lastServerPosition = _launchOrigin;
            }
            transform.position = Evaluate(_spawnTime, out _);
        }

        private Vector3 Evaluate(double now, out Vector3 velocity)
        {
            Segment s = _segment.Value;
            float t = (float)(now - s.StartTime);
            Vector3 g = Physics.gravity * _gravityScale;
            velocity = s.Velocity + g * t;
            return s.Origin + s.Velocity * t + 0.5f * g * (t * t);
        }

        private void Update()
        {
            if (!IsSpawned) return;

            double now = NetworkManager.ServerTime.Time;
            Vector3 position = Evaluate(now, out Vector3 velocity);
            transform.position = position;
            if (velocity.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(velocity);

            if (IsServer) ServerStep(now, position, velocity);
        }

        private void ServerStep(double now, Vector3 position, Vector3 velocity)
        {
            if (now - _spawnTime > _maxLifetime)
            {
                NetworkObject.Despawn(destroy: true);
                return;
            }

            Vector3 travel = position - _lastServerPosition;
            float distance = travel.magnitude;
            if (distance > 0.0001f &&
                Physics.SphereCast(_lastServerPosition, _radius, travel / distance, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                // The first leg can't hurt the shooter (it leaves from inside their head); after
                // a bounce, it absolutely can. That's the point of a bouncy gun.
                var hitObject = hit.collider.GetComponentInParent<NetworkObject>();
                bool isShooter = hitObject != null && hitObject == _shooterObject;
                bool firstLeg = _bouncesLeft == _bouncesAtLaunch;

                if (!(isShooter && firstLeg) && hit.collider.GetComponentInParent<IDamageable>() is { } target)
                {
                    target.TakeDamage(new DamageInfo(_damage, _damageType, hit.point, velocity.normalized, _shooter));
                }

                if (_bouncesLeft <= 0)
                {
                    NetworkObject.Despawn(destroy: true);
                    return;
                }

                _bouncesLeft--;
                Vector3 reflected = Vector3.Reflect(velocity, hit.normal) * _restitution;
                Vector3 origin = hit.point + hit.normal * (_radius * 1.05f);
                _segment.Value = new Segment { Origin = origin, Velocity = reflected, StartTime = now };
                _lastServerPosition = origin;
                return;
            }

            _lastServerPosition = position;
        }
    }
}
