using UnityEngine;
using UnityEngine.InputSystem;

[ExecuteAlways]
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Framing")]
    public float distance = 3f;                 // back from the pivot
    public Vector3 offset = new Vector3(1f, 0f, 0f); // yaw-relative: x=right shoulder, y=up, z=fwd
    public float height = 0.3f;                  // extra camera lift (tilts the angle down)
    public float verticalDeadzone = 10f;         // vertical follow only kicks in past this gap

    [Header("Look")]
    public float sensitivity = 0.4f;
    public float defaultPitch = 8f;
    public float minPitch = -20f;
    public float maxPitch = 60f;

    float yaw, pitch, followY, torsoUp;
    bool init;

    void OnEnable() { yaw = transform.eulerAngles.y; pitch = defaultPitch; init = false; }

    void ComputeTorso()
    {
        torsoUp = 1f;
        var rs = target.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (rs.Length == 0) return;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        torsoUp = b.center.y - target.position.y;   // mid-torso, body only (ignores sword)
    }

    void LateUpdate()
    {
        if (!target) return;
        if (!init) { ComputeTorso(); followY = target.position.y + torsoUp; init = true; }

        if (Application.isPlaying)
        {
            var m = Mouse.current;
            if (m != null)
            {
                Vector2 d = m.delta.ReadValue();
                yaw += d.x * sensitivity;
                pitch = Mathf.Clamp(pitch - d.y * sensitivity, minPitch, maxPitch);
            }
        }

        // pivot = auto mid-torso: follows X/Z always, Y only past the deadzone
        float torsoY = target.position.y + torsoUp;
        if (Mathf.Abs(torsoY - followY) > verticalDeadzone)
            followY = torsoY - Mathf.Sign(torsoY - followY) * verticalDeadzone;
        Vector3 pivot = new Vector3(target.position.x, followY, target.position.z);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 look = pivot + rot * offset;
        transform.position = look - rot * Vector3.forward * distance + Vector3.up * height;
        transform.rotation = rot;
    }
}
