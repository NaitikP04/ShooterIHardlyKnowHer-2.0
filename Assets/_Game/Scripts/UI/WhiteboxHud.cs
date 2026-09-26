using SIHKH.Core;
using SIHKH.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace SIHKH.UI
{
    /// <summary>
    /// Owner-only crosshair, health and weapon name. Deliberately IMGUI and deliberately
    /// ugly: it exists so the whitebox is playable, and gets replaced by a UI Toolkit HUD.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class WhiteboxHud : NetworkBehaviour
    {
        [SerializeField] private float _crosshairSize = 14f;
        [SerializeField] private float _crosshairThickness = 2f;
        [SerializeField] private Color _crosshairColor = Color.white;

        private Health _health;
        private PlayerWeapon _weapon;

        public override void OnNetworkSpawn()
        {
            _health = GetComponent<Health>();
            _weapon = GetComponent<PlayerWeapon>();
        }

        private void OnGUI()
        {
            if (!IsSpawned || !IsOwner) return;

            DrawCrosshair();

            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.LowerLeft };
            style.normal.textColor = Color.white;
            string hp = _health != null ? $"HP {_health.Current:0}/{_health.Max:0}" : "";
            string gun = _weapon != null && _weapon.Current != null ? _weapon.Current.DisplayName : "";
            GUI.Label(new Rect(16, Screen.height - 60, 400, 44), $"{hp}\n{gun}", style);
        }

        private void DrawCrosshair()
        {
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float s = _crosshairSize, t = _crosshairThickness, gap = 4f;
            Color prev = GUI.color;
            GUI.color = _crosshairColor;
            GUI.DrawTexture(new Rect(cx - s, cy - t * 0.5f, s - gap, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + gap, cy - t * 0.5f, s - gap, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - t * 0.5f, cy - s, t, s - gap), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - t * 0.5f, cy + gap, t, s - gap), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
