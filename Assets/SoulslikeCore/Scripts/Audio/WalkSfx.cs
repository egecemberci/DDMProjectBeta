using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================================
// WALK SFX  —  continuous footstep / run loop with press-hold + pause/resume.
//
// The owning character calls Report(moving, sprinting) once per frame. The state
// machine:
//   • Fresh: must be moving CONTINUOUSLY for `startThreshold` (0.5s) before the
//     loop starts (from the beginning). Brief taps make no sound.
//   • Moving: the loop plays (looped). Walk vs run clip is chosen live.
//   • Stop moving: wait `releaseDelay` (0.15s), then PAUSE the clip and open a
//     `resumeWindow` (3s). Move again within it -> resume from where it paused.
//   • No movement for the whole window -> reset to Fresh (next start re-arms the
//     0.5s threshold and restarts the clip from 0).
//
// CLIP SELECTION (level-aware; hardcoding scene names is fine — few levels):
//   walkClip plays in the scenes listed in walkClipLevels; otherwise altWalkClip
//   plays in altWalkClipLevels. runClip is the sprint sound in ALL levels.
//   (An empty level list = "applies to every level". A null clip = silent.)
// ============================================================================
[DisallowMultipleComponent]
public class WalkSfx : MonoBehaviour
{
    [Header("Clips")]
    public AudioClip walkClip;                  // primary walk loop
    public string    walkClipLevels = "";       // comma-separated scene names (empty = all levels)
    public AudioClip altWalkClip;               // secondary walk loop for other levels
    public string    altWalkClipLevels = "";    // comma-separated scene names
    public AudioClip runClip;                   // sprint loop (all levels)

    [Header("Mix")]
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Volume for the sprint (run) clip. -1 = use the same volume as walking.")]
    public float runVolume = -1f;
    [Range(0f, 1f)] public float spatialBlend = 1f;   // 0 = 2D, 1 = positional
    public float minDistance = 5f;
    public float maxDistance = 40f;

    [Header("Timing")]
    public float startThreshold = 0.5f;   // continuous movement needed before a fresh loop starts
    public float releaseDelay    = 0.15f;  // after movement stops, wait this long, then pause
    public float resumeWindow   = 3f;     // after pausing, resume if movement returns within this; else reset

    enum State { Fresh, Moving, Releasing, Paused }
    State _state = State.Fresh;
    float _holdTimer, _releaseTimer, _pausedTimer;

    AudioSource _src;
    AudioClip   _levelWalkClip;   // the walk clip active in THIS scene (resolved once)

    void Awake()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = true;                  // loops reset to start when they end
        _src.spatialBlend = spatialBlend;
        _src.minDistance = minDistance;
        _src.maxDistance = maxDistance;
        _src.rolloffMode = AudioRolloffMode.Linear;
        _src.volume = volume;
    }

    void Start()
    {
        string scene = SceneManager.GetActiveScene().name;
        if      (walkClip    != null && LevelMatches(walkClipLevels,    scene)) _levelWalkClip = walkClip;
        else if (altWalkClip != null && LevelMatches(altWalkClipLevels, scene)) _levelWalkClip = altWalkClip;
        else _levelWalkClip = null;
    }

    static bool LevelMatches(string csv, string scene)
    {
        if (string.IsNullOrWhiteSpace(csv)) return true;   // no list -> every level
        foreach (var part in csv.Split(','))
            if (string.Equals(part.Trim(), scene, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    AudioClip Desired(bool sprinting)
    {
        if (sprinting && runClip != null) return runClip;
        return _levelWalkClip;   // may be null (no walk sound in this level for this character)
    }

    void PlayFromStart(AudioClip clip)
    {
        _src.clip = clip;
        _src.loop = true;
        _src.volume = (clip == runClip && runVolume >= 0f) ? runVolume : volume;   // sprint clip can have its own level
        _src.time = 0f;
        _src.Play();
    }

    // Called once per frame by the owning character.
    public void Report(bool moving, bool sprinting)
    {
        if (_src == null) return;
        float dt = Time.deltaTime;
        AudioClip want = Desired(sprinting);

        switch (_state)
        {
            case State.Fresh:
                if (moving && want != null)
                {
                    _holdTimer += dt;
                    if (_holdTimer >= startThreshold) { PlayFromStart(want); _state = State.Moving; }
                }
                else _holdTimer = 0f;
                break;

            case State.Moving:
                if (!moving) { _state = State.Releasing; _releaseTimer = 0f; break; }
                if (want == null) { _src.Stop(); _state = State.Fresh; _holdTimer = 0f; }      // no clip for this mode/level
                else if (_src.clip != want) PlayFromStart(want);                                // walk <-> run switch
                break;

            case State.Releasing:
                if (moving)
                {
                    if (want == null) { _src.Stop(); _state = State.Fresh; _holdTimer = 0f; }
                    else { if (_src.clip != want) PlayFromStart(want); _state = State.Moving; } // resumed before the pause
                }
                else
                {
                    _releaseTimer += dt;
                    if (_releaseTimer >= releaseDelay) { _src.Pause(); _state = State.Paused; _pausedTimer = 0f; }
                }
                break;

            case State.Paused:
                if (moving)
                {
                    if (want == null) { _src.Stop(); _state = State.Fresh; _holdTimer = 0f; }
                    else if (_src.clip == want) { _src.UnPause(); _state = State.Moving; }       // continue from paused spot
                    else { PlayFromStart(want); _state = State.Moving; }                          // clip changed -> restart
                }
                else
                {
                    _pausedTimer += dt;
                    if (_pausedTimer >= resumeWindow) { _src.Stop(); _state = State.Fresh; _holdTimer = 0f; }  // full reset
                }
                break;
        }
    }
}
