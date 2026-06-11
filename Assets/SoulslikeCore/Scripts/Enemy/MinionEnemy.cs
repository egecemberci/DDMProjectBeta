using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System.Collections;
using System.Collections.Generic;

// The normal "minion" enemy — patrol + sightline aggro + leashed combat.
//
// PATROL (default): wanders within wanderRadius of its post (home), drifting toward other minions
//   within seekRadius so they cluster loosely but never abandon their post.
// AGGRO: only if it has line-of-sight to the player's HEAD AND the player is within aggroRange.
//   De-aggros the moment the player leaves aggroRange.
// LEASH: it will chase in combat, but if dragged past leashMax from home it RETREATS home at
//   retreatSpeed (ignoring the player — un-cheesable) until it's back within leashReturn. So a
//   player can lure it out to fight, but can't walk it across the map.
//
// ATTACKS: chunks of hostilenpcattack — attack1/2/3 = frames 0-50/50-100/100-150.
//   0 other enemies within proximityRange -> 1,2,3 ; 1-2 -> 1,(0-0.3s),2 ; 3+ -> 1. 1.5s combo cooldown.
//   Parryable; no poise -> a parry just twitches it (0.1s walk) then freezes it (0.5s).
// Animation is driven directly by a PlayableGraph (walk + attack clips) — no AnimatorController.
[RequireComponent(typeof(NavMeshAgent))]
public class MinionEnemy : MonoBehaviour, IDamageable, IStaggerable
{
    [Header("Stats")]
    public float maxHP        = 60f;
    public float moveSpeed    = 10f;     // combat chase speed
    public float attackDamage = 10f;
    public float attackPoiseToPlayer = 12f;

    [Header("Aggro (sightline to head + range)")]
    public float aggroRange       = 20f;   // player must be within this AND visible to aggro / stay aggroed
    public float eyeHeight        = 1.5f;  // minion eye origin for the LOS ray
    public float playerHeadHeight = 5.5f;  // fallback head height (used if no head bone is found)

    [Header("Patrol / post")]
    public float wanderRadius = 10f;       // STRICT: wandering stays within this of home
    public float seekRadius   = 20f;       // drift toward other minions within this
    public float patrolSpeed  = 3.5f;
    public float wanderPause  = 1.5f;
    [Range(0f,1f)] public float seekBias = 0.6f;

    [Header("Leash (can't be walked off the post)")]
    public float leashMax     = 50f;       // past this from home -> retreat
    public float leashReturn  = 35f;       // retreat until back within this of home
    public float retreatSpeed = 15f;

    [Header("Combat ranges")]
    public float attackRange    = 3f;
    public float proximityRange = 12.5f;   // count OTHER enemies within this for the combo decision
    public float hitReach       = 4f;
    public float hitRadius      = 0.8f;
    public float hitHeight      = 1f;

    [Header("Attack hurtbox / parry (frames within each 50-frame chunk)")]
    public float hurtStartFrame  = 35f;   // hurtbox LIVE from this frame ...
    public float hurtEndFrame    = 50f;   // ... to this
    public float parryStartFrame = 40f;   // parryable from this frame ...
    public float parryEndFrame   = 50f;   // ... to this

    [Header("Combo")]
    public float comboCooldown  = 1.5f;
    public float maxRandomDelay = 0.3f;

    [Header("Attack clip chunks")]
    public float attackFps   = 30f;
    public int   chunkFrames = 50;

    [Header("Facing")]
    public float modelYawOffset = 0f;      // the 90° base correction is baked into the "Model" wrapper child now
    public float turnSpeed      = 14f;

    [Header("Parry / stagger (no poise)")]
    public float staggerSnippet = 0.1f;
    public float staggerStill   = 0.5f;

    [Header("Clips (npc FBX clips)")]
    public AnimationClip walkClip;     // hostilenpcwalk
    public AnimationClip attackClip;   // hostilenpcattack

    // runtime
    NavMeshAgent _agent;
    Animator     _anim;
    Transform    _player, _playerHead;
    PlayerBlock  _playerBlock;
    PlayerDodge  _playerDodge;
    Vector3      _home, _wanderTarget;
    PlayableGraph _graph;
    AnimationMixerPlayable _mixer;
    AnimationClipPlayable  _walkP, _attackP;
    float _hp, _comboReadyAt, _wanderTimer;
    bool  _dead, _attacking, _staggered, _parryable, _moving, _aggroed, _retreating;
    MinionAudio _audio;
    WalkSfx     _walkSfx;

