using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// Defensive boss. DEFAULT state = blocking. It only leaves the guard to attack, to
// recover (the short punish window), or to impulse-dodge the player's strong attacks.
// State switches (block<->attack<->dodge) are INSTANT (0-duration anim transitions).
// Poise bar fills from guard hits and ONLY resets when broken (-> Tired). Aggressive
// sub-state (after a fled combo) prefers dodges over blocks until it next attacks.
[RequireComponent(typeof(NavMeshAgent))]
public class MimicBoss : MonoBehaviour, IDamageable, IStaggerable
{
    [Header("Refs (auto-found if empty)")]
    public Transform player;
    public Animator  anim;

    [Header("Stats")]
    public float maxHP        = 400f;
    public float poise        = 90f;
    public float maxStamina   = 100f;    // INVISIBLE — gates attacks
    public float staminaRegen  = 15f;
    public float tiredStaminaThreshold = 30f;

    [Header("Movement")]
    public float moveSpeed   = 4.5f;
    public float sprintSpeed = 7.5f;
    public float rotationSpeed = 14f;

    [Header("Ranges (metres)")]
    public float meleeRange = 1.0f;
    public float farRange   = 3.0f;
    public float fleeRange  = 4.0f;       // player past this DURING a combo -> abort to a forward dodge

    [Header("Guard / Poise break (VISIBLE bar — never regens, only resets on break)")]
    public float blockMeterMax      = 150f;
    public float guardCalculateTime = 1.2f;
    public float tiredTime          = 3.5f;

    [Header("Recovery / punish windows (×1.75 longer)")]
    public float recoverB = 0.65625f;   // was 0.375
    public float recoverC = 0.5f;       // widened (was 0.328) for parity / breathing room
    public float recoverD = 0.875f;     // was 0.5
    public float recoverHopTime      = 0.5f;    // window-exit hop length
    public float recoverHopBack      = 0.3f;    // backward nudge during the hop
    public float recoverHopAnimSpeed = 0.35f;   // jump anim slowed to this
    public string recoverJumpState   = "1031_women_OnehandSW_jump_Start";
    public float postHopNoDodge      = 0.25f;   // brief lockout so a dodge can't stack onto the hop anim

    [Header("Reactive blocking")]
    public float lightComboBlockTime = 2.5f;  // player light-combo -> turtle through it (re-armed each light/finisher hit)

    [Header("Strong-attack dodge bias (base chance, skewed by other systems)")]
    [Range(0f,1f)] public float strongDodgeChance = 0.75f;   // baseline: dodge a strong attack 75% of the time
    public float lowHpDodgeSkew    = -0.65f;  // too weak/tired at low HP -> mostly block instead
    public float aggroDodgeSkew    = -0.30f;  // pressing the attack (turtle-punish) -> eat/guard more, dodge less
    public float nearBreakDodgeSkew = +0.20f; // poise bar nearly full -> dodge to avoid the break
    public float tiredStaminaDodgeSkew = -0.20f; // low stamina -> commit to the guard rather than dodge

    [Header("Aggression / dodge")]
    public float forwardNudge    = 0.5f;  // each combo swing advances this far
    public float dodgeDist       = 1.5f;
    public float dodgeTime       = 0.385f;  // slower nudge (~65% of old speed)
    public float dodgeAnimSpeed  = 0.65f;   // dodge anim plays at 65%
    public float guardAggroDelay = 2.5f;  // player blocks this long (0.5 + 2) -> go aggressive
    public float guardAggroExit  = 1.0f;  // stays aggressive this long after they STOP blocking
    public string guardState     = "1071_women_OnehandSW_guard_Start";
    public string dodgeFrontState = "1061_women_OnehandSW_dodge_front";
    public string dodgeBackState  = "1062_women_OnehandSW_dodge_back";
    public string dodgeLeftState  = "1063_women_OnehandSW_dodge_left";
    public string dodgeRightState = "1064_women_OnehandSW_dodge_right";

    [Header("Intro / death anims")]
    public string battleStartState = "BattleStart";
    public string deathState       = "1051_women_OnehandSW_down";
    public float  introWalkSpeed   = 2.0f;   // slow approach during the intro

