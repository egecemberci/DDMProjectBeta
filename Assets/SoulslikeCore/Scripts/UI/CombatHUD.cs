using UnityEngine;

// ============================================================================
// PLACEHOLDER COMBAT HUD  —  throwaway IMGUI debug HUD, auto-spawns on Play.
//
// TEAMMATE: this whole script is a placeholder. DELETE it and bind your real UI
// to the DATA SOURCES listed below. Every layout / colour / label knob is hoisted
// to the top of this file (the "PLACEHOLDER UI VALUES" block) so nothing you need
// is buried inside OnGUI.
//
// ── DATA SOURCES (what your UI should read each frame) ──────────────────────
//   PLAYER  — component: PlayerStats, on the object tagged "Player"
//     HP       : PlayerStats.CurrentHP      / PlayerStats.maxHP        -> 0..1 bar
//     Stamina  : PlayerStats.CurrentStamina / PlayerStats.maxStamina   -> 0..1 bar
//     Poise    : PlayerStats.poise          -> flat number (stagger threshold, not a drain)
//   BOSS    — component: BossBrainBase (e.g. KatanaBoss).  Legacy: MimicBoss.
//     HP        : CurrentHP / MaxHP         -> 0..1 bar      (MimicBoss: CurrentHP / maxHP)
//     Poise bar : Poise01                   -> 0..1 bar      (full == poise break / guard break)
//     Flags     : IsDead, IsPoiseBroken, IsAggroed   (MimicBoss exposes IsTired instead of IsPoiseBroken)
//                 show the boss bar only while IsAggroed (engaged) && !IsDead
// ============================================================================
public class CombatHUD : MonoBehaviour
{
    [Header("Debug draw — master toggle for the whole HUD")]
    public bool draw = true;

    // ── PLACEHOLDER UI VALUES (all px unless noted) ─────────────────────────
    [Header("Player panel — anchored bottom-left")]
    public float playerMarginLeft   = 24f;
    public float playerMarginBottom = 96f;
    public float playerBarWidth     = 260f;
    public float playerBarHeight    = 16f;
    public float playerBarSpacing   = 22f;   // vertical gap between HP / STA / poise rows

    [Header("Boss panel — anchored top-centre")]
    public float bossTopY            = 28f;
    public float bossBarWidth        = 520f;
    public float bossHpBarHeight     = 20f;
    public float bossPoiseBarHeight  = 10f;
    public float bossPoiseBarGap     = 3f;    // gap between the HP bar and the poise bar

    [Header("Colours")]
    public Color playerHpColor    = new Color(0.80f, 0.12f, 0.12f);
    public Color playerStamColor  = new Color(0.20f, 0.70f, 0.25f);
    public Color bossHpColor      = new Color(0.55f, 0.05f, 0.55f);
    public Color bossPoiseColor   = new Color(0.95f, 0.55f, 0.15f);
    public Color bossBreakColor   = new Color(1.00f, 0.85f, 0.10f);  // poise-break / tired flash
    public Color barBackColor     = new Color(0f, 0f, 0f, 0.65f);

    [Header("Labels / text")]
    public int    fontSize         = 12;
    public string playerLabel      = "PLAYER";
    public string playerHpLabel    = "HP";
    public string playerStamLabel  = "STA";
    public string playerPoisePrefix = "Poise ";
    public string bossLabel        = "BOSS";
    public string bossBreakLabel   = "BOSS  —  POISE BREAK!";
    public string bossHpLabel      = "HP";
    public string bossPoiseLabel   = "POISE";
    public string mimicLabel       = "MIMIC";
    public string mimicTiredLabel  = "MIMIC  —  TIRED!";

