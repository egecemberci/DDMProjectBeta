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
    public float sprintAcceleration = 40f;   // NavMesh accel while sprinting (Attack C lunge-in) — higher = snappier launch
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
    public float recoverHopTime      = 0.5f;    // how long the breather OVERLAPS the window tail (early start, fine)
    public float recoverHopBack      = 0.3f;    // backward drift during the breather
    public float recoverHopAnimSpeed = 0.35f;   // jump-snippet slowed to this (subtle "breather")
    public float recoverHopBlend     = 0.5f;    // VERY smooth crossfade into the breather snippet
    public float recoverHopTail      = 0.1f;    // max spill PAST the window into the decision state (keep <=0.1)
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
    public float forwardNudge    = 0.5f;  // each combo swing advances this far (Attack B & D)
    public float attackCNudge    = 3f;    // Attack C's thrust lunge — its own knob, independent of forwardNudge
    public float attackCParryKnockback = 3f;     // if Attack C is PARRIED: kill the lunge + recoil this far back...
    public float attackCParryKnockTime = 0.18f;  // ...over this long (snappy, emphasizes the parry)
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
    public float specialHpPctMin = 0.35f;
    public float specialHpPctMax = 0.35f;
    public float leapTime    = 1.5f;    // first ~30 frames stretched to this (the "leap")
    public float leapSpeed   = 0.33f;   // playback speed during the leap
    public float lungeTime   = 1.0f;    // remaining ~60 frames at native speed (the "lunge")
    public float specialPause = 0.5f;   // punish pause at the very end
    public float specialInvuln = 1.5f;  // invincible through the leap
    [Range(0f,1f)] public float lungeStrikeFraction = 0.55f;
    public float lungeNudge = 3f;        // forward lunge distance during the lunge phase
    public float lungeNudgeSpeed = 4f;   // how fast that lunge covers the distance (x base)
    public float specialSweepWidth  = 3f;   // lunge STRIKE hitbox width (m) — wide so it can't be sidestepped
    public float specialSweepLength = 4f;   // lunge STRIKE hitbox length (forward reach, m)

    [Header("Low-HP defensive (after the lunge, < special threshold)")]
    public float lowHpGuardMult   = 1.8f;                 // longer guards when desperate
    [Range(0f,1f)] public float lowHpGuardChance = 0.5f;  // chance to keep guarding instead of attacking

    [Header("Block frequency / forced attacks")]
    [Range(0f,1f)] public float defensiveHpThreshold = 0.25f;  // at/below this HP fraction it turtles (blocks a lot)
    [Range(0f,1f)] public float normalBlockChance    = 0.25f;  // chance to guard before an attack ABOVE the threshold (rare)
    [Range(0f,1f)] public float defensiveBlockChance = 0.7f;   // ...and BELOW it (the turtle state — frequent)
    public float attackForceInterval = 3f;                      // can't perma-turtle: forces an attack after this long without one

    [Header("Heal — once-ever desperation potion (drops to <=5% HP, not staggered)")]
    public GameObject potionModel;                        // iksir drawn into the left hand during the drink (assigned in scene)
    public float healAmount     = 150f;                   // HP restored
    [Range(0f,1f)] public float healHpThreshold = 0.05f;  // triggers at/below this HP fraction
    public float healDrinkTime  = 1.5f;                   // drink act length — anim plays + invulnerable for this long
    public string healDrinkState = "Drink";               // item-use / drink anim state

    [Header("Combo pacing")]
    public float comboGapB        = 0.12f;   // tightened from 0.20 — combos flow instead of feeling disjointed
    public float comboGapD        = 0.22f;   // tightened from 0.40
    public float appendADelay     = 0.35f;   // pause before Attack-B's optional tag-on Attack-A (its own knob)
    public float hitCooldown      = 0.3f;

    [Header("Block-break retreat (block 2+ hits in one guard -> dodge away)")]
    public int   blockBreakHits = 2;     // blocked hits in a single guard that trigger the break-out
    public float blockBreakDist = 3f;    // dodge back until further than this from the player...
    public float blockBreakHold = 0.4f;  // ...and stay beyond it this long (checked only between dodges)

    [Header("Hitbox reach (forward offset, metres) — +15% range")]
    public float attackAReach = 0.2875f;   // was 0.25
    public float spearCReach  = 0.8625f;   // was 0.75
    public float otherReach   = 0.575f;    // was 0.5
    public float hitRadius    = 0.3f;     // capsule WIDTH (radius)
    public float hitLength    = 0f;       // capsule TUBE length along forward (0 = a plain sphere, old behaviour)
    public float hitHeight    = 0f;       // capsule vertical offset off the root (raise to body height)
    public bool  drawGizmos   = true;

    [Header("Audio")]
    public AudioClip clashClip;          // swordsound — plays when the Mimic blocks our attack or lands one of its hits (no gating)

    [Header("Attack clip STATE NAMES (frame-accurate playback)")]
    public string attackAState = "2001_women_OnehandSW_attack_A";   // LightAttack / A swings
    public string attackBState = "2011_women_OnehandSW_attack_B";   // Finisher
    [Header("Per-clip hurtbox/parry FRAMES (windup ends -> hurtbox; parry = [start,end))")]
    public int aWindup   = 21, aParryStart   = 21, aParryEnd   = 27;  // OnehandSW attack_A
    public int bWindup   = 50, bParryStart   = 50, bParryEnd   = 56;  // OnehandSW attack_B (Finisher / Attack-E)
    public int spAWindup = 20, spAParryStart = 20, spAParryEnd = 28;  // spear attack_A (Attack C)
    public int spBWindup = 26, spBParryStart = 26, spBParryEnd = 33;  // spear attack_B (Attack D)

    NavMeshAgent _agent;
    PlayerStateMachine _playerSM;
    CombatSfx _sfx;
    WalkSfx _walkSfx;
    PlayerState _lastPlayerState;
    float _hp, _stamina, _invulnUntil, _hurtUntil, _blockMeter, _specialThreshold, _blockHeldTimer, _blockStopTimer, _lightComboUntil, _blockTimer, _noDodgeUntil, _attackEReadyAt, _baseAccel, _lastAttackTime;
    bool  _dead, _busy, _blocking, _staggered, _tired, _specialUsed, _aggressive, _wantDodge, _lowHp, _guardAggro, _introDone, _inRecover, _dodging, _mustAttack, _recoverHit, _wantAttackE, _specialActive, _parryable, _wantBlockBreak, _healUsed, _wantHeal;
    int   _guardHits;

    public float CurrentHP => _hp;
    public bool  IsDead     => _dead;
    public float Poise01    => Mathf.Clamp01(_blockMeter / Mathf.Max(1f, blockMeterMax));
    public bool  IsTired    => _tired;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _sfx   = GetComponent<CombatSfx>();
        _walkSfx = GetComponent<WalkSfx>();
        if (anim == null)   anim = GetComponentInChildren<Animator>();
        if (anim != null)   anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // boss always animates (death plays off-camera too)
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p) player = p.transform; }
        if (player != null) _playerSM = player.GetComponent<PlayerStateMachine>();
        _hp = maxHP; _stamina = maxStamina;
        _specialThreshold = Random.Range(specialHpPctMin, specialHpPctMax) * maxHP;
        _agent.updateRotation = false;
        _agent.speed = moveSpeed;
        _baseAccel = _agent.acceleration;                 // inspector accel — used for all non-sprint movement
        _agent.stoppingDistance = meleeRange * 0.85f;
    }

    void OnEnable() { StartCoroutine(BossBrain()); }

    void Update()
    {
        ReportWalkSfx();
        if (_dead || player == null) return;
        if (!_dodging) FacePlayer();   // freeze facing during dodges so directional dodges read cleanly (no sideways slide)
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
            if (!_healUsed && _hp <= maxHP * healHpThreshold) { _wantHeal = false; yield return HealRoutine(); continue; }
            if (!_specialUsed && _hp <= _specialThreshold) { yield return DoSpecial(); continue; }

            // no perma-turtle: force an attack if it's gone too long without one
            if (!_mustAttack && Time.time - _lastAttackTime > attackForceInterval) _mustAttack = true;

            // blocked 2+ hits this guard -> break out: full dodge(s) until clear of the player (then MUST attack)
            if (_wantBlockBreak) { _wantBlockBreak = false; yield return BlockBreakRetreat(); continue; }

            // Attack E — blocked too long with the player in range -> explode out of the guard into a fast attack_B
            if (_wantAttackE) { _wantAttackE = false; _blockTimer = 0f; yield return DoAttackE(); continue; }

            // forced attack — after a block-break dodge, or the 3s timer above. Overrides turtle/dodge reactions.
            if (_mustAttack)
            {
                _mustAttack = false; _wantDodge = false;
                if (Dist() > meleeRange)        yield return DoAttackC();
                else if (Random.value < 0.5f)   yield return DoAttackB();
                else                            yield return DoAttackD();
                continue;
            }

            // player light-attack combo -> immediately turtle through the whole thing (never dodge a light combo)
            if (LightCombo) { _wantDodge = false; SetGuard(true); yield return null; continue; }

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

            // in melee: only SOMETIMES raise the guard before attacking — rare above the turtle threshold, frequent
            // below it. Never block while aggressive or while the player is blocking. Either branch then attacks.
            bool defensive = _hp <= maxHP * defensiveHpThreshold;
            bool wasBlocking = !Aggro() && !PlayerBlocking() && Random.value < (defensive ? defensiveBlockChance : normalBlockChance);
            if (wasBlocking) { yield return GuardCalculate(); if (_tired || _staggered || _wantAttackE || _wantBlockBreak) continue; }
            if (_stamina < tiredStaminaThreshold && !PlayerBlocking()) { SetGuard(true); yield return null; continue; }

            // leaving block to attack: dodge first (back/side if we guarded, forward if aggressive) —
            // but when defensive (<= threshold HP) it's too weak to dodge, attacks straight out of the block
            if (!defensive)
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
        if (anim != null) { anim.speed = 1f; anim.CrossFadeInFixedTime(battleStartState, 0.12f, 0); }   // women OnehandSW battlestart
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
        while (t < dur && !_tired && !_staggered && !_wantAttackE && !_wantBlockBreak)   // blocking: do NOT dodge (the guard absorbs strong attacks)
        {
            // never out-turtle the player: if they're blocking, hold guard only 0.20s then drop it and attack
            if (PlayerBlocking()) { pBlockT += Time.deltaTime; if (pBlockT >= 0.20f) break; }
            else pBlockT = 0f;
            t += Time.deltaTime; yield return null;
        }
        SetGuard(false);
    }

    IEnumerator Recover(float dur)   // punish window — fully vulnerable; a hit ends it early (see TakeDamage).
    {                                // exits with a SLOW, smoothly-blended "breather" snippet of the jump anim that
                                     // OVERLAPS the window tail (early start is fine) and spills <= recoverHopTail past it.
        _inRecover = true; _recoverHit = false;
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        if (anim != null) { anim.SetBool("IsRunning", false); anim.SetFloat("Speed", 0f); }

        float lead  = Mathf.Min(recoverHopTime, dur);   // breather overlaps this much of the window's tail
        float hopAt = dur - lead;
        float t = 0f; bool breathing = false;
        while (t < dur && !_staggered && !_dead && !_recoverHit)
        {
            if (!breathing && t >= hopAt)               // begin the breather INSIDE the window
            {
                breathing = true;
                if (anim != null) { anim.CrossFadeInFixedTime(recoverJumpState, recoverHopBlend, 0); anim.speed = recoverHopAnimSpeed; }
            }
            if (breathing && lead > 0.01f && _agent != null && _agent.isOnNavMesh)   // gentle backward drift for air
            {
                Vector3 away = transform.position - player.position; away.y = 0f;
                if (away.sqrMagnitude > 0.0001f) _agent.Move(away.normalized * (recoverHopBack / lead) * Time.deltaTime);
            }
            t += Time.deltaTime; yield return null;
        }
        _inRecover = false;
        if (_dead || _staggered) { if (anim != null) anim.speed = 1f; yield break; }
        if (_recoverHit)         { if (anim != null) anim.speed = 1f; yield break; }   // punished -> flinch took over, no breather

        yield return Wait(recoverHopTail);              // <=0.1s spill, then hand control back to the decision loop
        if (anim != null) anim.speed = 1f;
        _noDodgeUntil = Time.time + postHopNoDodge;
    }

    void SetGuard(bool on)
    {
        // gameplay-instant, but a short crossfade so the guard's start anim is visible (not jarring)
        if (on && !_blocking) { _guardHits = 0; if (anim != null) { anim.speed = 1f; anim.CrossFadeInFixedTime(guardState, 0.12f, 0); } }   // new guard session -> reset blocked-hit count
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

    // blocked blockBreakHits+ in one guard -> drop the block and dodge away until clear of the player.
    // Dodges are never interrupted/stacked: the proximity check runs ONLY after each dodge anim finishes.
    IEnumerator BlockBreakRetreat()
    {
        SetGuard(false);
        while (!_dead)
        {
            yield return BackDodge();                                          // one full back dodge (un-interruptible)
            float held = 0f;                                                   // dodge done -> now measure distance
            while (held < blockBreakHold && Dist() > blockBreakDist) { held += Time.deltaTime; yield return null; }
            if (Dist() > blockBreakDist && held >= blockBreakHold) break;      // far enough, long enough -> done
        }
        _mustAttack = true;                                                   // MUST attack after breaking out of a block with a dodge
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

    void PlayDodge(string state) { if (anim != null) { anim.CrossFadeInFixedTime(state, 0.12f, 0); anim.speed = dodgeAnimSpeed; } }

    IEnumerator MoveOver(Vector3 dir, float dist, float time)
    {
        float t = 0f;
        while (t < time) { if (_agent != null && _agent.isOnNavMesh) _agent.Move(dir * (dist / time) * Time.deltaTime); t += Time.deltaTime; yield return null; }
    }

    // Attack C parried -> kill its forward lunge momentum on the spot and recoil straight back to emphasize the parry
    IEnumerator AttackCParryKnockback()
    {
        if (_agent != null) _agent.velocity = Vector3.zero;          // stop the lunge dead
        Vector3 away = transform.position - player.position; away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
        yield return MoveOver(away.normalized, attackCParryKnockback, attackCParryKnockTime);
    }

    // ───────── Attack B (light-light-strong) ─────────
    IEnumerator DoAttackB()
    {
        _busy = true; _aggressive = false; _mustAttack = false; _lastAttackTime = Time.time;
        yield return FrameSwing(attackAState, null, aWindup, aParryStart, aParryEnd, comboLightDamage, poise * 0.5f, otherReach, forwardNudge, 8f);
        if (_wantDodge || _staggered || _wantHeal) { _busy = false; yield break; }
        if (Fled()) { yield return AbortToAggro(); yield break; }
        yield return Wait(comboGapB);
        yield return FrameSwing(attackAState, null, aWindup, aParryStart, aParryEnd, comboLightDamage, poise * 0.5f, otherReach, forwardNudge, 8f);
        if (_wantDodge || _staggered || _wantHeal) { _busy = false; yield break; }
        if (Fled()) { yield return AbortToAggro(); yield break; }
        yield return Wait(comboGapB);
        yield return FrameSwing(attackBState, null, bWindup, bParryStart, bParryEnd, comboFinisherDamage, poise * 0.5f, otherReach, forwardNudge, 8f);
        if (_wantDodge || _staggered || _wantHeal) { _busy = false; yield break; }
        if (Dist() <= meleeRange && Random.value <= appendAChance)
        { yield return Wait(appendADelay); yield return FrameSwing(attackAState, null, aWindup, aParryStart, aParryEnd, attackADamage, poise * 0.5f, attackAReach, forwardNudge, 8f); }
        _busy = false;
        yield return Recover(recoverB);
    }

    // ───────── Attack C (sprint in + spear) ─────────
    IEnumerator DoAttackC()
    {
        _busy = true; _aggressive = false; _mustAttack = false; _lastAttackTime = Time.time;
        _agent.speed = sprintSpeed; _agent.acceleration = sprintAcceleration; anim?.SetBool("IsRunning", true); anim?.SetFloat("Speed", 1f);
        float t = 0f;
        while (Dist() > meleeRange && t < 2.5f) { if (_agent.isOnNavMesh) _agent.SetDestination(player.position); anim?.SetFloat("Speed", 1f); t += Time.deltaTime; yield return null; }
        _agent.speed = moveSpeed; _agent.acceleration = _baseAccel; anim?.SetBool("IsRunning", false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        yield return FrameSwing(null, "HeavyAttack", spAWindup, spAParryStart, spAParryEnd, attackCDamage, poise * 0.7f, spearCReach, attackCNudge, 15f);
        if (_wantDodge || _staggered || _wantHeal) { if (_staggered) StartCoroutine(AttackCParryKnockback()); _busy = false; yield break; }
        _busy = false;
        yield return Recover(recoverC);
    }

    // ───────── Attack D (A -> women spearB -> A) ─────────
    IEnumerator DoAttackD()
    {
        _busy = true; _aggressive = false; _mustAttack = false; _lastAttackTime = Time.time;
        yield return FrameSwing(attackAState, null, aWindup, aParryStart, aParryEnd, attackADamage, poise * 0.5f, attackAReach, forwardNudge, 8f);
        if (_wantDodge || _staggered || _wantHeal) { _busy = false; yield break; }
        if (Fled()) { yield return AbortToAggro(); yield break; }
        yield return Wait(comboGapD);
        yield return FrameSwing(null, "SpearB", spBWindup, spBParryStart, spBParryEnd, spearBDamage, poise * 0.7f, otherReach, forwardNudge, 15f);
        if (_wantDodge || _staggered || _wantHeal) { _busy = false; yield break; }
        if (Fled()) { yield return AbortToAggro(); yield break; }
        yield return Wait(comboGapD);
        yield return FrameSwing(attackAState, null, aWindup, aParryStart, aParryEnd, attackADamage, poise * 0.5f, attackAReach, forwardNudge, 8f);
        if (_wantDodge || _staggered || _wantHeal) { _busy = false; yield break; }
        _busy = false;
        yield return Recover(recoverD);
    }

    // ───────── Attack E (guard-break punish) — fast women OnehandSW attack_B straight out of the block ─────────
    IEnumerator DoAttackE()
    {
        _busy = true; _aggressive = false; _mustAttack = false; _wantDodge = false; _lastAttackTime = Time.time;
        _attackEReadyAt = Time.time + attackECooldown;   // start the cooldown
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();
        _stamina = Mathf.Max(0, _stamina - 10f);
        // fast attack_B, but hurtbox/parry still keyed to the clip's frames (attack_B = b* frames), read live
        if (anim != null) { anim.Play(attackEState, 0, 0f); anim.Update(0f); anim.speed = attackEAnimSpeed / ATTACK_B_BASE; }
        float frames = 30f;
        if (anim != null) { var ci = anim.GetCurrentAnimatorClipInfo(0); if (ci.Length > 0 && ci[0].clip != null) frames = ci[0].clip.length * ci[0].clip.frameRate; }
        float frame = 0f, prev = 0f, guard = 0f; bool dealt = false;
        while (guard < 3f)
        {
            if (_wantDodge || _staggered || _wantHeal) break;
            if (anim != null) frame = anim.GetCurrentAnimatorStateInfo(0).normalizedTime * frames;
            _parryable = !_specialActive && frame >= bParryStart && frame < bParryEnd;
            float df = frame - prev; prev = frame;
            if (forwardNudge > 0f && frame <= bWindup && df > 0f && _agent != null && _agent.isOnNavMesh)
                _agent.Move(transform.forward * (forwardNudge * df / Mathf.Max(1f, bWindup)));
            // hit reaches the whole 1.75m in front: sphere centred at half-reach, radius = half-reach
            if (!dealt && frame >= bWindup) { _parryable = !_specialActive && bWindup >= bParryStart && bWindup < bParryEnd; MeleeHitR(attackEDamage, poise * 0.6f, attackEReach * 0.5f, attackEReach * 0.5f); dealt = true; }
            if (dealt && frame >= bParryEnd) break;
            if (frame >= frames - 0.5f) break;
            guard += Time.deltaTime; yield return null;
        }
        _parryable = false;
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
            if (!struck && t >= lungeTime * lungeStrikeFraction) { SpecialSweepHit(specialDamage, poise); struck = true; }
            t += Time.deltaTime; yield return null;
        }

        // PUNISH PAUSE — freeze the last frame
        if (anim != null) anim.speed = 0f;
        yield return Wait(specialPause);
        if (anim != null) anim.speed = 1f;
        _busy = false; _specialActive = false;
    }

    // ───────── Heal — once-ever desperation potion ─────────
    // At <=healHpThreshold HP (and not staggered) it drops everything, panics back with two quick dodges, then
    // draws the iksir and drinks (invulnerable so it always lands), healing healAmount. Healing RE-ARMS the
    // desperation lunge so it can fire once more as HP descends again; the heal itself can only ever happen once.
    IEnumerator HealRoutine()
    {
        _healUsed = true; _wantHeal = false;
        _busy = true; _aggressive = false; _wantDodge = false; _wantBlockBreak = false; _mustAttack = false;
        SetGuard(false);
        if (_agent.isOnNavMesh) _agent.ResetPath();

        yield return BackDodge();                          // two quick back-dodges (their i-frames protect it)
        yield return BackDodge();

        _busy = true;                                      // dodges cleared it
        _invulnUntil = Time.time + healDrinkTime;          // invulnerable through the drink so the heal can't be denied
        if (potionModel != null) potionModel.SetActive(true);
        if (anim != null) { anim.speed = 1f; anim.CrossFadeInFixedTime(healDrinkState, 0.15f, 0); }
        yield return Wait(healDrinkTime);
        _hp = Mathf.Min(maxHP, _hp + healAmount);
        if (potionModel != null) potionModel.SetActive(false);

        _specialUsed = false; _lowHp = false;              // re-arm the lunge for the next descent; heal stays once-only
        _mustAttack = false; _busy = false;
    }

    // ───────── frame-accurate swing helper ─────────
    // Plays the attack (by STATE name for the OnehandSW clips so re-use restarts cleanly, or by TRIGGER for the
    // spears), then times everything to the ACTUAL playing clip: hurtbox lands at the `windup` contact frame,
    // and the boss is parryable ONLY across [parryStart, parryEnd). Reads the clip's real frameRate, so any
    // fps / state-speed is handled automatically. `nudge` is spread across the windup (the step-in).
    IEnumerator FrameSwing(string playState, string trig, int windup, int parryStart, int parryEnd,
                           float dmg, float poiseDmg, float reach, float nudge, float staCost)
    {
        if (_wantDodge || _staggered || _wantHeal) yield break;
        _stamina = Mathf.Max(0, _stamina - staCost);
        if (anim != null)
        {
            anim.speed = 1f;
            if (!string.IsNullOrEmpty(playState)) { anim.Play(playState, 0, 0f); anim.Update(0f); }   // restart cleanly each use
            else
            {
                anim.SetTrigger(trig);
                float w = 0f;
                while (!anim.IsInTransition(0) && w < 0.05f) { w += Time.deltaTime; yield return null; }   // wait for the swap to start
                w = 0f;
                while (anim.IsInTransition(0) && w < 0.3f)   { w += Time.deltaTime; yield return null; }   // ...and finish
            }
        }

        float frames = 30f;
        if (anim != null) { var ci = anim.GetCurrentAnimatorClipInfo(0); if (ci.Length > 0 && ci[0].clip != null) frames = ci[0].clip.length * ci[0].clip.frameRate; }

        float frame = 0f, prev = 0f, guard = 0f; bool dealt = false;
        while (guard < 4f)
        {
            if (_wantDodge || _staggered || _wantHeal) { _parryable = false; yield break; }
            if (anim != null) frame = anim.GetCurrentAnimatorStateInfo(0).normalizedTime * frames;
            _parryable = !_specialActive && frame >= parryStart && frame < parryEnd;
            float df = frame - prev; prev = frame;
            if (nudge > 0f && frame <= windup && df > 0f && _agent != null && _agent.isOnNavMesh)
                _agent.Move(transform.forward * (nudge * df / Mathf.Max(1f, windup)));     // step in over the windup (total = nudge)
            if (!dealt && frame >= windup) { _parryable = !_specialActive && windup >= parryStart && windup < parryEnd; MeleeHitR(dmg, poiseDmg, reach); dealt = true; }   // contact AFTER the windup; parryable keyed to windup (frame-rate robust)
            if (dealt && frame >= parryEnd) break;                                              // hit landed + parry window closed
            if (frame >= frames - 0.5f) break;                                                  // clip finished
            guard += Time.deltaTime; yield return null;
        }
        _parryable = false;
        if (!dealt && !_wantDodge && !_staggered) MeleeHitR(dmg, poiseDmg, reach);   // safety: never silently whiff
    }

    // feed the footstep loop: moving = the agent has velocity; sprinting = faster than the base walk speed (e.g. Attack C dash)
    void ReportWalkSfx()
    {
        if (_walkSfx == null) return;
        bool moving = !_dead && _agent != null && _agent.isOnNavMesh && _agent.velocity.sqrMagnitude > 0.05f;
        bool sprinting = moving && _agent != null && _agent.speed > moveSpeed + 0.1f;
        _walkSfx.Report(moving, sprinting);
    }

    void MeleeHitR(float dmg, float poiseDmg, float reach) => MeleeHitR(dmg, poiseDmg, reach, hitRadius);

    // CAPSULE hurtbox — a `hitLength`-long tube of radius `radius`, centred `reach` m in front along forward.
    // hitLength 0 collapses both caps onto one point => identical to the old sphere, so existing tuning is unchanged.
    void MeleeHitR(float dmg, float poiseDmg, float reach, float radius)
    {
        Vector3 center = transform.position + transform.forward * reach + Vector3.up * hitHeight;
        Vector3 half   = transform.forward * (hitLength * 0.5f);
        foreach (var h in Physics.OverlapCapsule(center - half, center + half, radius))
            if (h.CompareTag("Player") && h.TryGetComponent<IDamageable>(out var t))
            { t.TakeDamage(dmg, poiseDmg, transform.position); if (_sfx != null) _sfx.PlayOver(clashClip); return; }   // SFX: Mimic landed a hit
    }

    // wide BOX sweep for the desperation lunge only — specialSweepLength forward × specialSweepWidth wide,
    // so the strike can't be sidestepped (a well-timed i-frame dodge still beats it; nothing else uses this).
    void SpecialSweepHit(float dmg, float poiseDmg)
    {
        Vector3 center = transform.position + transform.forward * (specialSweepLength * 0.5f) + Vector3.up * 1.0f;
        Vector3 half   = new Vector3(specialSweepWidth * 0.5f, 2.5f, specialSweepLength * 0.5f);
        foreach (var h in Physics.OverlapBox(center, half, transform.rotation))
            if (h.CompareTag("Player") && h.TryGetComponent<IDamageable>(out var t))
            { t.TakeDamage(dmg, poiseDmg, transform.position); if (_sfx != null) _sfx.PlayOver(clashClip); return; }   // SFX: Mimic lunge connected
    }

    // ───────── IDamageable ─────────
    public void TakeDamage(float damage, float poiseDamage, Vector3 attackerPos)
    {
        if (_dead || Time.time < _invulnUntil) return;
        if (Time.time < _hurtUntil) return;
        _hurtUntil = Time.time + hitCooldown;

        if (_blocking)   // GUARD up -> fill poise bar, NO hp (poiseDamage now counts, so strong attacks break the guard faster)
        {
            if (_sfx != null) _sfx.PlayOver(clashClip);   // SFX: Mimic blocked our attack
            _blockMeter += damage + poiseDamage;
            if (++_guardHits >= blockBreakHits) _wantBlockBreak = true;   // blocked 2+ this guard -> break out with a dodge
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
        if (!_healUsed && _hp <= maxHP * healHpThreshold) _wantHeal = true;   // drop everything & heal (brain picks it up next tick)
        if (!_busy)   // super-armor during attacks
        {
            if (poiseDamage >= poise) StartCoroutine(Stagger());
            else anim?.SetTrigger("Hit");
        }
    }

    // IStaggerable — the Mimic is parryable whenever its hit connects, EXCEPT its last-resort desperation lunge
    public bool CanBeParried => _parryable && !_specialActive;   // only during each swing's [parryStart,parryEnd) frames; never the lunge

    // IStaggerable — PARRIED: take poise (no HP), then play the stagger (deaf for its duration)
    public void Stagger(float poiseDamage)
    {
        if (_dead || _staggered) return;
        _blockMeter = Mathf.Min(blockMeterMax, _blockMeter + poiseDamage);
        StartCoroutine(Stagger());   // every parry -> full stagger, interrupts whatever it was doing
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
        // drive the walk/idle blend from the agent's STEERING intent, not noisy actual velocity -> no flicker
        bool moving = _agent != null && _agent.desiredVelocity.sqrMagnitude > 0.05f;
        anim.SetFloat("Speed", moving ? 1f : 0f, 0.12f, Time.deltaTime);
    }

    IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.deltaTime; yield return null; } }

    void DrawHitCapsule(float reach, Color c)
    {
        Gizmos.color = c;
        Vector3 center = transform.position + transform.forward * reach + Vector3.up * hitHeight;
        Vector3 half   = transform.forward * (hitLength * 0.5f);
        Vector3 p0 = center - half, p1 = center + half;
        Gizmos.DrawWireSphere(p0, hitRadius);
        if (hitLength > 0.001f)
        {
            Gizmos.DrawWireSphere(p1, hitRadius);
            Vector3 r = transform.right * hitRadius, u = transform.up * hitRadius;
            Gizmos.DrawLine(p0 + r, p1 + r); Gizmos.DrawLine(p0 - r, p1 - r);
            Gizmos.DrawLine(p0 + u, p1 + u); Gizmos.DrawLine(p0 - u, p1 - u);
        }
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, meleeRange);
        Gizmos.color = Color.cyan;   Gizmos.DrawWireSphere(transform.position, farRange);
        DrawHitCapsule(attackAReach, new Color(1f,0.85f,0f,0.9f));
        DrawHitCapsule(otherReach,   new Color(1f,0.45f,0f,0.9f));
        DrawHitCapsule(spearCReach,  Color.red);

        // desperation-lunge sweep box (purple) — only live during the lunge strike, drawn here for tuning
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position + transform.forward * (specialSweepLength * 0.5f) + Vector3.up * 1.0f, transform.rotation, Vector3.one);
        Gizmos.color  = new Color(0.7f, 0f, 1f, 0.6f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(specialSweepWidth, 5f, specialSweepLength));
        Gizmos.matrix = prev;
    }
}
