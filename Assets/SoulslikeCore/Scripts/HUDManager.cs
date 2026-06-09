using UnityEngine;
using UnityEngine.UI;

public class HUDManager : MonoBehaviour
{
    [Header("Barlar")]
    public Slider hpBar;
    public Slider staminaBar;

    private PlayerStats _stats;

    void Awake()
    {
        _stats = FindAnyObjectByType<PlayerStats>();
        if (_stats != null)
        {
            _stats.OnHPChanged      += UpdateHP;
            _stats.OnStaminaChanged += UpdateStamina;
        }
    }

    void OnDestroy()
    {
        if (_stats != null)
        {
            _stats.OnHPChanged      -= UpdateHP;
            _stats.OnStaminaChanged -= UpdateStamina;
        }
    }

    void Start()
    {
        // Başlangıç değerlerini UI'a bildir
        if (_stats != null)
        {
            UpdateHP(_stats.CurrentHP / _stats.maxHP);
            UpdateStamina(_stats.CurrentStamina / _stats.maxStamina);
        }
    }

    void UpdateHP(float ratio)      { if (hpBar != null)      hpBar.value      = ratio; }
    void UpdateStamina(float ratio) { if (staminaBar != null) staminaBar.value = ratio; }
}
