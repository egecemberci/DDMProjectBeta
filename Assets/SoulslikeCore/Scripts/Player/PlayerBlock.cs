using UnityEngine;
using System.Collections;

public class PlayerBlock : MonoBehaviour
{
    [Header("Block")]
    public float blockMinStamina    = 10f;    // need > this to (re)raise the guard
    public float blockStaminaPerHit = 2.0f;   // blocked HP damage ×2 -> stamina
    public string guardStartState   = "1071_women_OnehandSW_guard_Start";

    [Header("Parry (the first moments of a block)")]
    public float parryWindow      = 0.15f;  // a hit blocked within this long after STARTING a block = a parry
    public float parryPoiseDamage = 15f;    // poise dealt to the staggered enemy on a parry (no HP damage)
    public float parryFindRadius  = 1.5f;   // search radius around the attacker's position to find the enemy entity

    [Header("Knockback")]
    public float knockback     = 0.15f;       // normal nudge per blocked hit
    public float knockbackTime = 0.12f;
    public LayerMask wallMask  = ~0;          // surfaces the knockback won't shove you into

    [Header("Stance break (stamina runs out)")]
    public float stanceBreakKnockback = 1.25f;
    public float stanceBreakTime      = 2.0f;    // locked (no block / no dodge) for this long after a stance break
    public float stanceBreakHurtSpeed = 0.75f;   // hurt anim plays at this speed
    [Range(0f,1f)] public float stanceBreakRegenTo = 0.75f; // regen to this fraction by the end

    public bool IsBlocking     { get; private set; }
    public bool IsBlockStunned { get; private set; }
    public bool IsStanceBroken { get; private set; }

    PlayerStateMachine  _sm;
    PlayerStats         _stats;
    PlayerInputHandler  _input;
    CharacterController _cc;
    Animator            _anim;
    float               _parryTimer;           // counts down the parry window after a block starts
    bool                _lockedUntilRelease;   // after a break, must release + repress to block again

    void Awake()
    {
        _sm    = GetComponent<PlayerStateMachine>();
        _stats = GetComponent<PlayerStats>();
        _input = GetComponent<PlayerInputHandler>();
        _cc    = GetComponent<CharacterController>();
        _anim  = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (IsStanceBroken) return;                                                  // fully locked during a break
        if (_sm.CurrentState == PlayerState.Dead)    { if (IsBlocking) StopBlock(); return; }
        if (_sm.CurrentState == PlayerState.Dodging) { if (IsBlocking) StopBlock(); return; }

        if (!_input.BlockHeld) _lockedUntilRelease = false;                          // released -> may block again

        // can't raise the guard mid-attack — block no longer interrupts an attack animation
        bool wantBlock = _input.BlockHeld && !_lockedUntilRelease && _stats.HasStamina(blockMinStamina) && !_sm.IsAttacking();
        if (wantBlock) { if (!IsBlocking) StartBlock(); }                            // holding block costs NO stamina
        else if (IsBlocking && !IsBlockStunned) StopBlock();

        if (_parryTimer > 0f) _parryTimer -= Time.deltaTime;
    }

    void StartBlock()
    {
        IsBlocking = true; _parryTimer = parryWindow;   // starting a block opens the parry window

        _sm.ChangeState(PlayerState.Blocking);
        if (_anim != null) { _anim.speed = 1f; _anim.SetBool("IsBlocking", true); _anim.Play(guardStartState, 0, 0f); }
    }

    void StopBlock()
    {
        IsBlocking = false;
        if (_sm.CurrentState == PlayerState.Blocking) _sm.ChangeState(PlayerState.Idle);
        if (_anim != null) _anim.SetBool("IsBlocking", false);
    }

    public void OnBlockedHit(float damage, Vector3 attackerPos)
    {
        // PARRY — only if our parry window is open AND the attacker is in ITS parryable window (e.g. the early swing frames)
        if (_parryTimer > 0f && TryParry(attackerPos))   // free block (no HP, no stamina) + stagger the attacker
            return;

        _stats.DrainStamina(damage * blockStaminaPerHit);
        Vector3 away = transform.position - attackerPos; away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        away.Normalize();

        if (!_stats.HasStamina(1f))   // STANCE BREAK
        {
            StopBlock();
            _lockedUntilRelease = true;
            StartCoroutine(StanceBreakRoutine(away));
        }
        else
        {
            StartCoroutine(KnockbackRoutine(away, knockback, knockbackTime));
        }
    }

    // find the enemy entity that just hit us (it passed its own world position). It can only be PARRIED while it
    // is in its own parryable window (CanBeParried) — outside that, this returns false and the hit is a normal block.
    bool TryParry(Vector3 attackerPos)
    {
        IStaggerable best = null; float bestSqr = float.MaxValue;
        foreach (var c in Physics.OverlapSphere(attackerPos, parryFindRadius))
        {
            var st = c.GetComponentInParent<IStaggerable>();
            if (st == null || !st.CanBeParried) continue;   // attacker not in its parryable window -> can't parry it
            float sq = (c.transform.position - attackerPos).sqrMagnitude;
            if (sq < bestSqr) { bestSqr = sq; best = st; }
        }
        if (best != null) { best.Stagger(parryPoiseDamage); return true; }
        return false;
    }

    IEnumerator StanceBreakRoutine(Vector3 dir)
    {
        IsStanceBroken = true;
        _sm.ChangeState(PlayerState.Stunned);                                       // stuck in idle, can't act
        if (_anim != null) { _anim.speed = stanceBreakHurtSpeed; _anim.SetTrigger("Hit"); }
        _stats.BoostRegen(stanceBreakTime, _stats.maxStamina * stanceBreakRegenTo / Mathf.Max(0.1f, stanceBreakTime));

        float knockDur = Mathf.Min(0.3f, stanceBreakTime);
        float allowed  = ClampToWall(dir, stanceBreakKnockback);
        float t = 0f;
        while (t < stanceBreakTime)
        {
            if (t < knockDur && _cc != null && _cc.enabled) _cc.Move(dir * (allowed / knockDur) * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
        if (_anim != null) _anim.speed = 1f;
        IsStanceBroken = false;
        if (_sm.CurrentState == PlayerState.Stunned) _sm.ChangeState(PlayerState.Idle);
    }

    IEnumerator KnockbackRoutine(Vector3 dir, float dist, float time)
    {
        IsBlockStunned = true;
        float allowed = ClampToWall(dir, dist);
        float t = 0f;
        while (t < time)
        {
            if (_cc != null && _cc.enabled) _cc.Move(dir * (allowed / time) * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }
        IsBlockStunned = false;
    }

    // limits a shove so it can't push you into a wall
    float ClampToWall(Vector3 dir, float dist)
    {
        float radius   = _cc != null ? _cc.radius : 0.3f;
        Vector3 origin = transform.position + Vector3.up * (radius + 0.1f) + dir * (radius + 0.05f);
        if (Physics.SphereCast(origin, radius * 0.8f, dir, out var hit, dist, wallMask, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(transform)) return Mathf.Max(0f, hit.distance);
        return dist;
    }
}

// Implemented by enemies so a player PARRY can stagger them: take `poiseDamage` (no HP), play their
// hurt/stagger animation, and go deaf to their own logic for that animation's full duration (uninterruptible).
public interface IStaggerable
{
    bool CanBeParried { get; }   // true only while the attacker's hit can be parried (e.g. the early frames of a swing)
    void Stagger(float poiseDamage);
}
