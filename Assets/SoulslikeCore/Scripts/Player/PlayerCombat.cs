using UnityEngine;
using System.Collections;

public class PlayerCombat : MonoBehaviour
{
    [Header("Silah Modeli")]
    public Transform weaponBone;
    public GameObject weaponPrefab;
    public Vector3 weaponScale = Vector3.one;
    public Vector3 weaponRotation = Vector3.zero;

    [Header("Silah")]
    public WeaponData currentWeapon;
    public Collider hitbox;
    public Transform weaponTransform;

    // ── Each attack plays its FULL clip, then holds the last frame ──
    [Header("Light Combo — per-attack play time (≈ clip length)")]
    public float lightAttackDuration  = 0.98f;   // light clip
    public float strongAttackDuration = 1.10f;   // strong clip @1.35x
    [Range(0f, 1f)] public float hitFraction = 0.4f;          // when in the swing the hit lands
    [Range(0f, 1f)] public float finisherDamageBlend = 0.5f;  // 0=light,1=strong (0.5=median)

    [Header("End-of-attack hold (pause on last frame)")]
    public int   comboHoldFrames = 30;           // 30 frames ...
    public float comboHoldFps    = 30f;          // ... @30fps = 1.0s continue window

    [Header("Heavy / Spear (single attack)")]
    public float heavyDuration = 0.9f;

    // All hurtboxes are CAPSULES (a tube along forward): Range = forward distance to the centre, Radius = width,
    // Length = tube length (0 = a plain sphere), Height = vertical offset off the root. All values are × player scale.
    [Header("Hitbox — Light (capsule)")]
    public float lightRange   = 1.5f;   // distance
    public float lightRadius  = 1.0f;   // width
    public float lightLength  = 0f;     // tube length
    public float lightHeight  = 0f;     // vertical offset
    [Header("Hitbox — Finisher (capsule)")]
    public float finisherRange  = 2.0f;
    public float finisherRadius = 1.1f;
    public float finisherLength = 0f;
    public float finisherHeight = 0f;
    [Header("Hitbox — Heavy/Spear (capsule)")]
    public float heavyRange  = 1.75f;
    public float heavyRadius = 0.6f;
    public float heavyLength = 0f;
    public float heavyHeight = 1.0f;

    [Header("Debug")]
    public bool drawHitboxGizmos = true;

    PlayerStateMachine _sm;
    PlayerStats        _stats;
    PlayerInputHandler _input;
    PlayerBlock        _block;
    Animator           _anim;
    CharacterController _cc;
    GameObject         _weaponInstance;

    [Header("Heavy attack nudge")]
    public float heavyNudgeDist = 0.2f;   // forward scoot when the strong attack plays
    public float heavyNudgeTime = 0.18f;

    bool _comboActive;
    bool _continueQueued;   // light pressed during a combo -> chain at the hold

    void Awake()
    {
        _sm            = GetComponent<PlayerStateMachine>();
        _stats         = GetComponent<PlayerStats>();
        _input         = GetComponent<PlayerInputHandler>();
        _block         = GetComponent<PlayerBlock>();
        _anim          = GetComponentInChildren<Animator>();
        _cc            = GetComponent<CharacterController>();
        if (hitbox != null) hitbox.enabled = false;
    }

    void Start()
    {
        if (weaponPrefab != null && weaponBone != null)
        {
            _weaponInstance = Instantiate(weaponPrefab, weaponBone);
            _weaponInstance.transform.localPosition = Vector3.zero;
            _weaponInstance.transform.localScale    = weaponScale;
            _weaponInstance.transform.localRotation = Quaternion.Euler(weaponRotation);
        }
    }

    void Update()
    {
        bool light = _input.LightAttackPressed;
        bool heavy = _input.HeavyAttackPressed;

        if (_block != null && _block.IsBlocking) return;   // no attacking while blocking

        if (_comboActive)
        {
            if (light) _continueQueued = true;             // buffer the chain
            return;
        }

        if (!_sm.CanAct()) return;
        if      (light) StartCoroutine(LightCombo());
        else if (heavy) StartCoroutine(HeavySpear());
    }

    // light → (hold) → light → (hold) → strong
    IEnumerator LightCombo()
    {
        if (currentWeapon == null) yield break;
        if (!_stats.UseStamina(currentWeapon.staminaCost)) yield break;
        _comboActive = true; _continueQueued = false;

        yield return Swing("LightAttack", PlayerState.LightAttacking, lightAttackDuration,
                           lightRange, lightRadius, lightLength, lightHeight, currentWeapon.damage, currentWeapon.poise);
        yield return Hold();
        if (!_continueQueued || !_stats.UseStamina(currentWeapon.staminaCost)) { EndCombo(); yield break; }

        _continueQueued = false;
        yield return Swing("LightAttack", PlayerState.LightAttacking, lightAttackDuration,
                           lightRange, lightRadius, lightLength, lightHeight, currentWeapon.damage, currentWeapon.poise);
        yield return Hold();
        if (!_continueQueued || !_stats.UseStamina(currentWeapon.heavyStaminaCost)) { EndCombo(); yield break; }

        _continueQueued = false;
        float dmg = Mathf.Lerp(currentWeapon.damage, currentWeapon.heavyDamage, finisherDamageBlend);
        yield return Swing("Finisher", PlayerState.HeavyAttacking, strongAttackDuration,
                           finisherRange, finisherRadius, finisherLength, finisherHeight, dmg, currentWeapon.poise * 1.5f);
        EndCombo();   // strong has no hold — combo ends
    }

