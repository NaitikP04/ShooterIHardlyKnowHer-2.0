using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Rail
{
    /// <summary>
    /// Server-side physics for the two carts on the oval rail, all in one dimension:
    /// metres along the rail. Think of the oval's two long sides as two parallel rails
    /// with the players facing each other across the gap. The chain wants your partner
    /// directly across from you: slide along your side and it drags them along theirs in
    /// the same world direction; run opposite ways and it stretches diagonally until it
    /// snaps. Clients never run any of this; they read the carts' distances and the
    /// snapped flag.
    /// </summary>
    public class CartPair : NetworkBehaviour
    {
        [Header("Carts")]
        [SerializeField] private RailCart _cartA;
        [SerializeField] private RailCart _cartB;

        [Header("Strafing")]
        [SerializeField, Min(0f)] private float _strafeSpeed = 4f;    // m/s along the rail at full input
        // Drive force grows with how far you are from your target speed, so two players
        // pulling apart strain the chain twice as hard as one player alone. One player
        // alone settles at strafeSpeed * driveGain / (2 * springStrength) metres of stretch.
        [SerializeField, Min(0.1f)] private float _driveGain = 4f;    // m/s² per m/s of speed error

        [Header("Chain")]
        [SerializeField, Min(1f)] private float _snapStretch = 14f;     // rail metres off "directly across": chain breaks
        [SerializeField, Min(0f)] private float _rehookStretch = 3f;    // rail metres off: broken chain reconnects
        [SerializeField, Min(0f)] private float _springStrength = 1f;   // m/s² per metre of stretch
        [SerializeField, Min(0f)] private float _springDamping = 1f;    // m/s² per m/s of stretch rate
        [SerializeField, Min(0.5f)] private float _minGap = 3f;         // rail metres; carts can't pass through each other

        [Header("Spawn")]
        // 0.25 puts cart A a quarter lap in, i.e. at one of the oval's short ends, so the
        // pair starts directly across the narrow axis.
        [SerializeField, Range(0f, 1f)] private float _startLapFraction = 0.25f;

        private readonly NetworkVariable<bool> _snapped = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private CartSeats _seats;
        private float _velocityA;
        private float _velocityB;
        private bool _placed;

        public RailCart CartA => _cartA;
        public RailCart CartB => _cartB;
        public float SnapStretch => _snapStretch;
        public bool Snapped => _snapped.Value;

        private float RailLength => _cartA.RailLength;

        /// <summary>
        /// How far B is from the point directly across from A, in rail metres, signed.
        /// The rail runs one way along the top side and the other way along the bottom,
        /// so "directly across" from rail position s is rail position L - s.
        /// </summary>
        public float Stretch
        {
            get
            {
                float across = RailLength - _cartA.Distance;
                float d = Mathf.Repeat(_cartB.Distance - across, RailLength);
                return d > 0.5f * RailLength ? d - RailLength : d;
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
                _cartB.SetDistance(RailLength - start);
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
            float stretch = Stretch;
            float off = Mathf.Abs(stretch);

            if (_snapped.Value)
            {
                // A broken chain re-hooks once the pair is nearly across from each other.
                if (off <= _rehookStretch) _snapped.Value = false;
                return;
            }

            if (off > _snapStretch)
            {
                _snapped.Value = true;
                return;
            }

            // Stretch = B + A - L, so it grows with both velocities; pulling both carts
            // backward along their rails by the same amount shrinks it symmetrically.
            float stretchRate = _velocityA + _velocityB;
            float pull = _springStrength * stretch + _springDamping * stretchRate;
            _velocityA -= pull * dt;
            _velocityB -= pull * dt;
        }

        private void KeepApart()
        {
            float offset = Mathf.Repeat(_cartB.Distance - _cartA.Distance, RailLength);
            if (offset >= _minGap && offset <= RailLength - _minGap) return;

            // Inelastic bump: pin B just outside A on whichever side it's about to cross.
            float pinned = offset < 0.5f * RailLength ? _minGap : RailLength - _minGap;
            _cartB.SetDistance(_cartA.Distance + pinned);
            _velocityA = _velocityB = 0.5f * (_velocityA + _velocityB);
        }

        public override void OnNetworkDespawn() => _placed = false;
    }
}
