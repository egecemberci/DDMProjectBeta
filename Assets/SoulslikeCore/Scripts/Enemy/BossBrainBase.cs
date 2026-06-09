using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// ─────────────────────────────────────────────────────────────────────────────
// BossBrainBase — boss chassis for the Katana boss.
// Holds the animation-agnostic systems; a concrete subclass binds the anim-state
// names (inspector) and supplies its moveset via MeleeAttack()/OpeningAttack().
// State switches are INSTANT (0-duration anim transitions), driven by anim.Play /
// SetTrigger so it works on any rig.
//
// Defence model: incoming damage is split 15% HP / 85% poise bar. When the poise
// bar fills it BREAKS -> a window where the boss can only shamble (100% dmg -> HP).
// Below blockHpThreshold the boss is "tired": it perfectly deflects the player's
// heavy attacks (drains their stamina) and all poise dealt to it is doubled.
// ─────────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(NavMeshAgent))]
public abstract class BossBrainBase : MonoBehaviour, IDamageable, IStaggerable
{
    [Header("Refs (auto-found if empty)")]
    public Transform player;
    public Animator  anim;

    [Header("Stats")]
    public float maxHP       = 400f;
    public float poise       = 90f;     // poise damage the boss deals to the PLAYER (×0.5 per swing)
    public float maxStamina  = 100f;    // INVISIBLE — gates attacks
    public float staminaRegen = 15f;

    [Header("Movement")]
    public float moveSpeed    = 4.5f;
    public float rotationSpeed = 14f;

    [Header("Ranges (metres)")]
    public float meleeRange = 1.0f;
    public float farRange   = 3.0f;
    public float aggroRange = 12f;     // engages when the player FIRST comes within this; never de-aggros until the player dies

    [Header("Poise bar (VISIBLE — fills from damage, resets on break)")]
    public float blockMeterMax = 150f;
    [Range(0f,1f)] public float poiseToHpSplit = 0.15f;  // fraction of damage dealt to HP (rest -> poise bar)
    public float poiseBreakWindow = 5f;                  // open window after a poise break (boss can move, can't act)

    [Header("Dodge")]
    public float forwardNudge   = 0.5f;
    public float dodgeDist      = 1.5f;
    public float dodgeTime      = 0.385f;
    public float dodgeAnimSpeed = 0.65f;
    public float thinkDodgeDist = 2.5f;                  // backward "think dodge" before committing to an attack
    public float damageDodgeDist     = 1f;               // backward dodge whenever the boss takes damage
    public float damageDodgeCooldown = 3f;               // min gap between damage-dodges

    [Header("Parry window (boss-side)")]
    public float parryableDelay  = 0.1f;                 // first ~6 frames (@60fps) of a SWING — too early, NOT parryable
    public float parryableWindow = 0.167f;               // then the next ~10 frames ARE parryable; after that, not

    [Header("Tired / deflect (below blockHpThreshold)")]
    [Range(0f,1f)] public float blockHpThreshold = 0.35f; // at/below this HP fraction the boss perfect-deflects heavies
    public float lowHpThinkDodgeDist = 0.25f;            // tired: think-dodge shrinks
    public float lowHpPoiseMult      = 2f;               // ALL poise dealt to the boss ×2 when tired
    [Range(0f,1f)] public float deflectPoiseFraction = 0.10f; // blocked heavy: this fraction of dmg -> poise (×lowHpPoiseMult)
    public float deflectStaminaDamage = 86f;             // stamina deflected back to the player on a perfect block

    [Header("Anim state names")]
    public string guardState     = "Guard";
    public string dodgeBackState = "DodgeBack";
    public string staggerState   = "Hit";     // hurt/stagger clip played when parried
    public string battleStartState = "BattleStart";
    public string battleEndState   = "BattleEnd";   // played once when the player dies (de-aggro)
    public string deathState       = "Down";
    public float  introWalkSpeed   = 2.0f;

    [Header("Hitbox / intake")]
    public float hitRadius   = 0.3f;
    public float hitCooldown = 0.3f;
    public bool  drawGizmos  = true;

