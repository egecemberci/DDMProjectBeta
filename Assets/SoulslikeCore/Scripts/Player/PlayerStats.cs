using UnityEngine;
using System;
using System.Collections;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("Stats")]
    public float maxHP       = 100f;
    public float maxStamina  = 100f;
    public float poise       = 50f;
    public float staminaRegenRate = 30f;                       // faster base regen — never stops
    [Range(0f,1f)] public float blockRegenMultiplier = 0.1f;   // regen drops to 10% while blocking
    public float standStillRegenMult = 3f;                     // regen ×this once holding still...
    public float standStillDelay     = 0.3f;                   // ...for longer than this (no move input)

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
    private PlayerBlock        _block;
    private PlayerInputHandler _input;
    private float              _hurtTimer;
    private float              _regenBoostTimer, _regenBoostRate;
    private float              _standStillTimer;

    void Awake()
    {
        _sm             = GetComponent<PlayerStateMachine>();
        _anim           = GetComponentInChildren<Animator>();
        _block          = GetComponent<PlayerBlock>();
        _input          = GetComponent<PlayerInputHandler>();
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
        // track how long the player has CONSCIOUSLY stood still — full control (Idle) + no move input.
        // Idle excludes attacking / dodging / blocking / drinking / stunned / moving, so only a real breather counts.
        bool standingStill = _sm != null && _sm.CurrentState == PlayerState.Idle
                          && (_input == null || _input.MoveInput.sqrMagnitude < 0.01f);
        _standStillTimer = standingStill ? _standStillTimer + Time.deltaTime : 0f;

        if (_regenBoostTimer > 0f) _regenBoostTimer -= Time.deltaTime;
        if (_currentStamina >= maxStamina) return;
        float rate = _regenBoostTimer > 0f ? _regenBoostRate : staminaRegenRate;
        if (_regenBoostTimer <= 0f && _block != null && _block.IsBlocking) rate *= blockRegenMultiplier;
        if (_regenBoostTimer <= 0f && _standStillTimer > standStillDelay) rate *= standStillRegenMult;  // standing-still bonus
        _currentStamina = Mathf.Min(maxStamina, _currentStamina + rate * Time.deltaTime);
        OnStaminaChanged?.Invoke(_currentStamina / maxStamina);
    }

    public bool UseStamina(float amount)
    {
        if (_currentStamina < amount) return false;
        _currentStamina -= amount;
        OnStaminaChanged?.Invoke(_currentStamina / maxStamina);
        return true;
    }

    public void DrainStamina(float amount)   // always drains (clamped), used by block
    {
        _currentStamina = Mathf.Max(0f, _currentStamina - amount);
        OnStaminaChanged?.Invoke(_currentStamina / maxStamina);
    }

    public void AddStamina(float amount)
    {
        _currentStamina = Mathf.Min(maxStamina, _currentStamina + amount);
        OnStaminaChanged?.Invoke(_currentStamina / maxStamina);
    }

    public void Heal(float amount)   // restore HP (clamped), used by the healing potion
    {
        if (_currentHP <= 0f) return;   // can't heal a dead player
        _currentHP = Mathf.Min(maxHP, _currentHP + amount);
        OnHPChanged?.Invoke(_currentHP / maxHP);
    }

    // temporarily override the regen rate (used by the stance break)
    public void BoostRegen(float duration, float rate) { _regenBoostTimer = duration; _regenBoostRate = rate; }

    public bool HasStamina(float amount) => _currentStamina >= amount;

public void TakeDamage(float damage, float poiseDamage, Vector3 attackerPos)
{
    if (_sm.CurrentState == PlayerState.Dead) return;

    PlayerDodge dodge = GetComponent<PlayerDodge>();
    if (dodge != null && dodge.IsInvincible) return;

    // Block handled FIRST (no i-frame) so EVERY blocked hit drains stamina
    PlayerBlock block = GetComponent<PlayerBlock>();
    if (block != null && block.IsBlocking) { block.OnBlockedHit(damage, attackerPos); return; }

    if (_hurtTimer > 0f) return;            // i-frame: prevents rapid re-hits (only when NOT blocking)
    _hurtTimer = hitInvulnerability;

    if (damage <= 0) return;

    _currentHP = Mathf.Max(0, _currentHP - damage);
    OnHPChanged?.Invoke(_currentHP / maxHP);
    OnDamageTaken?.Invoke();

    if (_currentHP <= 0) { Die(); return; }

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