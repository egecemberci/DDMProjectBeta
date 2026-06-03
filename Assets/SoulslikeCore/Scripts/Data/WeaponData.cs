using UnityEngine;

[CreateAssetMenu(menuName = "Combat/Weapon")]
public class WeaponData : ScriptableObject
{
    [Header("Genel")]
    public string weaponName;

    [Header("Hafif Saldırı")]
    public float damage           = 15f;
    public float staminaCost      = 10f;
    public int   lightAttackCount = 3;    // combo adım sayısı

    [Header("Ağır Saldırı")]
    public float heavyDamage      = 35f;  // tek güçlü dürtme
    public float heavyStaminaCost = 25f;

    [Header("Poise")]
    public float poise            = 8f;   // rapier düşük poise kırar

    [Header("Animasyonlar")]
    public AnimationClip[] lightAttacks;  // 3 combo
    public AnimationClip   heavyAttack;   // tek dürtme
}