    // ── runtime ──
    protected NavMeshAgent _agent;
    protected PlayerStateMachine _playerSM;
    PlayerBlock _playerBlock;
    PlayerDodge _playerDodge;
    protected float _hp, _stamina;
    float _invulnUntil, _hurtUntil, _blockMeter, _damageDodgeReadyAt;
    protected bool _dead, _busy, _blocking, _introDone, _inRecover, _dodging, _recoverHit, _poiseBroken, _deflecting, _staggered, _wantDamageDodge, _parryable, _aggroed;

    public float CurrentHP => _hp;
    public float MaxHP      => maxHP;
    public bool  IsDead     => _dead;
    public bool  IsPoiseBroken => _poiseBroken;
    public bool  IsStaggered   => _staggered;
    public bool  IsAggroed     => _aggroed;     // HUD shows the boss bar only once engaged
    public bool  CanBeParried  => _parryable;   // IStaggerable — only true during the early "parryable" frames of a swing
    public float Poise01    => Mathf.Clamp01(_blockMeter / Mathf.Max(1f, blockMeterMax));

    // ── moveset hooks (subclass fills these) ──
    protected abstract IEnumerator MeleeAttack();
    protected virtual  IEnumerator OpeningAttack() { yield return MeleeAttack(); }

    protected virtual void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (anim == null)   anim = GetComponentInChildren<Animator>();
        if (anim != null)   anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p) player = p.transform; }
        if (player != null) { _playerSM = player.GetComponent<PlayerStateMachine>(); _playerBlock = player.GetComponent<PlayerBlock>(); _playerDodge = player.GetComponent<PlayerDodge>(); }
        _hp = maxHP; _stamina = maxStamina;
        if (_agent != null) { _agent.updateRotation = false; _agent.speed = moveSpeed; _agent.stoppingDistance = meleeRange * 0.85f; }
    }

    protected virtual void OnEnable() { StartCoroutine(BossBrain()); }

    protected virtual void Update()
    {
        if (_dead || player == null) return;
        if (_staggered) return;                 // deaf during a parry-stagger
        if (_aggroed) FacePlayer();             // don't track the player until engaged
        _stamina = Mathf.Min(maxStamina, _stamina + staminaRegen * Time.deltaTime);

        // TIRED DEFLECT — below blockHpThreshold, instantly read the player's heavy attack and perfect-block it
        if (_hp <= maxHP * blockHpThreshold && _playerSM != null && !_poiseBroken && !_dodging)
        {
            bool playerHeavy = _playerSM.CurrentState == PlayerState.HeavyAttacking;
            if (playerHeavy && !_deflecting) { _deflecting = true; SetGuard(true); }
            else if (!playerHeavy && _deflecting) { _deflecting = false; SetGuard(false); }
        }
        else if (_deflecting && !_dodging) { _deflecting = false; SetGuard(false); }
    }

    // ───────── brain ─────────
    IEnumerator BossBrain()
    {
        yield return null;
        while (!_dead)
        {
            if (player == null) { yield return null; continue; }

            // player dead -> INSTANTLY de-aggro: play the battle-end once, then idle (re-arms for the next aggro)
            if (_playerSM != null && _playerSM.CurrentState == PlayerState.Dead)
            {
                if (_aggroed)
                {
                    _aggroed = false; _introDone = false;
                    SetGuard(false);
                    if (_agent.isOnNavMesh) _agent.ResetPath();
                    if (anim != null) { anim.SetBool("IsRunning", false); anim.SetFloat("Speed", 0f); anim.speed = 1f; anim.Play(battleEndState, 0, 0f); }
                }
                yield return null; continue;
            }

            // passive until the player FIRST comes within aggroRange; once aggroed it never drops (only player death, above)
            if (!_aggroed)
            {
                SetGuard(false);
                if (_agent.isOnNavMesh) _agent.ResetPath();
                if (anim != null) { anim.SetBool("IsRunning", false); anim.SetFloat("Speed", 0f); }
                if (Dist() <= aggroRange) _aggroed = true;
                else { yield return null; continue; }
            }

            if (!_introDone) { yield return IntroSequence(); _introDone = true; continue; }

            // parry-staggered — deaf, do nothing until the stagger anim finishes
            if (_staggered) { yield return null; continue; }

            // POISE-BREAK window — can shamble toward the player but cannot attack/dodge/block
            if (_poiseBroken)
            {
                SetGuard(false);
                _agent.speed = moveSpeed * 0.5f;
                if (_agent.isOnNavMesh) _agent.SetDestination(player.position);
                UpdateLocomotionAnim();
                yield return null; continue;
            }

            // committed to a tired perfect-block — hold it, do nothing else
            if (_deflecting) { if (_agent.isOnNavMesh) _agent.ResetPath(); yield return null; continue; }

            // damage-reaction dodge — step back 1m whenever the boss is hit (3s cooldown), interrupting its current move
            if (_wantDamageDodge) { _wantDamageDodge = false; yield return DamageDodge(); continue; }

            // approach -> walk in (no sprint)
            if (Dist() > meleeRange)
            {
                SetGuard(false);
                _agent.speed = moveSpeed;
                if (_agent.isOnNavMesh) _agent.SetDestination(player.position);
                UpdateLocomotionAnim();
                yield return null; continue;
            }

            // in melee -> run the attack cycle (subclass owns think-dodge / A-B / punish / break-dodge)
            yield return MeleeAttack();
        }
    }

    IEnumerator IntroSequence()
    {
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        if (anim != null) { anim.speed = 1f; anim.Play(battleStartState, 0, 0f); }
        yield return Wait(1f);
        float t = 0f;
        while (Dist() > farRange && t < 8f && !_dead)
        {
            _agent.speed = introWalkSpeed;
            if (_agent.isOnNavMesh) _agent.SetDestination(player.position);
            UpdateLocomotionAnim();
            t += Time.deltaTime; yield return null;
        }
        _agent.speed = moveSpeed;
        if (_agent.isOnNavMesh) _agent.ResetPath();
        if (!_dead) yield return OpeningAttack();
    }

    // punish window after an attack — fully vulnerable; a hit ends it early. The attack's trailing
    // ("stuck") clip frames play out here.
    protected IEnumerator Recover(float dur)
    {
        _inRecover = true; _recoverHit = false;
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        if (anim != null) { anim.SetBool("IsRunning", false); anim.SetFloat("Speed", 0f); }
        float t = 0f;
        while (t < dur && !_dead && !_recoverHit && !_poiseBroken && !_deflecting && !_staggered && !_wantDamageDodge) { t += Time.deltaTime; yield return null; }
        _inRecover = false;
    }

    protected void SetGuard(bool on)
    {
        if (!on && _deflecting) return;   // tired perfect-block is UNINTERRUPTIBLE — nothing can lower it mid-deflect
        if (on && !_blocking && anim != null) anim.CrossFadeInFixedTime(guardState, 0.12f, 0);
        _blocking = on;
        if (anim != null) anim.SetBool("IsBlocking", on);
    }

    // poise bar full -> open window: boss can move but cannot attack/dodge/block; 100% dmg -> HP; then bar resets
    IEnumerator PoiseBreak()
    {
        _poiseBroken = true;
        _busy = false;
        SetGuard(false);
        if (anim != null) { anim.speed = 1f; anim.SetTrigger("Hit"); }
        _blockMeter = blockMeterMax;                 // stays full through the window
        yield return Wait(poiseBreakWindow);
        _blockMeter = 0f;                            // then resets
        _poiseBroken = false;
    }

    // ── IStaggerable: PARRIED — take poise (no HP), play the hurt anim, and go deaf for its full duration ──
    public void Stagger(float poiseDamage)
    {
        if (_dead || _staggered) return;
        _blockMeter = Mathf.Min(blockMeterMax, _blockMeter + poiseDamage);   // parry poise (no HP)
        StartCoroutine(StaggerRoutine());
    }

    IEnumerator StaggerRoutine()
    {
        _staggered = true;
        _busy = false; _deflecting = false; _inRecover = false;
        SetGuard(false);
        if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
        float len = 0.5f;
        if (anim != null)
        {
            anim.speed = 1f;
            anim.Play(staggerState, 0, 0f);
            anim.Update(0f);                                    // force the state so its length reads correctly
            float l = anim.GetCurrentAnimatorStateInfo(0).length;
            if (l > 0.01f) len = l;
        }
        yield return new WaitForSeconds(len);                   // deaf for exactly the stagger anim's duration
        _staggered = false;
    }

    // ───────── dodges ─────────
    // reactive backward dodge fired whenever the boss takes damage (damageDodgeDist, 3s cooldown)
    protected IEnumerator DamageDodge()
    {
        Vector3 away = transform.position - player.position; away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        yield return DoDodge(away.normalized, dodgeBackState, damageDodgeDist);
    }

    // special "think dodge" — bigger backward step (shrinks when tired), before committing to an attack
    protected IEnumerator ThinkDodge()
    {
        float dist = (_hp <= maxHP * blockHpThreshold) ? lowHpThinkDodgeDist : thinkDodgeDist;
        Vector3 away = transform.position - player.position; away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        yield return DoDodge(away.normalized, dodgeBackState, dist);
    }

    IEnumerator DoDodge(Vector3 dir, string state, float dist)
    {
        if (_deflecting || _staggered) yield break;   // never dodge while committed to a perfect block / staggered
        _busy = true; _dodging = true; SetGuard(false);
        _invulnUntil = Time.time + dodgeTime;          // i-frames for the whole dodge
        PlayDodge(state);
        yield return MoveOver(dir, dist, dodgeTime);
        if (anim != null) anim.speed = 1f;
        _dodging = false; _busy = false;
    }

    void PlayDodge(string state) { if (anim != null) { anim.speed = dodgeAnimSpeed; anim.Play(state, 0, 0f); } }

    protected IEnumerator MoveOver(Vector3 dir, float dist, float time)
    {
        float t = 0f;
        while (t < time) { if (_agent != null && _agent.isOnNavMesh) _agent.Move(dir * (dist / time) * Time.deltaTime); t += Time.deltaTime; yield return null; }
    }

    // ───────── swing helper for subclass movesets ─────────
    // Phased swing: clip plays windup -> swing -> stuck. The hurtbox is LIVE only during [swingStart, swingEnd]
    // seconds — never during the windup. If the player reacts (blocks OR i-frames) on ANY frame the hurtbox is on
    // them it resolves (a LATE block still drains stamina); if they never react, the hit lands at the window's end.
    protected IEnumerator Swing(string trig, float dmg, float reach, float nudge, float swingStart, float swingEnd, float staCost = 8f)
    {
        if (_poiseBroken || _deflecting || _staggered) yield break;
        if (anim != null) anim.SetTrigger(trig);
        _stamina = Mathf.Max(0, _stamina - staCost);
        float swingDur = Mathf.Max(0.01f, swingEnd - swingStart);
        float t = 0f; bool resolved = false;
        while (t < swingEnd)
        {
            if (_poiseBroken || _deflecting || _staggered || _wantDamageDodge) { _parryable = false; yield break; }
            if (t >= swingStart)                       // SWING phase — hurtbox live the whole window + lunge
            {
                float ps = t - swingStart;                          // time into the swing section
                _parryable = ps >= parryableDelay && ps < parryableDelay + parryableWindow;   // parryable only in the 2nd ~10-frame slice
                if (nudge > 0f && _agent != null && _agent.isOnNavMesh)
                    _agent.Move(transform.forward * (nudge / swingDur) * Time.deltaTime);
                if (!resolved && PlayerInReach(reach))
                {
                    bool blocking = _playerBlock != null && _playerBlock.IsBlocking;
                    bool iframe   = _playerDodge != null && _playerDodge.IsInvincible;
                    if (blocking || iframe) { MeleeHitR(dmg, poise * 0.5f, reach); resolved = true; }
                }
            }
            t += Time.deltaTime;
            yield return null;
        }
        _parryable = false;                                   // window over — late hit is a normal block, not a parry
        if (!resolved) MeleeHitR(dmg, poise * 0.5f, reach);   // never reacted -> the hit lands (HP, or block if just raised)
    }

    protected void BeginAttack() { _busy = true; }
    protected void EndAttack()   { _busy = false; }

    // is the player inside this swing's hurtbox right now? (same targeting as MeleeHitR, no damage)
    bool PlayerInReach(float reach)
    {
        if (player == null) return false;
        float d = Mathf.Min(Vector3.Distance(transform.position, player.position), reach);
        Vector3 center = transform.position + transform.forward * d + Vector3.up * 0.7f;
        foreach (var h in Physics.OverlapSphere(center, Mathf.Max(hitRadius, 0.6f)))
            if (h.CompareTag("Player")) return true;
        return false;
    }

    // aim the hit at the player's position clamped to `reach` along forward — so a LONG reach connects with a
    // close target too. Dodging is handled by the player's i-frames, not by spatial miss.
    protected bool MeleeHitR(float dmg, float poiseDmg, float reach)
    {
        float d = player != null ? Mathf.Min(Vector3.Distance(transform.position, player.position), reach) : reach;
        Vector3 center = transform.position + transform.forward * d + Vector3.up * 0.7f;
        foreach (var h in Physics.OverlapSphere(center, Mathf.Max(hitRadius, 0.6f)))
            if (h.CompareTag("Player") && h.TryGetComponent<IDamageable>(out var t)) { t.TakeDamage(dmg, poiseDmg, transform.position); return true; }
        return false;
    }

    // ───────── IDamageable ─────────
    public void TakeDamage(float damage, float poiseDamage, Vector3 attackerPos)
    {
        if (_dead || Time.time < _invulnUntil) return;
        if (Time.time < _hurtUntil) return;
        _hurtUntil = Time.time + hitCooldown;

        bool tired = _hp <= maxHP * blockHpThreshold;

        if (_blocking)   // only happens while tired -> perfect deflect of a heavy
        {
            _blockMeter += damage * deflectPoiseFraction * lowHpPoiseMult;   // 10% of dmg -> poise (×2); 90% erased (no HP)
            if (player != null) { var pst = player.GetComponent<PlayerStats>(); if (pst != null) pst.DrainStamina(deflectStaminaDamage); } // deflect -> stamina
            if (!_poiseBroken && _blockMeter >= blockMeterMax) StartCoroutine(PoiseBreak());
            return;
        }

        if (_inRecover) _recoverHit = true;             // a hit still ends a punish window early

        if (_poiseBroken)
            _hp = Mathf.Max(0, _hp - damage);           // break window: 100% to HP
        else
        {
            _hp = Mathf.Max(0, _hp - damage * poiseToHpSplit);            // 15% to HP
            _blockMeter += damage * (1f - poiseToHpSplit) * (tired ? lowHpPoiseMult : 1f); // 85% to poise (×2 when tired)
        }
        if (_hp <= 0f) { Die(); return; }
        if (!_poiseBroken && _blockMeter >= blockMeterMax) { StartCoroutine(PoiseBreak()); return; }
        if (!_busy && !_poiseBroken && !_staggered && anim != null) anim.SetTrigger("Hit");

        // damage-reaction dodge — flag a 1m back-step (3s cooldown); the brain / current move picks it up
        if (!_poiseBroken && !_staggered && Time.time >= _damageDodgeReadyAt)
        {
            _damageDodgeReadyAt = Time.time + damageDodgeCooldown;
            _wantDamageDodge = true;
        }
    }

    protected virtual void Die()
    {
        if (_dead) return;
        _dead = true;
        StopAllCoroutines();
        _busy = _blocking = _deflecting = _staggered = _wantDamageDodge = _parryable = false;

        if (player != null)
        {
            var lo = player.GetComponent<LockOnSystem>();
            if (lo != null && lo.LockedTarget != null && (lo.LockedTarget == transform || lo.LockedTarget.IsChildOf(transform)))
                lo.ForceUnlock();
        }

        if (_agent != null) { if (_agent.isOnNavMesh) _agent.ResetPath(); _agent.enabled = false; }
        var col = GetComponent<Collider>(); if (col) col.enabled = false;

        if (anim != null)
        {
            anim.speed = 1f;
            anim.Play(deathState, 0, 0f);
            anim.Update(0f);
        }
    }

    // ───────── shared utilities ─────────
    protected float Dist() => player == null ? 999f : Vector3.Distance(transform.position, player.position);
    protected WaitForSeconds Wait(float s) => new WaitForSeconds(s);

    protected void FacePlayer(bool instant = false)
    {
        if (player == null) return;
        Vector3 to = player.position - transform.position; to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(to);
        transform.rotation = instant ? target : Quaternion.Slerp(transform.rotation, target, rotationSpeed * Time.deltaTime);
    }

    protected void UpdateLocomotionAnim()
    {
        if (anim == null) return;
        bool moving = _agent != null && _agent.velocity.sqrMagnitude > 0.05f;
        anim.SetBool("IsRunning", moving && _agent.speed > moveSpeed + 0.1f);
        anim.SetFloat("Speed", moving ? 1f : 0f);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.red;    Gizmos.DrawWireSphere(transform.position, meleeRange);
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, farRange);
    }
}
