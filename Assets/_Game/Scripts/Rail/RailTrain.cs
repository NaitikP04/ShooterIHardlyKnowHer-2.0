using Unity.Netcode;
using UnityEngine;

namespace SIHKH.Rail
{
    /// <summary>
    /// Server-side driver for the two carts. Owns the relationship between them (who
    /// leads, how far apart they are); the carts themselves only know their own distance.
    /// For now: constant speed, fixed gap. The elastic chain replaces this logic next.
    /// </summary>
    public class RailTrain : NetworkBehaviour
    {
        [SerializeField] private RailCart _leadCart;
        [SerializeField] private RailCart _rearCart;
        [SerializeField, Min(0f)] private float _speed = 4f;   // metres per second
        [SerializeField, Min(1f)] private float _gap = 20f;    // metres of rail between carts

        private bool _placed;

        public RailCart LeadCart => _leadCart;
        public RailCart RearCart => _rearCart;

        /// <summary>Rail metres from rear cart to lead cart, accounting for the loop wrapping.</summary>
        public float Gap => Mathf.Repeat(_leadCart.Distance - _rearCart.Distance, _leadCart.RailLength);

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;

            // Done here rather than OnNetworkSpawn so both carts are guaranteed spawned
            // before we write to their NetworkVariables.
            if (!_placed)
            {
                _rearCart.SetDistance(0f);
                _leadCart.SetDistance(_gap);
                _placed = true;
            }

            float step = _speed * Time.deltaTime;
            _leadCart.Advance(step);
            _rearCart.Advance(step);
        }

        public override void OnNetworkDespawn() => _placed = false;
    }
}