    IEnumerator HeavySpear()
    {
        if (currentWeapon == null) yield break;
        if (!_stats.UseStamina(currentWeapon.heavyStaminaCost)) yield break;
        _comboActive = true; _continueQueued = false;
        _sm.ChangeState(PlayerState.HeavyAttacking);   // set state BEFORE the nudge, or its own guard bails it on frame 1
        StartCoroutine(NudgeForward(heavyNudgeDist, heavyNudgeTime));
        yield return Swing("HeavyAttack", PlayerState.HeavyAttacking, heavyDuration,
                           heavyRange, heavyRadius, heavyLength, heavyHeight, currentWeapon.heavyDamage, currentWeapon.poise * 2.5f);   // capsule
        EndCombo();
    }

    IEnumerator NudgeForward(float dist, float time)   // small forward scoot during the strong attack
    {
        if (_cc == null || dist <= 0f) yield break;
        Vector3 dir = transform.forward; dir.y = 0f; dir.Normalize();
        float t = 0f;
        while (t < time)
        {
            if (_sm.CurrentState != PlayerState.HeavyAttacking) yield break;   // cancelled
            if (_cc.enabled) _cc.Move(dir * (dist / time) * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
    }

    // plays one attack's full clip, deals its capsule hit partway through
    IEnumerator Swing(string trig, PlayerState st, float dur, float range, float radius, float length, float height,
                      float dmg, float poise)
    {
        _sm.ChangeState(st);
        if (_anim != null) { _anim.speed = 1f; _anim.SetTrigger(trig); }
        float t = 0f; bool dealt = false;
        while (t < dur)
        {
            if (_sm.CurrentState == PlayerState.Dodging || _sm.CurrentState == PlayerState.Blocking) yield break; // cancelled
            if (!dealt && t >= dur * hitFraction)
            {
                DealCapsule(range, radius, length, height, dmg, poise);
                dealt = true;
            }
            t += Time.deltaTime;
            yield return null;
        }
    }

    // keep the continue window open WITHOUT freezing — the swing settles back to idle/locomotion on its own
    IEnumerator Hold()
    {
        if (_sm.CurrentState == PlayerState.Dodging || _sm.CurrentState == PlayerState.Blocking)
        { if (_anim != null) _anim.speed = 1f; yield break; }

        _sm.ChangeState(PlayerState.Idle);                 // accept the continue input
        if (_anim != null) _anim.speed = 1f;               // let the swing resolve instead of holding a frozen frame
        float hold = comboHoldFrames / Mathf.Max(1f, comboHoldFps);
        float t = 0f;
        while (t < hold)
        {
            if (_continueQueued) break;                    // chain now
            if (_sm.CurrentState == PlayerState.Dodging || _sm.CurrentState == PlayerState.Blocking
                || (_block != null && _block.IsBlocking)) { _continueQueued = false; break; }
            t += Time.deltaTime;
            yield return null;
        }
        if (_anim != null) _anim.speed = 1f;
    }

    void EndCombo()
    {
        _comboActive = false; _continueQueued = false;
        if (_anim != null) _anim.speed = 1f;
        if (_sm.IsAttacking()) _sm.ChangeState(PlayerState.Idle);
    }

    // capsule hurtbox: a `length`-long tube of `radius`, centred `range` m in front along forward (all × player scale).
    // length 0 collapses to a sphere — identical to the old light/finisher behaviour.
    void DealCapsule(float range, float radius, float length, float height, float dmg, float poise)
    {
        float s = transform.lossyScale.x;
        Vector3 center = transform.position + transform.forward * range * s + Vector3.up * height * s;
        Vector3 half   = transform.forward * (length * 0.5f * s);
        foreach (var h in Physics.OverlapCapsule(center - half, center + half, radius * s))
        {
            if (!h.CompareTag("Enemy")) continue;
            if (h.TryGetComponent<IDamageable>(out var t))
                t.TakeDamage(dmg, poise, transform.position);
        }
    }

    // Animator-event stubs (so clip events don't error) + death lockout
    public void EnableHitbox()  { }
    public void DisableHitbox() { }
    public void OnAttackEnd()   { if (_sm != null) _sm.ChangeState(PlayerState.Idle); }

    public void DisableCombat()
    {
        StopAllCoroutines();
        _comboActive = false; _continueQueued = false;
        if (_anim != null) _anim.speed = 1f;
        if (hitbox != null) hitbox.enabled = false;
        enabled = false;
    }

    void DrawHitCapsule(float range, float radius, float length, float height, Color c)
    {
        float s = transform.lossyScale.x;
        Gizmos.color = c;
        Vector3 center = transform.position + transform.forward * range * s + Vector3.up * height * s;
        Vector3 half   = transform.forward * (length * 0.5f * s);
        Vector3 p0 = center - half, p1 = center + half;
        Gizmos.DrawWireSphere(p0, radius * s);
        if (length > 0.001f)
        {
            Gizmos.DrawWireSphere(p1, radius * s);
            Vector3 r = transform.right * radius * s, u = transform.up * radius * s;
            Gizmos.DrawLine(p0 + r, p1 + r); Gizmos.DrawLine(p0 - r, p1 - r);
            Gizmos.DrawLine(p0 + u, p1 + u); Gizmos.DrawLine(p0 - u, p1 - u);
        }
    }

    void OnDrawGizmos()
    {
        if (!drawHitboxGizmos) return;
        DrawHitCapsule(lightRange,    lightRadius,    lightLength,    lightHeight,    new Color(1f, 0.9f, 0.1f, 0.7f));
        DrawHitCapsule(finisherRange, finisherRadius, finisherLength, finisherHeight, new Color(1f, 0.5f, 0f, 0.8f));
        DrawHitCapsule(heavyRange,    heavyRadius,    heavyLength,    heavyHeight,    new Color(1f, 0.15f, 0.15f, 0.9f));
    }
}
