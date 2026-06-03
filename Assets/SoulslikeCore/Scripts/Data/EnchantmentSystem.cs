using UnityEngine;
using System.Collections;

public class EnchantmentSystem : MonoBehaviour
{
    [Header("Elementler — Inspector'dan ata")]
    public EnchantmentData fireEnchant;
    public EnchantmentData iceEnchant;
    public EnchantmentData lightEnchant;
    public EnchantmentData voidEnchant;

    [Header("Aktif Element")]
    public EnchantmentData activeEnchantment;

    public bool IsActive { get; private set; }

    private EnchantBar _bar;
    private PlayerStateMachine _sm;
    private Animator _anim;
    private Transform _weaponTransform;
    private GameObject _particleInstance;

     void Awake()
     {
         _bar   = GetComponent<EnchantBar>();
         _sm    = GetComponent<PlayerStateMachine>();
         _anim  = GetComponentInChildren<Animator>();
         _input = GetComponent<PlayerInputHandler>();

         _bar.OnEnchantExpired += OnEnchantExpired;
     }

    void OnDestroy()
    {
        _bar.OnBarFull -= OnBarFull;
        _bar.OnEnchantExpired -= OnEnchantExpired;
    }

    // Silahın transform'unu Combat scripti başlarken atar
    public void SetWeaponTransform(Transform weapon)
    {
        _weaponTransform = weapon;
    }

    void OnBarFull()
    {
        StartCoroutine(ActivateSequence());
    }

IEnumerator ActivateSequence()
{
    if (activeEnchantment == null) yield break;
    if (_sm == null) yield break;

    _sm.ChangeState(PlayerState.EnchantActivating);
    if (_anim != null) _anim.SetTrigger("EnchantActivate");

    yield return new WaitForSeconds(0.5f);

    _bar.SetEnchanted(true);
    IsActive = true;

    if (activeEnchantment.enchantParticlePrefab != null && _weaponTransform != null)
    {
        _particleInstance = Instantiate(
            activeEnchantment.enchantParticlePrefab,
            _weaponTransform.position,
            Quaternion.identity,
            _weaponTransform
        );
    }

    _sm.ChangeState(PlayerState.Idle);

    // Süre boyunca bar yavaşça geri gitsin
    float elapsed  = 0f;
    float duration = activeEnchantment.duration;

    while (elapsed < duration)
    {
        elapsed += Time.deltaTime;
        float ratio = 1f - (elapsed / duration);
        _bar.SetCurrentBar(ratio * _bar.maxBar);
        _bar.InvokeBarChanged(ratio);
        yield return null;
    }

    _bar.ResetBar();
}

    void OnEnchantExpired()
    {
        IsActive = false;
        if (_particleInstance != null) Destroy(_particleInstance);
    }

    // PlayerCombat her saldırıda bunu çağırır
    public void ApplyEnchantEffect(IDamageable target, Vector3 hitPoint, Transform targetTransform)
    {
        if (!IsActive || activeEnchantment == null) return;

        switch (activeEnchantment.element)
        {
            case ElementType.Fire:
                target.TakeDamage(activeEnchantment.bonusDamage, 0, transform.position);
                if (targetTransform.TryGetComponent<StatusEffectHandler>(out var fire))
                    fire.ApplyBurn(activeEnchantment.fireDotDamage, activeEnchantment.fireDotDuration);
                break;

            case ElementType.Ice:
                if (targetTransform.TryGetComponent<StatusEffectHandler>(out var ice))
                    ice.ApplySlow(activeEnchantment.iceSlowMultiplier, activeEnchantment.iceSlowDuration);
                break;

            case ElementType.Light:
                SpawnLightProjectile(hitPoint);
                break;

            case ElementType.Void:
                if (targetTransform.TryGetComponent<StatusEffectHandler>(out var vd))
                    vd.ApplyVoidDebuff(activeEnchantment.voidDamageReduction, activeEnchantment.voidDebuffDuration);
                break;
        }
    }

    void SpawnLightProjectile(Vector3 hitPoint)
    {
        if (activeEnchantment.lightProjectilePrefab == null) return;

        Vector3 dir = (hitPoint - transform.position).normalized;
        GameObject proj = Instantiate(
            activeEnchantment.lightProjectilePrefab,
            transform.position + Vector3.up,
            Quaternion.LookRotation(dir)
        );

        if (proj.TryGetComponent<LightProjectile>(out var lp))
            lp.Init(activeEnchantment.lightProjectileDamage, activeEnchantment.lightProjectileSpeed);
    }
    private PlayerInputHandler _input;



    void Update()
    {
        if (_input == null) return;
        if (!_sm.CanAct()) return;

        // Efsun barı doluysa ve tuşa basılınca aktive et
        if (_input.SpellPressed && _bar.CurrentBar >= _bar.maxBar && !IsActive)
        {
            StartCoroutine(ActivateSequence());
        }
    }
}