    public bool CanBeParried => _parryable;
    public bool IsAggro => _aggroed;          // read by MinionAudio to gate the idle laugh

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _anim  = GetComponent<Animator>();
        if (_anim == null) _anim = GetComponentInChildren<Animator>();
        _audio = GetComponent<MinionAudio>();
        _walkSfx = GetComponent<WalkSfx>();
        _hp = maxHP;
        _home = transform.position;
        if (_agent != null)
        {
            _agent.speed = moveSpeed;
            _agent.updateRotation = false;          // we face direction ourselves (with the yaw offset)
            _agent.stoppingDistance = attackRange * 0.9f;
        }
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            _player = p.transform;
            _playerBlock = p.GetComponent<PlayerBlock>();
            _playerDodge = p.GetComponent<PlayerDodge>();
            foreach (var t in p.GetComponentsInChildren<Transform>())
                if (t.name.ToLower().Contains("head")) { _playerHead = t; break; }
        }
        BuildGraph();
    }

    void BuildGraph()
    {
        if (_anim == null || walkClip == null || attackClip == null) return;
        _anim.runtimeAnimatorController = null;
        _graph   = PlayableGraph.Create("MinionAnim_" + GetInstanceID());
        var output = AnimationPlayableOutput.Create(_graph, "out", _anim);
        _mixer   = AnimationMixerPlayable.Create(_graph, 2);
        _walkP   = AnimationClipPlayable.Create(_graph, walkClip);
        _attackP = AnimationClipPlayable.Create(_graph, attackClip);
        _walkP.SetApplyFootIK(false); _attackP.SetApplyFootIK(false);
        _graph.Connect(_walkP, 0, _mixer, 0);
        _graph.Connect(_attackP, 0, _mixer, 1);
        output.SetSourcePlayable(_mixer);
        _mixer.SetInputWeight(0, 1f);
        _mixer.SetInputWeight(1, 0f);
        _walkP.SetSpeed(0); _attackP.SetSpeed(0);
        _graph.Play();
    }

    void Start() { StartCoroutine(SnapToNavMesh()); }

    // placing a prefab doesn't drop it onto the navmesh — if we spawn slightly off/above it, warp on
    IEnumerator SnapToNavMesh()
    {
        yield return null;                                   // let the agent initialise
        if (_agent == null || !_agent.enabled) yield break;
        if (_agent.isOnNavMesh) yield break;
        if (NavMesh.SamplePosition(transform.position, out var hit, 60f, NavMesh.AllAreas))
        {
            _agent.Warp(hit.position);
            _home = hit.position;                            // anchor the post to the valid navmesh point
        }
    }

    void OnDestroy() { if (_graph.IsValid()) _graph.Destroy(); }

    void Update()
    {
        if (_walkSfx != null)
        {
            bool moving = !_dead && _agent != null && _agent.isOnNavMesh && _agent.velocity.sqrMagnitude > 0.05f;
            bool sprinting = moving && _agent != null && _agent.speed > patrolSpeed + 0.1f;   // chase/combat = faster than patrol
            _walkSfx.Report(moving, sprinting);
        }
        if (_dead || _player == null) return;
        if (_staggered) return;

        LoopWalk();
        float homeDist = Vector3.Distance(transform.position, _home);

        // ── RETREAT (leash) — run home, ignore the player, until back within leashReturn ──
        if (_retreating)
        {
            _aggroed = false; _attacking = false; _parryable = false;
            _agent.speed = retreatSpeed;
            if (_agent.isOnNavMesh) _agent.SetDestination(_home);
            SetMoving(_agent.velocity.sqrMagnitude > 0.05f);
            FaceDir(_agent.velocity);
            if (homeDist <= leashReturn) { _retreating = false; _agent.speed = moveSpeed; }
            return;
        }
        if (homeDist >= leashMax) { _retreating = true; return; }   // dragged too far -> retreat

        // ── AGGRO management ──
        float pd = Dist();
        if (_aggroed) { if (pd > aggroRange) _aggroed = false; }                 // left proximity -> drop
        else if (pd <= aggroRange && HasLOSToHead()) _aggroed = true;            // see the head + in range -> aggro

        // ── COMBAT ──
        if (_aggroed)
        {
            _agent.speed = moveSpeed;
            FaceDir(_player.position - transform.position);
            if (_attacking) return;
            if (pd > attackRange)
            {
                SetMoving(true);
                if (_agent.isOnNavMesh) _agent.SetDestination(_player.position);
            }
            else
            {
                SetMoving(false);
                if (_agent.isOnNavMesh) _agent.ResetPath();
                if (Time.time >= _comboReadyAt) StartCoroutine(DoCombo());
            }
            return;
        }

        // ── PATROL ──
        Patrol(homeDist);
    }

    void Patrol(float homeDist)
    {
        // too far from post (e.g. just de-aggroed after a chase) -> walk straight back first
        if (homeDist > wanderRadius * 1.2f)
        {
            _agent.speed = moveSpeed;
            if (_agent.isOnNavMesh) _agent.SetDestination(_home);
            bool mv = _agent.velocity.sqrMagnitude > 0.05f;
            SetMoving(mv); if (mv) FaceDir(_agent.velocity);
            _wanderTimer = 0f;
            return;
        }

        _agent.speed = patrolSpeed;
        _wanderTimer -= Time.deltaTime;
        bool reached = _agent.isOnNavMesh && !_agent.pathPending &&
                       _agent.remainingDistance <= _agent.stoppingDistance + 0.4f;
        if (_wanderTimer <= 0f || reached)
        {
            _wanderTarget = PickWanderTarget();
            if (_agent.isOnNavMesh) _agent.SetDestination(_wanderTarget);
            _wanderTimer = wanderPause + Random.Range(0f, 2f);
        }
        bool moving = _agent.isOnNavMesh && _agent.velocity.sqrMagnitude > 0.05f;
        SetMoving(moving);
        if (moving) FaceDir(_agent.velocity);
    }

    // a random point within wanderRadius of home, biased toward where nearby minions are
    Vector3 PickWanderTarget()
    {
        Vector2 r = Random.insideUnitCircle;
        Vector3 dir = new Vector3(r.x, 0f, r.y);
        Vector3 seek = NearbyMinionDir();
        if (seek != Vector3.zero) dir = Vector3.Lerp(dir.normalized, seek, seekBias);
        if (dir.sqrMagnitude < 0.001f) { var s = Random.insideUnitCircle.normalized; dir = new Vector3(s.x, 0, s.y); }
        return _home + dir.normalized * Random.Range(wanderRadius * 0.25f, wanderRadius);
    }

    // direction (from home) toward the average position of other minions within seekRadius
    Vector3 NearbyMinionDir()
    {
        var roots = new HashSet<Transform>();
        Vector3 sum = Vector3.zero;
        foreach (var h in Physics.OverlapSphere(transform.position, seekRadius))
        {
            if (!h.CompareTag("Enemy")) continue;
            var rt = h.transform.root;
            if (rt == transform.root || roots.Contains(rt)) continue;
            roots.Add(rt); sum += rt.position;
        }
        if (roots.Count == 0) return Vector3.zero;
        Vector3 d = (sum / roots.Count) - _home; d.y = 0f;
        return d.sqrMagnitude < 0.001f ? Vector3.zero : d.normalized;
    }

    bool HasLOSToHead()
    {
        Vector3 eye  = transform.position + Vector3.up * eyeHeight;
        Vector3 head = _playerHead != null ? _playerHead.position : _player.position + Vector3.up * playerHeadHeight;
        Vector3 d = head - eye; float dist = d.magnitude;
        if (dist < 0.01f) return true;
        foreach (var h in Physics.RaycastAll(eye, d / dist, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            var rt = h.collider.transform.root;
            if (rt == transform.root) continue;     // ignore self
            if (rt == _player) continue;             // the player isn't a blocker
            if (rt.CompareTag("Enemy")) continue;    // other minions don't block sight
            return false;                            // environment blocks the head
        }
        return true;
    }

    void FaceDir(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, modelYawOffset, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    void SetMoving(bool moving)
    {
        _moving = moving;
        if (!_graph.IsValid()) return;
        _mixer.SetInputWeight(0, 1f);
        _mixer.SetInputWeight(1, 0f);
        _walkP.SetSpeed(moving ? 1f : 0f);
    }

    void LoopWalk()
    {
        if (!_graph.IsValid() || walkClip == null) return;
        if (_moving && _walkP.GetTime() >= walkClip.length)
            _walkP.SetTime(_walkP.GetTime() % walkClip.length);
    }

    IEnumerator DoCombo()
    {
        _attacking = true;
        if (_audio != null) _audio.PlayAttack();          // SFX: start the attack 'wee' as the swing begins
        int near = CountNearbyEnemies();
        if (near == 0)
        {
            yield return Chunk(0);
            if (!_staggered && !_dead) yield return Chunk(1);
            if (!_staggered && !_dead) yield return Chunk(2);
        }
        else if (near <= 2)
        {
            yield return Chunk(0);
            if (!_staggered && !_dead)
            {
                yield return new WaitForSeconds(Random.Range(0f, maxRandomDelay));
                if (!_staggered && !_dead) yield return Chunk(1);
            }
        }
        else yield return Chunk(0);

        _comboReadyAt = Time.time + comboCooldown;
        _attacking = false;
        SetMoving(false);
    }

    IEnumerator Chunk(int idx)
    {
        if (_staggered || _dead || !_graph.IsValid()) yield break;
        float chunkDur  = chunkFrames / attackFps;
        float startTime = (idx * chunkFrames) / attackFps;
        _mixer.SetInputWeight(0, 0f);
        _mixer.SetInputWeight(1, 1f);
        _attackP.SetTime(startTime);
        _attackP.SetSpeed(1f);
        bool resolved = false; float t = 0f;
        while (t < chunkDur)
        {
            if (_staggered || _dead) { _parryable = false; yield break; }
            float frame = t * attackFps;                                       // 0..chunkFrames within this chunk
            _parryable = frame >= parryStartFrame && frame <= parryEndFrame;    // parry window (frames 40-50)
            // hurtbox window (frames 35-50): resolve early if the player reacts (block/parry/i-frame)
            if (!resolved && frame >= hurtStartFrame && frame <= hurtEndFrame && PlayerInReach())
            {
                bool blocking = _playerBlock != null && _playerBlock.IsBlocking;
                bool iframe   = _playerDodge != null && _playerDodge.IsInvincible;
                if (blocking || iframe) { Strike(); resolved = true; }
            }
            if (_staggered) { _parryable = false; yield break; }               // got parried on the hit
            t += Time.deltaTime;
            yield return null;
        }
        if (!resolved) Strike();                                               // never reacted -> the hit lands (HP)
        _attackP.SetSpeed(0f);
        _parryable = false;
    }

    bool PlayerInReach() => _player != null &&
        Vector3.Distance(transform.position, _player.position) <= hitReach + hitRadius;

    void Strike()
    {
        if (_player == null) return;
        Vector3 dir = _player.position - transform.position; dir.y = 0f;
        float d = Mathf.Min(dir.magnitude, hitReach);
        Vector3 center = transform.position + dir.normalized * d + Vector3.up * hitHeight;
        foreach (var h in Physics.OverlapSphere(center, hitRadius))
            if (h.CompareTag("Player") && h.TryGetComponent<IDamageable>(out var dmg))
            {
                dmg.TakeDamage(attackDamage, attackPoiseToPlayer, transform.position);
                return;
            }
    }

    int CountNearbyEnemies()
    {
        var roots = new HashSet<Transform>();
        foreach (var h in Physics.OverlapSphere(transform.position, proximityRange))
        {
            if (!h.CompareTag("Enemy")) continue;
            var rt = h.transform.root;
            if (rt == transform.root) continue;
            roots.Add(rt);
        }
        return roots.Count;
    }

    // ── IDamageable ── no poise: damage only chips HP; only a PARRY staggers it
    public void TakeDamage(float damage, float poiseDamage, Vector3 attackerPos)
    {
        if (_dead) return;
        _hp -= damage;
        if (_audio != null) _audio.PlayHurt();            // SFX: slap — interrupts everything, queues, survives death
        if (_hp <= 0f) Die();
    }

    // ── IStaggerable ── parried: twitch (0.1s walk) then freeze-stagger (0.5s)
    public void Stagger(float poiseDamage)
    {
        if (_dead || _staggered) return;
        StartCoroutine(StaggerRoutine());
    }

    IEnumerator StaggerRoutine()
    {
        _staggered = true; _attacking = false; _parryable = false;
        if (_agent != null && _agent.isOnNavMesh) { _agent.ResetPath(); _agent.velocity = Vector3.zero; }
        if (_graph.IsValid()) { _mixer.SetInputWeight(0, 1f); _mixer.SetInputWeight(1, 0f); _walkP.SetSpeed(1f); }
        yield return new WaitForSeconds(staggerSnippet);
        if (_graph.IsValid()) _walkP.SetSpeed(0f);
        yield return new WaitForSeconds(staggerStill);
        _staggered = false;
    }

    void Die()
    {
        _dead = true; _parryable = false;
        if (_audio != null) _audio.OnDeath();             // SFX: kill all but the last-played hurt (it finishes detached)
        if (_agent != null) { if (_agent.isOnNavMesh) _agent.ResetPath(); _agent.enabled = false; }
        var col = GetComponent<Collider>(); if (col) col.enabled = false;
        if (_graph.IsValid()) _walkP.SetSpeed(0f);
        Destroy(gameObject, 0.5f);
    }

    float Dist() => _player == null ? 999f : Vector3.Distance(transform.position, _player.position);

    void OnDrawGizmosSelected()
    {
        Vector3 h = Application.isPlaying ? _home : transform.position;
        Gizmos.color = Color.green;  Gizmos.DrawWireSphere(h, wanderRadius);   // post / patrol
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(h, aggroRange);     // aggro range
        Gizmos.color = Color.red;    Gizmos.DrawWireSphere(h, leashMax);       // leash
    }
}
