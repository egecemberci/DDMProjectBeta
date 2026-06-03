using UnityEngine;
using UnityEngine.UI;

public class HUDManager : MonoBehaviour
{
    [Header("Barlar")]
    public Slider hpBar;
    public Slider staminaBar;
    public Slider enchantBar;

    [Header("Efsun Aktif Göstergesi")]
    public Image enchantActiveIndicator; // efsun aktifken parlayan ikon

    private PlayerStats       _stats;
    private EnchantBar        _enchantBar;
    private EnchantmentSystem _enchantSystem;

    void Awake()
    {
        _stats         = FindAnyObjectByType<PlayerStats>();
        _enchantBar    = FindAnyObjectByType<EnchantBar>();
        _enchantSystem = FindAnyObjectByType<EnchantmentSystem>();

        _stats.OnHPChanged      += UpdateHP;
        _stats.OnStaminaChanged += UpdateStamina;
        _enchantBar.OnBarChanged += UpdateEnchant;
        _enchantBar.OnEnchantExpired += OnEnchantExpired;
    }

    void OnDestroy()
    {
        _stats.OnHPChanged       -= UpdateHP;
        _stats.OnStaminaChanged  -= UpdateStamina;
        _enchantBar.OnBarChanged -= UpdateEnchant;
        _enchantBar.OnEnchantExpired -= OnEnchantExpired;
    }

    void Update()
    {
        // Efsun aktif göstergesi
        if (enchantActiveIndicator != null && _enchantSystem != null)
        {
            enchantActiveIndicator.gameObject.SetActive(_enchantSystem.IsActive);
        }

        // Efsun barı dolunca parlat
        if (enchantBar != null && _enchantBar != null)
        {
            bool isFull = _enchantBar.CurrentBar >= _enchantBar.maxBar && !_enchantSystem.IsActive;
            enchantBar.fillRect.GetComponent<Image>().color = isFull
                ? Color.white   // dolunca beyaz/parlak
                : new Color(0.6f, 0.2f, 0.8f); // normal mor
        }
    }

    void UpdateHP(float ratio)      => hpBar.value      = ratio;
    void UpdateStamina(float ratio) => staminaBar.value = ratio;
    void UpdateEnchant(float ratio) => enchantBar.value = ratio;
    void OnEnchantExpired()         => enchantBar.value = 0f;

    void Start()
    {
        // Başlangıç değerlerini UI'a bildir
        if (_stats != null)
        {
            UpdateHP(_stats.CurrentHP / _stats.maxHP);
            UpdateStamina(_stats.CurrentStamina / _stats.maxStamina);
        }
        if (enchantBar != null) enchantBar.value = 0f;
        if (enchantActiveIndicator != null) enchantActiveIndicator.gameObject.SetActive(false);
    }
}