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
            GUI.Label(new Rect(16, Screen.height - 40, 400, 30), hp, style);

            if (_weapon != null)
            {
                DrawSlots(style);
                DrawInteractPrompt();
            }
        }

        private void DrawInteractPrompt()
        {
            OneOffWeapon item = _weapon.PromptTarget(out string action);
            Camera cam = GetComponent<Player.PlayerRig>().Camera;
            if (item == null || cam == null) return;

            Vector3 screen = cam.WorldToScreenPoint(item.transform.position + Vector3.up * 0.4f);
            if (screen.z <= 0f) return; // behind the camera

            var style = new GUIStyle(GUI.skin.box) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = action == "Catch!" ? Color.yellow : Color.white;
            var rect = new Rect(screen.x - 70f, Screen.height - screen.y - 50f, 140f, 34f);
            GUI.Box(rect, $"[E] {action}", style);
        }

        private void DrawSlots(GUIStyle style)
        {
            // Slot 0 is the default gun; 1..N are one-offs. ">" marks the selection. Q cycles.
            int slots = _weapon.OneOffSlots;
            float y = Screen.height - 40f - 26f * (slots + 1);
            for (int slot = 0; slot <= slots; slot++)
            {
                var def = _weapon.SlotDefinition(slot);
                bool selected = slot == _weapon.SelectedSlot;
                string name = def != null ? def.DisplayName : "—";
                style.normal.textColor = selected ? Color.yellow : (def != null ? Color.white : new Color(1f, 1f, 1f, 0.4f));
                GUI.Label(new Rect(16, y + 26f * slot, 400, 26), $"{(selected ? ">" : " ")} {slot + 1}  {name}", style);
            }
            style.normal.textColor = Color.white;
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
