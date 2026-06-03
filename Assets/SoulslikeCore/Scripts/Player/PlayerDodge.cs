using UnityEngine;
using System.Collections;

public class PlayerDodge : MonoBehaviour
{
    [Header("Ayarlar")]
    public float dodgeDistance  = 4f;
    public float dodgeDuration  = 0.5f;
    public float iFrameStart    = 0.1f;
    public float iFrameEnd      = 0.4f;
    public float staminaCost    = 20f;

    private PlayerStateMachine  _sm;
    private PlayerStats         _stats;
    private PlayerInputHandler  _input;
    private CharacterController _cc;
    private Animator            _anim;

    private bool _isInvincible;
    public  bool IsInvincible => _isInvincible;

    void Awake()
    {
        _sm    = GetComponent<PlayerStateMachine>();
        _stats = GetComponent<PlayerStats>();
        _input = GetComponent<PlayerInputHandler>();
        _cc    = GetComponent<CharacterController>();
        _anim  = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (!_sm.CanAct()) return;
        if (_input.DodgePressed) TryDodge();
    }

    void TryDodge()
    {
        if (_sm.CurrentState == PlayerState.Dodging) return;
        if (!_stats.UseStamina(staminaCost)) return;

        Vector3 dodgeDir = transform.forward;

        // Hareket inputu varsa o yönde dodge yap
        if (GetComponent<PlayerMovement>() != null)
        {
            Vector2 input = _input.MoveInput;
            if (input.sqrMagnitude > 0.1f)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 camForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    Vector3 camRight   = Vector3.ProjectOnPlane(cam.transform.right,   Vector3.up).normalized;
                    dodgeDir = (camForward * input.y + camRight * input.x).normalized;
                }
            }
        }

        StartCoroutine(DodgeRoutine(dodgeDir));
    }

    IEnumerator DodgeRoutine(Vector3 dir)
    {
        _sm.ChangeState(PlayerState.Dodging);

        // Dodge yönünü karaktere göre hesapla
        if (_anim != null)
        {
            Vector3 localDir = transform.InverseTransformDirection(dir);

            float absX = Mathf.Abs(localDir.x);
            float absZ = Mathf.Abs(localDir.z);

            if (absZ >= absX)
            {
                if (localDir.z >= 0)
                    _anim.SetTrigger("DodgeFront");
                else
                    _anim.SetTrigger("DodgeBack");
            }
            else
            {
                if (localDir.x > 0)
                    _anim.SetTrigger("DodgeRight");
                else
                    _anim.SetTrigger("DodgeLeft");
            }
        }

        float elapsed = 0f;

        while (elapsed < dodgeDuration)
        {
            _isInvincible = elapsed >= iFrameStart && elapsed < iFrameEnd;
            float speed = dodgeDistance / dodgeDuration;
            _cc.Move(dir * speed * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        _isInvincible = false;
        _sm.ChangeState(PlayerState.Idle);
    }
}