    [Header("Damage")]
    public float attackADamage = 12f;
    public float comboLightDamage = 12f;
    public float comboFinisherDamage = 22f;
    public float attackCDamage = 30f;
    public float spearBDamage = 28f;
    public float specialDamage = 50f;
    [Range(0f,1f)] public float appendAChance = 0.8f;

    [Header("Attack E — guard-break punish (block too long + player close)")]
    public float  attackEBlockThreshold = 1.1f;        // continuous block time that triggers it
    public float  attackEBlockThresholdLowHp = 2.0f;   // slower to fire when desperate (more of a turtle)
    public float  attackECooldown = 3.5f;              // min gap between Attack E's (breaks the metronome)
    public float  attackERange   = 1.5f;          // player must be within this to fire
    public float  attackEDamage  = 25f;
    public float  attackEReach   = 1.75f;         // hit reaches out this far
    public float  attackELock    = 0.45f;         // swing window (it plays fast)
    public float  attackEAnimSpeed = 1.8f;        // effective playback vs the natural clip
    public string attackEState   = "2011_women_OnehandSW_attack_B";
    const float   ATTACK_B_BASE  = 1.35f;         // controller speed of that state (divided out so 1.8 = true 1.8x)

    [Header("Special — desperation lunge (once)")]
    public float specialHpPctMin = 0.10f;
    public float specialHpPctMax = 0.25f;
    public float leapTime    = 1.5f;    // first ~30 frames stretched to this (the "leap")
    public float leapSpeed   = 0.33f;   // playback speed during the leap
    public float lungeTime   = 1.0f;    // remaining ~60 frames at native speed (the "lunge")
    public float specialPause = 0.5f;   // punish pause at the very end
    public float specialInvuln = 1.5f;  // invincible through the leap
    [Range(0f,1f)] public float lungeStrikeFraction = 0.55f;
    public float lungeNudge = 3f;        // forward lunge distance during the lunge phase
    public float lungeNudgeSpeed = 4f;   // how fast that lunge covers the distance (x base)

    [Header("Low-HP defensive (after the lunge, < special threshold)")]
    public float lowHpGuardMult   = 1.8f;                 // longer guards when desperate
    [Range(0f,1f)] public float lowHpGuardChance = 0.5f;  // chance to keep guarding instead of attacking

    [Header("Attack anim locks (seconds)")]
    public float lightLock    = 0.55f;
    public float finisherLock = 0.70f;
    public float spearLock    = 1.00f;

    [Header("Combo pacing")]
    public int   comboStartFrames = 10;
    public float comboFps         = 30f;
    public float comboGapB        = 0.20f;
    public float comboGapD        = 0.40f;
    public float hitCooldown      = 0.3f;

    [Header("Hitbox reach (forward offset, metres) — +15% range")]
    public float attackAReach = 0.2875f;   // was 0.25
    public float spearCReach  = 0.8625f;   // was 0.75
    public float otherReach   = 0.575f;    // was 0.5
    public float hitRadius    = 0.3f;
    public bool  drawGizmos   = true;

    NavMeshAgent _agent;
    PlayerStateMachine _playerSM;
    PlayerState _lastPlayerState;
    float _hp, _stamina, _invulnUntil, _hurtUntil, _blockMeter, _specialThreshold, _blockHeldTimer, _blockStopTimer, _lightComboUntil, _blockTimer, _noDodgeUntil, _attackEReadyAt;
    bool  _dead, _busy, _blocking, _staggered, _tired, _specialUsed, _aggressive, _wantDodge, _lowHp, _guardAggro, _introDone, _inRecover, _dodging, _mustAttack, _recoverHit, _wantAttackE, _specialActive;

    public float CurrentHP => _hp;
    public bool  IsDead     => _dead;
    public float Poise01    => Mathf.Clamp01(_blockMeter / Mathf.Max(1f, blockMeterMax));
    public bool  IsTired    => _tired;

