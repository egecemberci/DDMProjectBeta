using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Speed")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float rotationSpeed = 10f;

    [Header("Jump")]
    public float jumpHeight = 5f;

    [Header("Gravity")]
    public float gravity = -20f;

    private CharacterController _cc;
    private PlayerStateMachine _sm;
    private PlayerInputHandler _input;
    private PlayerStats _stats;
    private LockOnSystem _lockOn;
    private Animator _anim;
    private Camera _cam;
    private Vector3 _velocity;
    private WalkSfx _walkSfx;
    private bool _sfxMoving, _sfxSprinting;   // fed to the footstep loop each frame

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _sm = GetComponent<PlayerStateMachine>();
        _input = GetComponent<PlayerInputHandler>();
        _stats = GetComponent<PlayerStats>();
        _lockOn = GetComponent<LockOnSystem>();
        _anim = GetComponentInChildren<Animator>();
        _walkSfx = GetComponent<WalkSfx>();
        _cam = Camera.main;
    }

    void Update()
    {
        HandleGravity();
        _sfxMoving = false; _sfxSprinting = false;

        if (_sm.CanAct())
        {
            if (_input.JumpPressed && _cc.isGrounded)
            {
                _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                if (_anim != null) _anim.SetTrigger("Jump");
            }

            HandleMovement();

            if (_anim != null)
                _anim.SetBool("IsGrounded", _cc.isGrounded);
        }

        if (_walkSfx != null) _walkSfx.Report(_sfxMoving, _sfxSprinting);
    }

    void HandleMovement()
    {
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
        }

        Vector2 raw = _input.MoveInput;
        if (_sm.IsAttacking()) return;
        if (raw.sqrMagnitude < 0.01f)
        {
            _sm.ChangeState(PlayerState.Idle);
            if (_anim != null)
            {
                _anim.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
                _anim.SetBool("IsRunning", false);
            }
            return;
        }

        Vector3 camForward = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
        Vector3 camRight = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up).normalized;
        Vector3 moveDir = (camForward * raw.y + camRight * raw.x).normalized;

        PlayerBlock block = GetComponent<PlayerBlock>();
        bool isBlocking = block != null && block.IsBlocking;
        bool isSprinting = _input.SprintHeld && _stats.HasStamina(5f) && !isBlocking;

        float speed = isBlocking ? walkSpeed * 0f :
                      isSprinting ? sprintSpeed :
                                    walkSpeed;

        if (isSprinting) _stats.UseStamina(8f * Time.deltaTime);

        _sfxMoving    = !isBlocking;   // footsteps only when actually moving (blocking pins speed to 0)
        _sfxSprinting = isSprinting;

        _cc.Move(moveDir * speed * Time.deltaTime);

        if (_lockOn == null || !_lockOn.IsLockedOn)
        {
            Vector3 cameraForward = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
            if (cameraForward != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(cameraForward);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }

        if (_anim != null)
        {
            _anim.SetFloat("Speed", 1f, 0.1f, Time.deltaTime);
            _anim.SetBool("IsRunning", isSprinting);
        }

        _sm.ChangeState(PlayerState.Moving);
    }

    void HandleGravity()
    {
        if (!_cc.enabled) return;

        if (_cc.isGrounded && _velocity.y < 0f) _velocity.y = -2f;
        _velocity.y += gravity * Time.deltaTime;
        _cc.Move(_velocity * Time.deltaTime);
    }
}