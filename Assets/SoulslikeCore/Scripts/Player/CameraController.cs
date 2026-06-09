using UnityEngine;

// Spherical-orbit third-person camera: constant distance to a torso pivot,
// right-shoulder framing, wide pitch range, with map collision pull-in.
public class CameraController : MonoBehaviour
{
    [Header("Pivot (follow point on the character — torso)")]
    public Transform cameraTarget;

    [Header("Framing (world metres)")]
    public float distance     = 2.0f;   // constant orbit radius (same at every angle)
    public float shoulder     = 0.5f;   // right offset (+ = right)
    public float heightOffset = 0.25f;  // extra vertical nudge of the camera

    [Header("Look")]
    public float horizontalSpeed = 0.6f;
    public float verticalSpeed   = 0.45f;
    public float minPitch    = -80f;    // look UP limit
    public float maxPitch    =  80f;    // look DOWN limit
    public float defaultPitch =  8f;    // resting downward tilt (frames whole body)
    public float lockFollowSpeed = 8f;  // how fast the camera swings behind player toward the locked target
    public float lockPitch       = 10f; // pitch while locked on

    [Header("Collision (pull camera in through walls/ground)")]
    public LayerMask collisionMask = ~0; // surfaces that block the camera
    public float collisionRadius = 0.2f; // camera probe sphere
    public float collisionSkin   = 0.15f;// keep this far off the surface
    public float minDistance     = 0.4f; // never closer to pivot than this

    PlayerInputHandler _input;
    LockOnSystem       _lockOn;
    float _yaw, _pitch;
    int   _warmupFrames;       // ignore look input briefly so the play-start mouse-delta spike can't throw the pitch

    void Awake()
    {
        _input  = FindAnyObjectByType<PlayerInputHandler>();
        _lockOn = FindAnyObjectByType<LockOnSystem>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        _pitch = defaultPitch;
        _warmupFrames = 5;
        if (cameraTarget != null) _yaw = cameraTarget.eulerAngles.y;
    }

    void LateUpdate()
    {
        if (cameraTarget == null) return;

        if (_warmupFrames > 0)
        {
            _warmupFrames--;
            _pitch = defaultPitch;   // discard the first-frame mouse-delta spike on play start
        }
        else if (_lockOn != null && _lockOn.IsLockedOn && _lockOn.LockedTarget != null)
        {
            // swing behind the player to face the locked target
            Vector3 toT = _lockOn.LockedTarget.position - _lockOn.transform.position; toT.y = 0f;
            if (toT.sqrMagnitude > 0.01f)
                _yaw = Mathf.LerpAngle(_yaw, Quaternion.LookRotation(toT).eulerAngles.y, lockFollowSpeed * Time.deltaTime);
            _pitch = Mathf.Lerp(_pitch, lockPitch, lockFollowSpeed * Time.deltaTime);
        }
        else
        {
            Vector2 look = _input != null ? _input.LookInput : Vector2.zero;
            _yaw   += look.x * horizontalSpeed;
            _pitch -= look.y * verticalSpeed;
            _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        Quaternion rot   = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 fwd      = rot * Vector3.forward;
        Vector3 origin   = cameraTarget.position + rot * Vector3.right * shoulder + Vector3.up * heightOffset;

        // constant-radius orbit, then pull in if a surface is between pivot and camera
        float dist = ResolveCollision(origin, -fwd, distance);

        transform.position = origin - fwd * dist;
        transform.rotation = rot;
    }

    float ResolveCollision(Vector3 origin, Vector3 dir, float desired)
    {
        Transform self = cameraTarget != null ? cameraTarget.root : null;
        var hits = Physics.SphereCastAll(origin, collisionRadius, dir, desired,
                                         collisionMask, QueryTriggerInteraction.Ignore);
        float best = desired;
        foreach (var h in hits)
        {
            if (h.distance <= 0f) continue;                              // started overlapping
            var col = h.collider;
            if (self != null && col.transform.IsChildOf(self)) continue; // ignore the player rig
            if (col.CompareTag("Enemy") || col.CompareTag("NPC")) continue; // NPCs/enemies may sit between
            if (h.distance < best) best = h.distance;
        }
        return Mathf.Max(best - collisionSkin, minDistance);
    }
}
