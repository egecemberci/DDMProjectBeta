using UnityEngine;

public enum ElementType { Fire, Ice, Light, Void }

[CreateAssetMenu(menuName = "Combat/Enchantment")]
public class EnchantmentData : ScriptableObject
{
    public ElementType element;
    public string enchantName;
    public float duration = 15f;   // efsun kaç saniye sürer
    public float bonusDamage = 8f;    // her saldırıda ek hasar

    [Header("Fire")]
    public float fireDotDamage = 3f;           // saniyede yanma hasarı
    public float fireDotDuration = 4f;

    [Header("Ice")]
    public float iceSlowMultiplier = 0.5f;      // 0.5 = yarı hız
    public float iceSlowDuration = 3f;

    [Header("Light")]
    public float lightProjectileDamage = 12f;
    public float lightProjectileSpeed = 20f;
    public GameObject lightProjectilePrefab;

    [Header("Void")]
    public float voidDamageReduction = 0.3f;    // düşman %30 daha az hasar verir
    public float voidDebuffDuration = 5f;

    [Header("Visuals")]
    public GameObject enchantParticlePrefab;    // kılıca bağlanacak particle
    public Color enchantColor;
}