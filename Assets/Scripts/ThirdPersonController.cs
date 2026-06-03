using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Speed")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float rotationSpeed = 12f;
    public float gravity = -20f;

    [Header("Keybinds")]
    public Key forward = Key.W;
    public Key back = Key.S;
    public Key left = Key.A;
    public Key right = Key.D;
    public Key sprint = Key.LeftShift;

    CharacterController cc;
    Transform cam;
    float vy;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (Camera.main) cam = Camera.main.transform;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        Vector2 input = new Vector2(
            (kb[right].isPressed ? 1 : 0) - (kb[left].isPressed ? 1 : 0),
            (kb[forward].isPressed ? 1 : 0) - (kb[back].isPressed ? 1 : 0));

        Vector3 move = Vector3.zero;
        if (input.sqrMagnitude > 0.01f)
        {
            Vector3 f = cam ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 r = cam ? cam.right : Vector3.right;
            move = (f * input.y + r * input.x).normalized;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(move), rotationSpeed * Time.deltaTime);
        }

        if (cc.isGrounded && vy < 0) vy = -2f;
        vy += gravity * Time.deltaTime;

        float speed = kb[sprint].isPressed ? sprintSpeed : walkSpeed;
        cc.Move((move * speed + Vector3.up * vy) * Time.deltaTime);
    }
}
