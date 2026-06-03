using UnityEngine;
using System;
using System.Collections;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("Stats")]
    public float maxHP       = 100f;
    public float maxStamina  = 100f;
    public float poise       = 50f;

    [SerializeField] private float _currentHP;
    [SerializeField] private float _currentStamina;

    public float CurrentHP      => _currentHP;
    public float CurrentStamina => _currentStamina;

    public event Action OnDamageTaken;
    public event Action<float> OnHPChanged;
    public event Action<float> OnStaminaChanged;
    public event Action        OnDied;

    [Header("Hasar Tepkisi")]
    public float hitInvulnerability = 0.4f;   // vuruş sonrası kısa dokunulmazlık (üst üste vurulmayı engeller)
    public float staggerDuration    = 0.5f;    // stagger'dan toparlanma süresi

    private PlayerStateMachine _sm;
    private Animator           _anim;
    private float              _staminaRegenTimer;
    private float              _hurtTimer;
    private const float        StaminaRegenDelay = 1.2f;
    private const float        StaminaRegenRate  = 20f;

    void Awake()
    {
        _sm             = GetComponent<PlayerStateMachine>();
        _anim           = GetComponentInChildren<Animator>();
        _currentHP      = maxHP;
        _currentStamina = maxStamina;
    }

    void Update()
    {
        if (_hurtTimer > 0f) _hurtTimer -= Time.deltaTime;
        HandleStaminaRegen();
    }

    void HandleStaminaRegen()
    {
        if (_staminaRegenTimer > 0f)
        {
            _staminaRegenTimer -= Time.deltaTime;
            return;
        }

        if (_currentStamina < maxStamina)
        {
            _currentStamina = Mathf.Min(maxStamina, _currentStamina + StaminaRegenRate * Time.deltaTime);
            OnStaminaChanged?.Invoke(_currentStamina / maxStamina);
        }
    }

    public bool UseStamina(float amount)
    {
        if (_currentStamina < amount) return false;
        _currentStamina    -= amount;
        _staminaRegenTimer  = StaminaRegenDelay;
        OnStaminaChanged?.Invoke(_currentStamina / maxStamina);
        return true;
    }

    public bool HasStamina(float amount) => _currentStamina >= amount;

public void TakeDamage(float damage, float poiseDamage, Vector3 attackerPos)
{
    if (_sm.CurrentState == PlayerState.Dead) return;

    // I-frame kontrolü
    PlayerDodge dodge = GetComponent<PlayerDodge>();
    if (dodge != null && dodge.IsInvincible) return;

    // Vuruş sonrası dokunulmazlık — aynı anda üst üste vurulmayı ve animasyonun tekrar tekrar tetiklenmesini engeller
    if (_hurtTimer > 0f) return;

    // Block/Parry kontrolü
    PlayerBlock block = GetComponent<PlayerBlock>();
    if (block != null) damage = block.ProcessDamage(damage);

    if (damage <= 0) return;

    _currentHP = Mathf.Max(0, _currentHP - damage);
    OnHPChanged?.Invoke(_currentHP / maxHP);
    OnDamageTaken?.Invoke();

    if (_currentHP <= 0) { Die(); return; }

    // Bir tepki animasyonuna giriyoruz — bir süre tekrar vurulmayı engelle
    _hurtTimer = hitInvulnerability;

    if (poiseDamage >= poise) Stagger();
    else HitReaction(attackerPos);
}

    void HitReaction(Vector3 attackerPos)
    {
        if (_anim != null) _anim.SetTrigger("Hit");
    }

    void Stagger()
    {
        _sm.ChangeState(PlayerState.Stunned);
        if (_anim != null) _anim.SetTrigger("Stagger");
        StartCoroutine(StaggerRecover());
    }

    // OnStaggerEnd animasyon event'i gelmezse oyuncu kalıcı Stunned kalmasın diye güvenlik
    IEnumerator StaggerRecover()
    {
        yield return new WaitForSeconds(staggerDuration);
        if (_sm.CurrentState == PlayerState.Stunned) _sm.ChangeState(PlayerState.Idle);
    }

    void Die()
    {
        _sm.ChangeState(PlayerState.Dead);

        if (_anim != null)
        {
            // Ölüm animasyonunu ezebilecek bekleyen tetikleri temizle
            _anim.ResetTrigger("Hit");
            _anim.ResetTrigger("Stagger");
            _anim.ResetTrigger("Jump");
            _anim.ResetTrigger("LightAttack");
            _anim.ResetTrigger("HeavyAttack");
            _anim.ResetTrigger("DodgeFront");
            _anim.ResetTrigger("DodgeBack");
            _anim.ResetTrigger("DodgeLeft");
            _anim.ResetTrigger("DodgeRight");
            _anim.SetTrigger("Death");
        }

        // Tüm girdiyi kes ve saldırı hitbox'ını kapat — ölüm animasyonu kesilmesin
        var input = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (input != null) input.DeactivateInput();

        var combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.DisableCombat();

        OnDied?.Invoke();
    }

    public void OnStaggerEnd() => _sm.ChangeState(PlayerState.Idle);
}