using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Rail
{
    /// <summary>
    /// Server-side physics for the two carts on the circular rail, all in one dimension:
    /// metres along the rail. Each player strafes their own cart; the chain across the
    /// ring is a spring that pulls the pair back toward being exactly opposite each
    /// other and snaps when they drift too far off. Clients never run any of this; they
    /// read the carts' distances and the snapped flag.
    /// </summary>
    public class CartPair : NetworkBehaviour
    {
        [Header("Carts")]
        [SerializeField] private RailCart _cartA;
        [SerializeField] private RailCart _cartB;

        [Header("Strafing")]
        [SerializeField, Min(0f)] private float _strafeSpeed = 4f;    // m/s along the rail at full input
        // Drive force grows with how far you are from your target speed, so two players
        // pulling opposite ways strain the chain twice as hard as one player alone.
        // Snapping is meant to be a two-person mistake.
        [SerializeField, Min(0.1f)] private float _driveGain = 4f;    // m/s² per m/s of speed error

        [Header("Chain")]
        // Tuned so one player strafing alone drags their partner and settles well short of
        // the snap point (~4 m off); two players pulling opposite ways break it (~8 m).
        [SerializeField, Range(1f, 179f)] private float _snapAngle = 26f;   // degrees off opposite: chain breaks
        [SerializeField, Range(0f, 90f)] private float _rehookAngle = 8f;   // degrees off opposite: chain reconnects
        [SerializeField, Min(0f)] private float _springStrength = 2f;       // m/s² per metre off opposite
        [SerializeField, Min(0f)] private float _springDamping = 1.5f;      // m/s² per m/s of relative speed
        [SerializeField, Min(0.5f)] private float _minGap = 3f;             // carts can't pass through each other

        [Header("Spawn")]
        // 0.25 puts cart A a quarter lap in, i.e. at one of the oval's short ends, so the
        // pair starts across the narrow axis with the shortest possible chain.
        [SerializeField, Range(0f, 1f)] private float _startLapFraction = 0.25f;

        private readonly NetworkVariable<bool> _snapped = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private CartSeats _seats;
        private float _velocityA;
        private float _velocityB;
        private bool _placed;

        public RailCart CartA => _cartA;
        public RailCart CartB => _cartB;
        public bool Snapped => _snapped.Value;

        private float RailLength => _cartA.RailLength;
        private float HalfLap => 0.5f * RailLength;

        /// <summary>Metres of arc per degree, so angle settings can be applied in rail space.</summary>
        private float MetresPerDegree => RailLength / 360f;

        public float SnapArc => _snapAngle * MetresPerDegree;

        /// <summary>
        /// How far B is from being exactly opposite A, in rail metres, signed. 0 means
        /// perfectly opposite; positive means B is ahead of that point along the rail.
        /// </summary>
        public float Deviation
        {
            get
            {
                float offset = Mathf.Repeat(_cartB.Distance - _cartA.Distance, RailLength);
                return offset - HalfLap;
            }
        }

        private void Awake() => _seats = GetComponent<CartSeats>();

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            // Done here rather than OnNetworkSpawn so both carts are guaranteed spawned
            // before we write to their NetworkVariables.
            if (!_placed)
            {
                float start = _startLapFraction * RailLength;
                _cartA.SetDistance(start);
                _cartB.SetDistance(start + HalfLap);
                _velocityA = _velocityB = 0f;
                _placed = true;
            }

            float dt = Time.deltaTime;
            Drive(ref _velocityA, _seats.Strafe(0) * _seats.StrafeRailDirection(0), dt);
            Drive(ref _velocityB, _seats.Strafe(1) * _seats.StrafeRailDirection(1), dt);
            ApplyChain(dt);

            _cartA.Advance(_velocityA * dt);
            _cartB.Advance(_velocityB * dt);
            KeepApart();
        }

        private void Drive(ref float velocity, float input, float dt)
        {
            float target = Mathf.Clamp(input, -1f, 1f) * _strafeSpeed;
            velocity += (target - velocity) * _driveGain * dt;
        }

        private void ApplyChain(float dt)
        {
            float deviation = Deviation;
            float off = Mathf.Abs(deviation);

            if (_snapped.Value)
            {
                // A broken chain re-hooks once the pair is nearly opposite again.
                if (off <= _rehookAngle * MetresPerDegree) _snapped.Value = false;
                return;
            }

            if (off > SnapArc)
            {
                _snapped.Value = true;
                return;
            }

            // Pull B back toward opposite and A forward toward it, equally.
            float relativeSpeed = _velocityB - _velocityA;
            float pull = _springStrength * deviation + _springDamping * relativeSpeed;
            _velocityB -= pull * dt;
            _velocityA += pull * dt;
        }

        private void KeepApart()
        {
            float offset = Mathf.Repeat(_cartB.Distance - _cartA.Distance, RailLength);
            if (offset >= _minGap && offset <= RailLength - _minGap) return;

            // Inelastic bump: pin B just outside A on whichever side it's about to cross.
            float pinned = offset < HalfLap ? _minGap : RailLength - _minGap;
            _cartB.SetDistance(_cartA.Distance + pinned);
            _velocityA = _velocityB = 0.5f * (_velocityA + _velocityB);
        }

        public override void OnNetworkDespawn() => _placed = false;
    }
}
