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

        private void Update()
        {
            if (IsSpawned && IsOwner) DamageNumber.Tick(Time.deltaTime);
        }

        private void OnGUI()
        {
            if (!IsSpawned || !IsOwner) return;

            DrawDamageNumbers();
            DrawCrosshair();

            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.LowerLeft };
            style.normal.textColor = Color.white;
            string hp = _health != null ? $"HP {_health.Current:0}/{_health.Max:0}" : "";
            GUI.Label(new Rect(16, Screen.height - 40, 400, 30), hp, style);

            if (GetComponent<Player.PlayerRig>().Planted)
            {
                var planted = new GUIStyle(style) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                planted.normal.textColor = new Color(1f, 0.6f, 0.2f);
                GUI.Label(new Rect(Screen.width * 0.5f - 100f, Screen.height - 90f, 200f, 30f), "PLANTED (hold S)", planted);
            }

            if (_weapon != null)
            {
                DrawSlots(style);
                DrawInteractPrompt();
            }

            DrawRun();
        }

        private void DrawRun()
        {
            var director = Waves.WaveDirector.Current;
            if (director == null) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            style.normal.textColor = Color.white;

            // Team health bar, top centre.
            float w = 320f, h = 14f, x = Screen.width * 0.5f - w * 0.5f, y = 14f;
            float frac = director.TeamMaxHealth > 0f ? director.TeamHealth / director.TeamMaxHealth : 0f;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.85f, 0.4f), frac);
            GUI.DrawTexture(new Rect(x, y, w * frac, h), Texture2D.whiteTexture);
            GUI.color = prev;

            string line = director.CurrentPhase switch
            {
                Waves.WaveDirector.Phase.Idle => $"Run starts in {director.PhaseSecondsLeft:0}",
                Waves.WaveDirector.Phase.Spawning or Waves.WaveDirector.Phase.Fighting => $"WAVE {director.Wave}   enemies {director.EnemiesAlive}",
                Waves.WaveDirector.Phase.Breather => $"Wave {director.Wave} cleared   next in {director.PhaseSecondsLeft:0}",
                _ => "",
            };
            GUI.Label(new Rect(0, y + h + 4, Screen.width, 28), line, style);

            if (director.CurrentPhase == Waves.WaveDirector.Phase.RunOver)
            {
                var big = new GUIStyle(style) { fontSize = 44 };
                big.normal.textColor = new Color(1f, 0.35f, 0.3f);
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 60), "RUN OVER", big);
                var body = new GUIStyle(style) { fontSize = 22 };
                GUI.Label(new Rect(0, Screen.height * 0.3f + 70, Screen.width, 120),
                    $"Reached wave {director.Wave}\nKills {director.Kills}   Times you shot each other {director.FriendlyFireHits}\n\nHost: press Enter to go again", body);
            }
        }

        private void DrawDamageNumbers()
        {
            Camera cam = GetComponent<Player.PlayerRig>().Camera;
            if (cam == null) return;

            var style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            foreach (var e in DamageNumber.Entries)
            {
                Vector3 screen = cam.WorldToScreenPoint(e.WorldPosition);
                if (screen.z <= 0f) continue;

                // Shrink with distance a little so far hits don't shout, and fade out.
                float distanceScale = Mathf.Clamp(12f / Mathf.Max(screen.z, 1f), 0.6f, 1.4f);
                style.fontSize = Mathf.RoundToInt(22f * e.Scale * distanceScale);
                Color c = e.Color;
                c.a = 1f - Mathf.Clamp01((e.Age - DamageNumber.Lifetime * 0.5f) / (DamageNumber.Lifetime * 0.5f));
                style.normal.textColor = c;

                var rect = new Rect(screen.x - 60f, Screen.height - screen.y - 16f, 120f, 32f);
                GUI.Label(rect, e.Text, style);
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
            // Slot 0 is the default gun; 1..N are one-offs. ">" marks the selection. Keys 1..N+1 or Q.
            int slots = _weapon.OneOffSlots;
            float y = Screen.height - 40f - 26f * (slots + 1);
            for (int slot = 0; slot <= slots; slot++)
            {
                var def = _weapon.SlotDefinition(slot);
                bool selected = slot == _weapon.SelectedSlot;
                string name = def != null ? def.DisplayName : "—";

                // Ammo readout: magazine count + reload bar, or heat bar + state for heat weapons.
                string ammo = "";
                if (slot > 0 && def != null && !def.HasInfiniteAmmo)
                {
                    var item = OneOffWeapon.HeldIn(_weapon.OwnerClientId, slot);
                    if (item != null && def.UsesHeat)
                    {
                        string bar = new string('▮', Mathf.RoundToInt(item.Heat * 10f)).PadRight(10, '▯');
                        string state = item.IsOverheated ? "  OVERHEATED" : item.IsHeatLocked ? "  VENTING" : item.Heat > 0.8f ? "  !" : "";
                        ammo = $"  HEAT {bar}{state}";
                    }
                    else if (item != null)
                    {
                        ammo = item.IsReloading
                            ? "  RELOADING " + new string('▮', Mathf.RoundToInt(item.ReloadProgress * 8f)).PadRight(8, '▯')
                            : $"  {item.Ammo}/{item.Magazine}" + (item.Ammo == 0 ? "  (R)" : "");
                    }
                }

                style.normal.textColor = selected ? Color.yellow : (def != null ? Color.white : new Color(1f, 1f, 1f, 0.4f));
                GUI.Label(new Rect(16, y + 26f * slot, 500, 26), $"{(selected ? ">" : " ")} {slot + 1}  {name}{ammo}", style);
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