    // ── runtime (placeholder plumbing — none of this carries over) ──────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<CombatHUD>() != null) return;
        if (FindFirstObjectByType<PlayerStats>() == null) return;   // gameplay scenes only
        new GameObject("CombatHUD").AddComponent<CombatHUD>();
    }

    PlayerStats   _player;
    MimicBoss     _boss;
    BossBrainBase _boss2;
    GUIStyle      _label;

    void Refresh()
    {
        if (_player == null) { var p = GameObject.FindWithTag("Player"); if (p) _player = p.GetComponent<PlayerStats>(); }
        if (_boss   == null) _boss  = FindFirstObjectByType<MimicBoss>();
        if (_boss2  == null) _boss2 = FindFirstObjectByType<BossBrainBase>();
    }

    void Update() => Refresh();

    void OnGUI()
    {
        if (!draw) return;
        if (_label == null)
        {
            _label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = fontSize };
            _label.normal.textColor = Color.white;
        }

        // ── PLAYER (bottom-left) ──
        if (_player != null)
        {
            float x = playerMarginLeft, w = playerBarWidth, h = playerBarHeight, y = Screen.height - playerMarginBottom;
            GUI.Label(new Rect(x, y - 20f, w, 20f), playerLabel, _label);
            Bar(new Rect(x, y,                     w, h), _player.CurrentHP      / Mathf.Max(1f, _player.maxHP),      playerHpColor,   playerHpLabel);
            Bar(new Rect(x, y + playerBarSpacing,  w, h), _player.CurrentStamina / Mathf.Max(1f, _player.maxStamina), playerStamColor, playerStamLabel);
            GUI.Label(new Rect(x, y + playerBarSpacing * 2f, w, 20f), playerPoisePrefix + Mathf.RoundToInt(_player.poise), _label);
        }

        // ── BOSS: BossBrainBase (e.g. Katana boss) — top-centre (only once it has aggroed) ──
        if (_boss2 != null && !_boss2.IsDead && _boss2.IsAggroed)
        {
            float w = bossBarWidth, h = bossHpBarHeight, x = (Screen.width - w) * 0.5f, y = bossTopY;
            GUI.Label(new Rect(x, y - 20f, w, 20f), _boss2.IsPoiseBroken ? bossBreakLabel : bossLabel, _label);
            Bar(new Rect(x, y, w, h), _boss2.CurrentHP / Mathf.Max(1f, _boss2.MaxHP), bossHpColor, bossHpLabel);
            Color pc = _boss2.IsPoiseBroken ? bossBreakColor : bossPoiseColor;
            Bar(new Rect(x, y + h + bossPoiseBarGap, w, bossPoiseBarHeight), _boss2.Poise01, pc, bossPoiseLabel);
        }

        // ── BOSS: legacy Mimic — top-centre ──
        if (_boss != null && !_boss.IsDead)
        {
            float w = bossBarWidth, h = bossHpBarHeight, x = (Screen.width - w) * 0.5f, y = bossTopY;
            GUI.Label(new Rect(x, y - 20f, w, 20f), _boss.IsTired ? mimicTiredLabel : mimicLabel, _label);
            Bar(new Rect(x, y, w, h), _boss.CurrentHP / Mathf.Max(1f, _boss.maxHP), bossHpColor, bossHpLabel);
            Color pc = _boss.IsTired ? bossBreakColor : bossPoiseColor;
            Bar(new Rect(x, y + h + bossPoiseBarGap, w, bossPoiseBarHeight), _boss.Poise01, pc, bossPoiseLabel);
        }
    }

    // draws a filled bar (frac 0..1). Placeholder rendering — uses a flat white texture tinted by GUI.color.
    void Bar(Rect r, float frac, Color fill, string label)
    {
        var prev = GUI.color;
        GUI.color = barBackColor;
        GUI.DrawTexture(r, Texture2D.whiteTexture);                              // background
        GUI.color = fill;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), Texture2D.whiteTexture); // fill
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture); // top edge
        GUI.color = prev;
        if (!string.IsNullOrEmpty(label)) GUI.Label(new Rect(r.x + 4f, r.y - 1f, r.width, r.height), label, _label);
    }
}
