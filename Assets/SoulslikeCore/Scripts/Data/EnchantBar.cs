using UnityEngine;
using System;

public class EnchantBar : MonoBehaviour
{
    [Header("Settings")]
    public float maxBar = 100f;
    public float lightAttackGain = 10f;
    public float heavyAttackGain = 20f;

    public float CurrentBar { get; private set; }
    public bool IsEnchanted { get; private set; }

    public event Action<float> OnBarChanged;
    public event Action OnBarFull;
    public event Action OnEnchantExpired;

    private PlayerStats _stats;

    void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _stats.OnDamageTaken += ResetBar;
    }

    void OnDestroy()
    {
        _stats.OnDamageTaken -= ResetBar;
    }

    public void AddProgress(bool isHeavy)
    {
        if (IsEnchanted) return;

        float gain = isHeavy ? heavyAttackGain : lightAttackGain;
        CurrentBar = Mathf.Min(maxBar, CurrentBar + gain);
        OnBarChanged?.Invoke(CurrentBar / maxBar);

    }
    public void SetCurrentBar(float value)
{
    CurrentBar = Mathf.Clamp(value, 0f, maxBar);
}
public void InvokeBarChanged(float ratio)
{
    OnBarChanged?.Invoke(ratio);
}
    public void SetEnchanted(bool value)
    {
        IsEnchanted = value;
    }

    public void ResetBar()
    {
        CurrentBar = 0f;
        IsEnchanted = false;
        OnBarChanged?.Invoke(0f);
        OnEnchantExpired?.Invoke();
    }
}