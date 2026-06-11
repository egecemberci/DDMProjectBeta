using UnityEngine;
using System.Collections;

// ============================================================================
// MINION AUDIO  —  drives the three minion SFX with strict priority rules.
//
// Sits on the Minion (Enemy.prefab) next to MinionEnemy. MinionEnemy calls
// PlayAttack() / PlayHurt() / OnDeath() at the right moments, and this reads
// MinionEnemy.IsAggro to gate the idle laugh.
//
// Three runtime AudioSources are created in Awake (one per channel) so hurt can
// pause attack/laugh and play OVER them simultaneously. Assign the three clips
// on the prefab; everything else (volume, 3D rolloff, timings) is exposed below.
//
//   minionAttackSound  (floraphonic wee, ~3s) — on attack start. Plays to the
//       end, then a cooldown before it can fire again. While locked it is DEAF
//       to new attack calls; never overlaps / never interrupts itself.
//   hurt (slap)        — on damage. Interrupts: PAUSES attack+laugh, plays over
//       them, cannot itself be interrupted, plays to completion. Rapid hits
//       QUEUE and play back-to-back with a gap. After the queue, paused clips
//       resume. On death every other sound is killed but the last PLAYED hurt
//       is detached and allowed to finish.
//   laugh (cutie)      — random idle chatter, ONLY while NOT aggroed. Delay is
//       biased toward the short end.
// ============================================================================
[DisallowMultipleComponent]
public class MinionAudio : MonoBehaviour
{
    [Header("Clips")]
    public AudioClip attackClip;   // floraphonic-cute-character-wee  (minionAttackSound)
    public AudioClip hurtClip;     // homemade_sfx-slap-hurt-pain
    public AudioClip laughClip;    // freesound_community-cutie-laugh
    [Tooltip("Final pain sound on death. Falls back to hurtClip if left empty.")]
    public AudioClip deathClip;    // one last pain cry when the minion dies

    [Header("Attack sound (minionAttackSound)")]
    [Tooltip("Cooldown AFTER the attack clip finishes before it may play again.")]
    public float attackCooldownAfter = 1.5f;
    [Range(0f, 1f)] public float attackVolume = 1f;

    [Header("Hurt sound")]
    [Tooltip("Gap after each hurt before the next queued/new hurt may start.")]
    public float hurtCooldown = 0.2f;
    [Range(0f, 1f)] public float hurtVolume = 1f;

    [Header("Idle laugh (only while NOT aggroed)")]
    public float laughDelayMin   = 0.07f;
    public float laughDelayMax    = 2.0f;
    [Tooltip("Delays below this count as 'short'.")]
    public float laughDelayPivot = 1.0f;
    [Tooltip("Probability the next delay is in [min, pivot) (the short bias).")]
    [Range(0f, 1f)] public float laughShortBias = 0.6f;
    [Range(0f, 1f)] public float laughVolume = 1f;

    [Header("3D sound settings (shared by all three sources)")]
    [Range(0f, 1f)] public float spatialBlend = 1f;   // 1 = fully positional
    public float minDistance = 5f;
    public float maxDistance = 60f;

    // ── runtime ──
    AudioSource _attackSrc, _hurtSrc, _laughSrc;
    MinionEnemy _minion;
    bool _dead;

    bool _attackLocked;                 // attack playing OR in post-cooldown -> deaf
    bool _attackPaused, _laughPaused;   // paused by an active hurt

    int  _hurtQueued;                   // pending hurt plays
    bool _hurtRunning;                  // hurt pump active

    void Awake()
    {
        _minion    = GetComponent<MinionEnemy>();
        _attackSrc = NewSource();
        _hurtSrc   = NewSource();
        _laughSrc  = NewSource();
        StartCoroutine(LaughLoop());
    }

