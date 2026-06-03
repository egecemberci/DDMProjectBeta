using UnityEngine;
using System.Collections.Generic;
using System.Collections;
public class PlayerCombat : MonoBehaviour
{
    [Header("Silah Modeli")]
    public Transform weaponBone;      // RightHand kemiği
    public GameObject weaponPrefab;   // silah prefabı
    private GameObject _weaponInstance;
    public Vector3 weaponScale = Vector3.one;
    public Vector3 weaponRotation = Vector3.zero;
    void Start()
{
    if (weaponPrefab != null && weaponBone != null)
    {
        _weaponInstance = Instantiate(weaponPrefab, weaponBone);
        _weaponInstance.transform.localPosition = Vector3.zero;
        _weaponInstance.transform.localRotation = Quaternion.identity;
        _weaponInstance.transform.localScale = weaponScale;
        _weaponInstance.transform.localRotation = Quaternion.Euler(weaponRotation);
    }
}
    [Header("Silah")]
    public WeaponData currentWeapon;
    public Collider   hitbox;
    public Transform  weaponTransform;

    private PlayerStateMachine _sm;
    private PlayerStats        _stats;
    private PlayerInputHandler _input;
    private EnchantBar         _enchantBar;
    private EnchantmentSystem  _enchantSystem;
    private Animator           _anim;


    [Header("Saldırı Zamanlaması")]
    public float lightAttackDuration = 0.7f;   // hafif saldırı kilidi (anim bitene kadar)
    public float heavyAttackDuration = 1.0f;    // ağır saldırı kilidi
    public float attackCooldown      = 0.25f;   // saldırı sonrası kısa bekleme

    private int   _comboStep;
    private float _comboTimer;
    private float _attackCooldownTimer;
    private const float ComboWindow = 1.0f;

    private HashSet<Collider> _hitEnemies = new();

    void Awake()
    {
        _sm            = GetComponent<PlayerStateMachine>();
        _stats         = GetComponent<PlayerStats>();
        _input         = GetComponent<PlayerInputHandler>();
        _enchantBar    = GetComponent<EnchantBar>();
        _enchantSystem = GetComponent<EnchantmentSystem>();
        _anim          = GetComponentInChildren<Animator>();

        if (hitbox != null) hitbox.enabled = false;
        if (_enchantSystem != null && weaponTransform != null)
            _enchantSystem.SetWeaponTransform(weaponTransform);
    }

    void Update()
    {
        if (_comboTimer > 0f) _comboTimer -= Time.deltaTime;
        else _comboStep = 0;

        if (_attackCooldownTimer > 0f) _attackCooldownTimer -= Time.deltaTime;

        if (!_sm.CanAct()) return;

        bool lightAttack = _input.LightAttackPressed;
        bool heavyAttack = _input.HeavyAttackPressed;

        if (lightAttack) TryLightAttack();
        else if (heavyAttack) TryHeavyAttack();
    }

void TryLightAttack()
{
    if (_sm.IsAttacking()) return;
    if (_attackCooldownTimer > 0f) return;
    if (currentWeapon == null) return;
    if (!_stats.UseStamina(currentWeapon.staminaCost)) return;

    _sm.ChangeState(PlayerState.LightAttacking);
    _comboStep  = (_comboStep + 1) % currentWeapon.lightAttackCount;
    _comboTimer = ComboWindow;
    _hitEnemies.Clear();

    if (_anim != null) _anim.SetTrigger("LightAttack");

    StartCoroutine(AttackSequence(lightAttackDuration));
    DealLightDamage();
}

void TryHeavyAttack()
{
    if (_sm.IsAttacking()) return;
    if (_attackCooldownTimer > 0f) return;
    if (currentWeapon == null) return;
    if (!_stats.UseStamina(currentWeapon.heavyStaminaCost)) return;

    _sm.ChangeState(PlayerState.HeavyAttacking);
    _comboStep  = 0;
    _comboTimer = 0f;
    _hitEnemies.Clear();

    if (_anim != null) _anim.SetTrigger("HeavyAttack");

    StartCoroutine(AttackSequence(heavyAttackDuration));
    DealHeavyDamage();
}

IEnumerator AttackSequence(float duration)
{
    yield return new WaitForSeconds(duration);
    _attackCooldownTimer = attackCooldown;
    _sm.ChangeState(PlayerState.Idle);
}

    void DealLightDamage()
    {
        float s = transform.lossyScale.x;
        Collider[] hits = Physics.OverlapSphere(
            transform.position + transform.forward * 1.5f * s, 1f * s);

        bool hitAnything = false;

        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Enemy")) continue;
            if (hit.TryGetComponent<IDamageable>(out var target))
            {
                target.TakeDamage(currentWeapon.damage, currentWeapon.poise, transform.position);
                _enchantSystem?.ApplyEnchantEffect(target, hit.transform.position, hit.transform);
                hitAnything = true;
            }
        }

        if (hitAnything) _enchantBar?.AddProgress(isHeavy: false);


    }

    void DealHeavyDamage()
    {
        Debug.Log("HeavyAttack tetiklendi");

        float s = transform.lossyScale.x;
        Vector3 origin = transform.position + Vector3.up * s;

        RaycastHit[] hits = Physics.BoxCastAll(
            origin,
            new Vector3(0.3f, 0.5f, 0.3f) * s,
            transform.forward,
            transform.rotation,
            2.5f * s);

        bool hitAnything = false;

        foreach (var hit in hits)
        {
            if (!hit.collider.CompareTag("Enemy")) continue;
            if (hit.collider.TryGetComponent<IDamageable>(out var target))
            {
                target.TakeDamage(currentWeapon.heavyDamage, currentWeapon.poise * 2f, transform.position);
                _enchantSystem?.ApplyEnchantEffect(target, hit.point, hit.collider.transform);
                hitAnything = true;
            }
        }

        if (hitAnything) _enchantBar?.AddProgress(isHeavy: true);

    }

    // Animator Event
    public void EnableHitbox()  { if (hitbox != null) hitbox.enabled = true;  }
    public void DisableHitbox() { if (hitbox != null) hitbox.enabled = false; }
    public void OnAttackEnd()   => _sm.ChangeState(PlayerState.Idle);

    // Ölümde çağrılır — saldırıyı tamamen durdur (hitbox + bileşen)
    public void DisableCombat()
    {
        StopAllCoroutines();
        if (hitbox != null) hitbox.enabled = false;
        enabled = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (hitbox == null || !hitbox.enabled) return;
        if (_hitEnemies.Contains(other)) return;
        if (!other.CompareTag("Enemy")) return;

        _hitEnemies.Add(other);

        if (other.TryGetComponent<IDamageable>(out var target))
        {
            target.TakeDamage(currentWeapon.damage, currentWeapon.poise, transform.position);
            _enchantSystem?.ApplyEnchantEffect(target, other.transform.position, other.transform);
        }
    }
}