    float ComboPause => comboStartFrames / Mathf.Max(1f, comboFps);

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (anim == null)   anim = GetComponentInChildren<Animator>();
        if (anim != null)   anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // boss always animates (death plays off-camera too)
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p) player = p.transform; }
        if (player != null) _playerSM = player.GetComponent<PlayerStateMachine>();
        _hp = maxHP; _stamina = maxStamina;
        _specialThreshold = Random.Range(specialHpPctMin, specialHpPctMax) * maxHP;
        _agent.updateRotation = false;
        _agent.speed = moveSpeed;
        _agent.stoppingDistance = meleeRange * 0.85f;
    }

    void OnEnable() { StartCoroutine(BossBrain()); }

    void Update()
    {
        if (_dead || player == null) return;
        FacePlayer();
        _stamina = Mathf.Min(maxStamina, _stamina + staminaRegen * Time.deltaTime);
        // poise bar NEVER regens — only resets on break (TiredRoutine)

        // Attack E arming — blocked continuously too long with the player in close range (+ cooldown so it can't metronome)
        if (_blocking && !_busy)
        {
            _blockTimer += Time.deltaTime;
            float thr = _lowHp ? attackEBlockThresholdLowHp : attackEBlockThreshold;
            if (_blockTimer >= thr && Dist() <= attackERange && Time.time >= _attackEReadyAt) _wantAttackE = true;
        }
        else _blockTimer = 0f;

        if (_playerSM != null)
        {
            var s = _playerSM.CurrentState;
            // player STARTS a light combo -> arm a block window so the boss turtles through the whole thing
            if (s == PlayerState.LightAttacking && _lastPlayerState != PlayerState.LightAttacking)
                _lightComboUntil = Time.time + lightComboBlockTime;
            else if (s == PlayerState.HeavyAttacking && _lastPlayerState != PlayerState.HeavyAttacking)
            {
                if (Time.time < _lightComboUntil)
                    _lightComboUntil = Time.time + lightComboBlockTime;                        // combo finisher -> keep blocking, don't dodge
                else if (!_blocking && !_inRecover && !_dodging)
                    _wantDodge = true;                                                          // standalone strong attack -> dodge
            }
            _lastPlayerState = s;
        }

        // turtle detection — block for guardAggroDelay -> guard-aggro; exits only guardAggroExit
        // seconds after you STOP blocking (re-blocking within that window keeps it going)
        bool pBlock = _playerSM != null && _playerSM.CurrentState == PlayerState.Blocking;
        if (pBlock)
        {
            _blockHeldTimer += Time.deltaTime; _blockStopTimer = 0f;
            if (_blockHeldTimer >= guardAggroDelay) _guardAggro = true;
        }
        else
        {
            _blockHeldTimer = 0f;
            if (_guardAggro) { _blockStopTimer += Time.deltaTime; if (_blockStopTimer >= guardAggroExit) _guardAggro = false; }
        }
    }

    bool Aggro() => _aggressive || _guardAggro;
    bool PlayerBlocking() => _playerSM != null && _playerSM.CurrentState == PlayerState.Blocking;
    bool LightCombo => Time.time < _lightComboUntil;   // player mid light-combo -> boss turtles, never dodges

    // base chance to dodge a strong attack, skewed by the boss's current situation (clamped 0..1)
    float DodgeChance()
    {
        float c = strongDodgeChance;
        if (_lowHp)                          c += lowHpDodgeSkew;
        if (Aggro())                         c += aggroDodgeSkew;
        if (Poise01 > 0.75f)                 c += nearBreakDodgeSkew;
        if (_stamina < tiredStaminaThreshold) c += tiredStaminaDodgeSkew;
        return Mathf.Clamp01(c);
    }

    // ───────── brain ─────────
    IEnumerator BossBrain()
    {
        yield return null;
        while (!_dead)
        {
            if (player == null) { yield return null; continue; }

            // player dead -> idle and re-arm the intro for the next aggro (respawn)
            if (_playerSM != null && _playerSM.CurrentState == PlayerState.Dead)
            {
                SetGuard(false);
                if (_agent.isOnNavMesh) _agent.ResetPath();
                if (anim != null) { anim.SetBool("IsRunning", false); anim.SetFloat("Speed", 0f); }
                _introDone = false;
                yield return null; continue;
            }

            // first aggro -> hard-coded intro: battlestart -> wait 1s -> slow walk -> Attack C at 3m
            if (!_introDone) { yield return IntroSequence(); _introDone = true; continue; }

            if (_staggered || _tired) { yield return null; continue; }
            if (!_specialUsed && _hp <= _specialThreshold) { yield return DoSpecial(); continue; }

            // Attack E — blocked too long with the player in range -> explode out of the guard into a fast attack_B
            if (_wantAttackE) { _wantAttackE = false; _blockTimer = 0f; yield return DoAttackE(); continue; }

            // player light-attack combo -> immediately turtle through the whole thing (never dodge a light combo)
            if (LightCombo) { _wantDodge = false; SetGuard(true); yield return null; continue; }

            // just dodged -> the NEXT move must be an attack (never a block, never another dodge)
            if (_mustAttack)
            {
                _mustAttack = false; _wantDodge = false;
                if (Dist() > meleeRange)        yield return DoAttackC();
                else if (Random.value < 0.5f)   yield return DoAttackB();
                else                            yield return DoAttackD();
                continue;
            }

            // strong attack: usually dodge (base 75%, skewed by HP / aggression / poise / stamina) — otherwise guard it
            if (_wantDodge)
            {
                _wantDodge = false;
                if (Random.value < DodgeChance()) yield return ImpulseDodge();
                else { SetGuard(true); yield return null; }   // chose not to dodge -> raise the guard to absorb it
                continue;
            }

            float d = Dist();

            if (d > farRange) { if (!_lowHp) yield return ForwardDodge(); yield return DoAttackC(); continue; }

            if (d > meleeRange)   // close the gap
            {
                if (Aggro() && !_lowHp) { yield return ForwardDodge(); }
                else
                {
                    SetGuard(false);                       // general walking = no block
                    _agent.speed = moveSpeed;
                    if (_agent.isOnNavMesh) _agent.SetDestination(player.position);
                    UpdateLocomotionAnim();
                    if (_wantDodge && !_lowHp) { _wantDodge = false; yield return ImpulseDodge(); }
                    yield return null;
                }
                continue;
            }

            // in melee: default-guard (skip if aggressive OR if the player is blocking — never block a blocker)
            bool wasBlocking = !Aggro() && !PlayerBlocking();
            if (wasBlocking) { yield return GuardCalculate(); if (_tired || _staggered || _wantAttackE) continue; }
            if (_stamina < tiredStaminaThreshold && !PlayerBlocking()) { SetGuard(true); yield return null; continue; }

            // low-HP: often just keep guarding to preserve itself (but never while the player is blocking)
            if (_lowHp && !PlayerBlocking() && Random.value < lowHpGuardChance) { yield return null; continue; }

            // leaving block to attack: dodge first (back/side if guarding, forward if aggressive) —
            // but at low HP it's too weak to dodge, so it attacks straight out of the block
            if (!_lowHp)
            {
                if (wasBlocking) yield return ImpulseDodge();
                else             yield return ForwardDodge();
            }
            if (Random.value < 0.5f) yield return DoAttackB();
            else                     yield return DoAttackD();
        }
    }

    IEnumerator IntroSequence()
    {
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        if (anim != null) { anim.speed = 1f; anim.Play(battleStartState, 0, 0f); }   // women OnehandSW battlestart
        yield return Wait(1f);
        // walk slowly toward the player until within 3m (farRange)
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
        if (!_dead) yield return DoAttackC();    // open with Attack C
    }

    IEnumerator GuardCalculate()
    {
        SetGuard(true);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        float dur = _lowHp ? guardCalculateTime * lowHpGuardMult : guardCalculateTime;
        float t = 0f, pBlockT = 0f;
        while (t < dur && !_tired && !_staggered && !_wantAttackE)   // blocking: do NOT dodge (the guard absorbs strong attacks)
        {
            // never out-turtle the player: if they're blocking, hold guard only 0.20s then drop it and attack
            if (PlayerBlocking()) { pBlockT += Time.deltaTime; if (pBlockT >= 0.20f) break; }
            else pBlockT = 0f;
            t += Time.deltaTime; yield return null;
        }
        SetGuard(false);
    }

    IEnumerator Recover(float dur)   // punish window — fully vulnerable; exits with a slow backward jump-hop, NO dodge.
    {                                // a hit during the window drains hp+poise, flinches, and ends it early (see TakeDamage).
        _inRecover = true; _recoverHit = false;
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        if (anim != null) { anim.SetBool("IsRunning", false); anim.SetFloat("Speed", 0f); }
        float t = 0f;
        while (t < dur && !_staggered && !_dead && !_recoverHit) { t += Time.deltaTime; yield return null; }
        _inRecover = false;
        if (_dead || _staggered) yield break;
        if (_recoverHit) yield break;              // got punished -> the flinch already played, no hop
        yield return WindowExitHop();              // 1031 jump @0.35x for 0.5s + 0.3m back nudge
    }

    IEnumerator WindowExitHop()
    {
        Vector3 away = transform.position - player.position; away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        away.Normalize();
        if (anim != null) { anim.speed = recoverHopAnimSpeed; anim.Play(recoverJumpState, 0, 0f); }
        yield return MoveOver(away, recoverHopBack, recoverHopTime);
        if (anim != null) anim.speed = 1f;
        _noDodgeUntil = Time.time + postHopNoDodge;   // can't dodge straight out of the hop (anims would stack)
    }

    void SetGuard(bool on)
    {
        // gameplay-instant, but a short crossfade so the guard's start anim is visible (not jarring)
        if (on && !_blocking && anim != null) anim.CrossFadeInFixedTime(guardState, 0.12f, 0);
        _blocking = on;
        if (anim != null) anim.SetBool("IsBlocking", on);
    }

    IEnumerator TiredRoutine()
    {
        _tired = true; SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        if (anim != null) { anim.speed = 1f; anim.SetBool("IsRunning", false); anim.SetFloat("Speed", 0f); }
        yield return Wait(tiredTime);
        _blockMeter = 0f; _tired = false;
    }

    IEnumerator ImpulseDodge()   // 50% back, 25% left, 25% right
    {
        Vector3 away = transform.position - player.position; away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        away.Normalize();
        Vector3 dir; string state;
        float r = Random.value;
        if      (r < 0.50f) { dir = away;                          state = dodgeBackState; }
        else if (r < 0.75f) { dir = Quaternion.Euler(0,-80,0)*away; state = dodgeLeftState; }
        else                { dir = Quaternion.Euler(0, 80,0)*away; state = dodgeRightState; }
        yield return DoDodge(dir, state);
    }

    IEnumerator BackDodge()         // straight back (exits punish windows)
    {
        Vector3 away = transform.position - player.position; away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        yield return DoDodge(away.normalized, dodgeBackState);
    }

    IEnumerator ForwardDodge()      // close distance toward player
    {
        Vector3 toward = player.position - transform.position; toward.y = 0f;
        if (toward.sqrMagnitude < 0.0001f) toward = transform.forward;
        yield return DoDodge(toward.normalized, dodgeFrontState);
    }

    IEnumerator DoDodge(Vector3 dir, string state)
    {
        while (Time.time < _noDodgeUntil && !_dead) yield return null;   // delay (don't stack) a dodge right after the hop
        _busy = true; _dodging = true; _wantDodge = false; SetGuard(false);
        _invulnUntil = Time.time + dodgeTime;          // i-frames for the whole dodge
        PlayDodge(state);
        yield return MoveOver(dir, dodgeDist, dodgeTime);
        if (anim != null) anim.speed = 1f;             // restore (dodge anim played at dodgeAnimSpeed)
        _dodging = false; _wantDodge = false; _mustAttack = true;   // commit to an attack next — never dodge-into-dodge or dodge-into-block
        _busy = false;
    }

    void PlayDodge(string state) { if (anim != null) { anim.speed = dodgeAnimSpeed; anim.Play(state, 0, 0f); } }

    IEnumerator MoveOver(Vector3 dir, float dist, float time)
    {
        float t = 0f;
        while (t < time) { if (_agent != null && _agent.isOnNavMesh) _agent.Move(dir * (dist / time) * Time.deltaTime); t += Time.deltaTime; yield return null; }
    }

    // ───────── Attack B (light-light-strong) ─────────
    IEnumerator DoAttackB()
    {
        _busy = true; _aggressive = false; _mustAttack = false;
        yield return Swing("LightAttack", lightLock,    comboLightDamage,    ComboPause, otherReach, forwardNudge);
        if (_wantDodge || _staggered) { _busy = false; yield break; }
        if (Fled()) { yield return AbortToAggro(); yield break; }
        yield return Wait(comboGapB);
        yield return Swing("LightAttack", lightLock,    comboLightDamage,    0f, otherReach, forwardNudge);
        if (_wantDodge || _staggered) { _busy = false; yield break; }
        if (Fled()) { yield return AbortToAggro(); yield break; }
        yield return Wait(comboGapB);
        yield return Swing("Finisher",    finisherLock, comboFinisherDamage, 0f, otherReach, forwardNudge);
        if (_wantDodge || _staggered) { _busy = false; yield break; }
        if (Dist() <= meleeRange && Random.value <= appendAChance)
        { yield return Wait(comboGapB); yield return Swing("LightAttack", lightLock, attackADamage, 0f, attackAReach, forwardNudge); }
        _busy = false;
        yield return Recover(recoverB);
    }

    // ───────── Attack C (sprint in + spear) ─────────
    IEnumerator DoAttackC()
    {
        _busy = true; _aggressive = false; _mustAttack = false;
        _agent.speed = sprintSpeed; anim?.SetBool("IsRunning", true); anim?.SetFloat("Speed", 1f);
        float t = 0f;
        while (Dist() > meleeRange && t < 2.5f) { if (_agent.isOnNavMesh) _agent.SetDestination(player.position); anim?.SetFloat("Speed", 1f); t += Time.deltaTime; yield return null; }
        _agent.speed = moveSpeed; anim?.SetBool("IsRunning", false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        anim?.SetTrigger("HeavyAttack");
        yield return SpearHit(spearLock, attackCDamage, 0f, spearCReach, 0f);
        if (_wantDodge || _staggered) { _busy = false; yield break; }
        _busy = false;
        yield return Recover(recoverC);
    }

    // ───────── Attack D (A -> women spearB -> A) ─────────
    IEnumerator DoAttackD()
    {
        _busy = true; _aggressive = false; _mustAttack = false;
        yield return Swing("LightAttack", lightLock, attackADamage, ComboPause, attackAReach, forwardNudge);
        if (_wantDodge || _staggered) { _busy = false; yield break; }
        if (Fled()) { yield return AbortToAggro(); yield break; }
        yield return Wait(comboGapD);
        anim?.SetTrigger("SpearB");
        yield return SpearHit(spearLock, spearBDamage, 0f, otherReach, forwardNudge);
        if (_wantDodge || _staggered) { _busy = false; yield break; }
        if (Fled()) { yield return AbortToAggro(); yield break; }
        yield return Wait(comboGapD);
        yield return Swing("LightAttack", lightLock, attackADamage, 0f, attackAReach, forwardNudge);
        if (_wantDodge || _staggered) { _busy = false; yield break; }
        _busy = false;
        yield return Recover(recoverD);
    }

    // ───────── Attack E (guard-break punish) — fast women OnehandSW attack_B straight out of the block ─────────
    IEnumerator DoAttackE()
    {
        _busy = true; _aggressive = false; _mustAttack = false; _wantDodge = false;
        _attackEReadyAt = Time.time + attackECooldown;   // start the cooldown
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        _stamina = Mathf.Max(0, _stamina - 10f);
        if (anim != null) { anim.speed = attackEAnimSpeed / ATTACK_B_BASE; anim.Play(attackEState, 0, 0f); }   // true 1.8x natural
        float t = 0f; bool dealt = false;
        while (t < attackELock)
        {
            if (forwardNudge > 0f && t < attackELock * 0.5f && _agent != null && _agent.isOnNavMesh)
                _agent.Move(transform.forward * (forwardNudge / (attackELock * 0.5f)) * Time.deltaTime);
            t += Time.deltaTime;
            // hit reaches the whole 1.75m in front: sphere centred at half-reach, radius = half-reach
            if (!dealt && t >= attackELock * 0.4f) { MeleeHitR(attackEDamage, poise * 0.6f, attackEReach * 0.5f, attackEReach * 0.5f); dealt = true; }
            yield return null;
        }
        if (anim != null) anim.speed = 1f;
        _busy = false;
        yield return Recover(recoverB);
    }

    bool Fled() => Dist() > fleeRange;

    IEnumerator AbortToAggro()   // player fled mid-combo -> end early, dash in, go aggressive
    {
        _busy = false; _aggressive = true;
        yield return ForwardDodge();
    }

    // ───────── Special ─────────
    IEnumerator DoSpecial()
    {
        _busy = true; _specialUsed = true; _specialActive = true; _lowHp = true; _aggressive = false; SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        anim?.SetTrigger("Special");
        _invulnUntil = Time.time + specialInvuln;

        // LEAP — first ~30 frames stretched to leapTime, tracking the player (invincible)
        if (anim != null) anim.speed = leapSpeed;
        float t = 0f;
        while (t < leapTime) { FacePlayer(true); t += Time.deltaTime; yield return null; }

        // LUNGE — remaining ~60 frames at native speed; the strike lands mid-lunge (now vulnerable)
        if (anim != null) anim.speed = 1f;
        t = 0f; bool struck = false; float nudged = 0f;
        while (t < lungeTime)
        {
            if (nudged < lungeNudge && _agent != null && _agent.isOnNavMesh)   // fast forward lunge, capped at lungeNudge
            {
                float step = Mathf.Min((lungeNudge / lungeTime) * lungeNudgeSpeed * Time.deltaTime, lungeNudge - nudged);
                _agent.Move(transform.forward * step); nudged += step;
            }
            if (!struck && t >= lungeTime * lungeStrikeFraction) { MeleeHitR(specialDamage, poise, otherReach); struck = true; }
            t += Time.deltaTime; yield return null;
        }

        // PUNISH PAUSE — freeze the last frame
        if (anim != null) anim.speed = 0f;
        yield return Wait(specialPause);
        if (anim != null) anim.speed = 1f;
        _busy = false; _specialActive = false;
    }

    // ───────── swing / hit helpers (nudge = forward advance during the swing) ─────────
    IEnumerator Swing(string trig, float lockT, float dmg, float windup, float reach, float nudge)
    {
        if (_wantDodge || _staggered) yield break;                       // strong attack -> bail to a dodge
        if (windup > 0f) yield return Wait(windup);
        anim?.SetTrigger(trig);
        _stamina = Mathf.Max(0, _stamina - 8f);
        float t = 0f; bool dealt = false;
        while (t < lockT)
        {
            if (_wantDodge || _staggered) yield break;
            if (nudge > 0f && t < lockT * 0.5f && _agent != null && _agent.isOnNavMesh)
                _agent.Move(transform.forward * (nudge / (lockT * 0.5f)) * Time.deltaTime);
            t += Time.deltaTime;
            if (!dealt && t >= lockT * 0.4f) { MeleeHitR(dmg, poise * 0.5f, reach); dealt = true; }
            yield return null;
        }
    }

    IEnumerator SpearHit(float lockT, float dmg, float windup, float reach, float nudge)
    {
        if (_wantDodge || _staggered) yield break;
        _stamina = Mathf.Max(0, _stamina - 15f);
        float t = 0f; bool dealt = false;
        while (t < lockT)
        {
            if (_wantDodge || _staggered) yield break;
            if (nudge > 0f && t < lockT * 0.5f && _agent != null && _agent.isOnNavMesh)
                _agent.Move(transform.forward * (nudge / (lockT * 0.5f)) * Time.deltaTime);
            t += Time.deltaTime;
            if (!dealt && t >= lockT * 0.45f) { MeleeHitR(dmg, poise * 0.7f, reach); dealt = true; }
            yield return null;
        }
    }

    void MeleeHitR(float dmg, float poiseDmg, float reach) => MeleeHitR(dmg, poiseDmg, reach, hitRadius);

    void MeleeHitR(float dmg, float poiseDmg, float reach, float radius)
    {
        foreach (var h in Physics.OverlapSphere(transform.position + transform.forward * reach, radius))
            if (h.CompareTag("Player") && h.TryGetComponent<IDamageable>(out var t)) { t.TakeDamage(dmg, poiseDmg, transform.position); return; }
    }

    // ───────── IDamageable ─────────
    public void TakeDamage(float damage, float poiseDamage, Vector3 attackerPos)
    {
        if (_dead || Time.time < _invulnUntil) return;
        if (Time.time < _hurtUntil) return;
        _hurtUntil = Time.time + hitCooldown;

        if (_blocking)   // GUARD up -> fill poise bar, NO hp (poiseDamage now counts, so strong attacks break the guard faster)
        {
            _blockMeter += damage + poiseDamage;
            if (_blockMeter >= blockMeterMax && !_tired) StartCoroutine(TiredRoutine());
            return;
        }

        if (_inRecover)   // PUNISH WINDOW -> hit drains BOTH hp and the poise bar, flinches, ends the window early
        {
            _hp = Mathf.Max(0, _hp - damage);
            _blockMeter = Mathf.Min(blockMeterMax, _blockMeter + damage);
            _recoverHit = true;
            if (_hp <= 0f) { Die(); return; }
            if (_blockMeter >= blockMeterMax && !_tired) { StartCoroutine(TiredRoutine()); return; }
            anim?.SetTrigger("Hit");
            return;
        }

        _hp = Mathf.Max(0, _hp - damage);
        if (_hp <= 0f) { Die(); return; }
        if (!_busy)   // super-armor during attacks
        {
            if (poiseDamage >= poise) StartCoroutine(Stagger());
            else anim?.SetTrigger("Hit");
        }
    }

    // IStaggerable — the Mimic is parryable whenever its hit connects, EXCEPT its last-resort desperation lunge
    public bool CanBeParried => !_specialActive;

    // IStaggerable — PARRIED: take poise (no HP), then play the stagger (deaf for its duration)
    public void Stagger(float poiseDamage)
    {
        if (_dead || _staggered) return;
        _blockMeter = Mathf.Min(blockMeterMax, _blockMeter + poiseDamage);
        StartCoroutine(Stagger());
    }

    IEnumerator Stagger()
    {
        _staggered = true; _busy = true;
        if (anim != null) anim.speed = 1f;
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        anim?.SetTrigger("Stagger");
        yield return Wait(0.6f);
        _staggered = false; _busy = false;
    }

    void Die()
    {
        if (_dead) return;
        _dead = true;
        StopAllCoroutines();                                  // halt brain/attacks so nothing overrides the collapse
        _busy = _blocking = _staggered = _tired = _guardAggro = _aggressive = false;

        // break the player's lock-on if it's targeting us
        if (player != null)
        {
            var lo = player.GetComponent<LockOnSystem>();
            if (lo != null && lo.LockedTarget != null && (lo.LockedTarget == transform || lo.LockedTarget.IsChildOf(transform)))
                lo.ForceUnlock();
        }

        // glue in place
        if (_agent != null) { if (_agent.isOnNavMesh) _agent.ResetPath(); _agent.enabled = false; }
        var col = GetComponent<Collider>(); if (col) col.enabled = false;

        // play the collapse — bulletproof (clear speed + all triggers first, then play the state directly)
        if (anim != null)
        {
            anim.speed = 1f;
            anim.SetBool("IsBlocking", false); anim.SetBool("IsRunning", false); anim.SetFloat("Speed", 0f);
            foreach (var p in anim.parameters) if (p.type == AnimatorControllerParameterType.Trigger) anim.ResetTrigger(p.name);
            anim.Play(deathState, 0, 0f);
            anim.Update(0f);                 // apply the death state immediately (don't wait a frame)
        }
    }

    float Dist() { Vector3 a = transform.position, b = player.position; a.y = b.y = 0f; return Vector3.Distance(a, b); }

    void FacePlayer(bool instant = false)
    {
        Vector3 dir = player.position - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = instant ? target : Quaternion.Slerp(transform.rotation, target, rotationSpeed * Time.deltaTime);
    }

    void UpdateLocomotionAnim()
    {
        if (anim == null) return;
        anim.SetFloat("Speed", Mathf.Clamp01(_agent.velocity.magnitude / Mathf.Max(0.1f, moveSpeed)), 0.1f, Time.deltaTime);
    }

    IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.deltaTime; yield return null; } }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, meleeRange);
        Gizmos.color = Color.cyan;   Gizmos.DrawWireSphere(transform.position, farRange);
        Gizmos.color = new Color(1f,0.85f,0f,0.9f); Gizmos.DrawWireSphere(transform.position + transform.forward * attackAReach, hitRadius);
        Gizmos.color = new Color(1f,0.45f,0f,0.9f); Gizmos.DrawWireSphere(transform.position + transform.forward * otherReach,  hitRadius);
        Gizmos.color = Color.red;                   Gizmos.DrawWireSphere(transform.position + transform.forward * spearCReach, hitRadius);
    }
}