    AudioSource NewSource()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = false;
        s.spatialBlend = spatialBlend;
        s.minDistance = minDistance;
        s.maxDistance = maxDistance;
        s.rolloffMode = AudioRolloffMode.Linear;
        return s;
    }

    // ── ATTACK ───────────────────────────────────────────────────────────────
    // Called when the attack animation starts. Deaf while playing or cooling down.
    public void PlayAttack()
    {
        if (_dead || _attackLocked || attackClip == null) return;
        StartCoroutine(AttackRoutine());
    }

    IEnumerator AttackRoutine()
    {
        _attackLocked = true;
        _attackSrc.clip   = attackClip;
        _attackSrc.volume = attackVolume;
        _attackSrc.Play();
        // wait for the clip to finish — paused time (by a hurt) extends this naturally
        while ((_attackSrc.isPlaying || _attackPaused) && !_dead) yield return null;
        if (_dead) { _attackLocked = false; yield break; }
        yield return new WaitForSeconds(attackCooldownAfter);
        _attackLocked = false;
    }

    // ── HURT ─────────────────────────────────────────────────────────────────
    // Called on every damage event. Interrupts + queues; uninterruptible.
    public void PlayHurt()
    {
        if (hurtClip == null) return;
        _hurtQueued++;
        if (!_hurtRunning) StartCoroutine(HurtPump());
    }

    IEnumerator HurtPump()
    {
        _hurtRunning = true;
        PauseOthersForHurt();                 // duck attack + laugh for the whole burst
        while (_hurtQueued > 0 && !_dead)
        {
            _hurtQueued--;
            _hurtSrc.clip   = hurtClip;
            _hurtSrc.volume = hurtVolume;
            _hurtSrc.Play();
            while (_hurtSrc.isPlaying && !_dead) yield return null;   // play to completion
            if (_dead) break;
            yield return new WaitForSeconds(hurtCooldown);            // gap before next
        }
        _hurtRunning = false;
        ResumeOthersAfterHurt();              // restore the paused clips (no-op if dead)
    }

    void PauseOthersForHurt()
    {
        if (_attackSrc.isPlaying) { _attackSrc.Pause(); _attackPaused = true; }
        if (_laughSrc.isPlaying)  { _laughSrc.Pause();  _laughPaused  = true; }
    }

    void ResumeOthersAfterHurt()
    {
        if (_dead) return;
        if (_attackPaused) { _attackSrc.UnPause(); _attackPaused = false; }
        if (_laughPaused)  { _laughSrc.UnPause();  _laughPaused  = false; }
    }

    // ── LAUGH ────────────────────────────────────────────────────────────────
    IEnumerator LaughLoop()
    {
        while (!_dead)
        {
            float delay = (Random.value < laughShortBias)
                ? Random.Range(laughDelayMin, laughDelayPivot)
                : Random.Range(laughDelayPivot, laughDelayMax);
            yield return new WaitForSeconds(delay);
            if (_dead) yield break;

            bool aggro = _minion != null && _minion.IsAggro;
            if (aggro || _hurtRunning || laughClip == null) continue;   // not while engaged / hurt

            _laughSrc.clip   = laughClip;
            _laughSrc.volume = laughVolume;
            _laughSrc.Play();
            // wait out this laugh (incl. any hurt-pause) before timing the next interval
            while ((_laughSrc.isPlaying || _laughPaused) && !_dead) yield return null;
        }
    }

    // ── DEATH ────────────────────────────────────────────────────────────────
    // Interrupt + kill EVERY sound (attack, laugh, any playing/queued hurt), then
    // fire one final pain cry on a throwaway object so it rides out to completion
    // even after this minion is Destroyed.
    public void OnDeath()
    {
        if (_dead) return;
        _dead = true;
        _hurtQueued = 0;

        if (_attackSrc) _attackSrc.Stop();
        if (_laughSrc)  _laughSrc.Stop();
        if (_hurtSrc)   _hurtSrc.Stop();
        _attackPaused = _laughPaused = false;

        var clip = deathClip != null ? deathClip : hurtClip;
        if (clip != null) PlayDetached(clip);
    }

    // plays a clip on a standalone object that outlives this minion, then self-destroys
    void PlayDetached(AudioClip clip)
    {
        var go = new GameObject("MinionSFX_Death");
        go.transform.position = transform.position;
        var a = go.AddComponent<AudioSource>();
        a.clip = clip; a.volume = hurtVolume;
        a.spatialBlend = spatialBlend; a.minDistance = minDistance;
        a.maxDistance = maxDistance;   a.rolloffMode = AudioRolloffMode.Linear;
        a.Play();
        Object.Destroy(go, clip.length + 0.1f);
    }
}
