using UnityEngine;

// ============================================================================
// COMBAT SFX  —  tiny shared one-shot player for melee feedback sounds.
//
// Drop one on any character (Player / Mimic / BossKatana). Two channels:
//   PlayOver(clip)        — layered one-shot (PlayOneShot). For dramatic stings
//                           that should ride over whatever else is going on
//                           (parry clash, the boss swing whoosh).
//   PlayGated(clip, cd)   — single source that NEVER overlaps or interrupts
//                           itself, and won't retrigger until `cd` seconds after
//                           the last trigger. For rapid hit/clash sounds.
//
// Two AudioSources are created at runtime so a gated hit and a layered sting can
// coexist. 3D settings are shared and exposed for tuning.
// ============================================================================
[DisallowMultipleComponent]
public class CombatSfx : MonoBehaviour
{
    [Header("3D sound settings (shared by both channels)")]
    [Range(0f, 1f)] public float spatialBlend = 1f;   // 1 = fully positional, 0 = 2D
    public float minDistance = 5f;
    public float maxDistance = 60f;
    [Range(0f, 1f)] public float volume = 1f;

    AudioSource _oneShot;   // layered stings (PlayOneShot)
    AudioSource _gated;     // no-overlap hit sounds
    AudioSource _swing;     // stoppable swing channel (can be interrupted mid-clip)
    float _gateUntil;
    float _swingFreeAt;     // earliest time a new swing may start (scheduled/forced end + end-delay)

    void Awake()
    {
        _oneShot = NewSource();
        _gated   = NewSource();
        _swing   = NewSource();
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

    // Layered one-shot — plays over anything, can stack. Use for parry/swing stings.
    public void PlayOver(AudioClip clip, float vol = 1f)
    {
        if (clip == null || _oneShot == null) return;
        _oneShot.PlayOneShot(clip, Mathf.Clamp01(volume * vol));
    }

    // Gated one-shot — won't overlap/interrupt itself and won't retrigger for `cooldown`s.
    public void PlayGated(AudioClip clip, float cooldown, float vol = 1f)
    {
        if (clip == null || _gated == null) return;
        if (_gated.isPlaying || Time.time < _gateUntil) return;
        _gated.clip = clip;
        _gated.volume = Mathf.Clamp01(volume * vol);
        _gated.Play();
        _gateUntil = Time.time + cooldown;
    }

    // Stoppable swing channel. No overlap, NO queue: a call is dropped while one is
    // playing or within `endDelay`s of the last one ending. Can be cut mid-clip by
    // StopSwing (e.g. a parry), which also re-arms the same end-delay cooldown.
    public bool IsSwinging => _swing != null && _swing.isPlaying;

    public void PlaySwing(AudioClip clip, float endDelay, float vol = 1f)
    {
        if (clip == null || _swing == null) return;
        if (_swing.isPlaying || Time.time < _swingFreeAt) return;   // no overlap, no queue, respect cooldown
        _swing.clip = clip;
        _swing.volume = Mathf.Clamp01(volume * vol);
        _swing.Play();
        _swingFreeAt = Time.time + clip.length + endDelay;          // cooldown measured from the clip's natural end
    }

    public void StopSwing(float endDelay = 0f)
    {
        if (_swing == null) return;
        if (_swing.isPlaying) _swing.Stop();
        _swingFreeAt = Time.time + endDelay;                        // re-arm the cooldown from the interruption point
    }
}
