using UnityEngine;
using System.Collections;

public class StatusEffectHandler : MonoBehaviour
{
    private IDamageable _damageable;
    private EnemyMovement _movement;
    private EnemyStats _enemyStats;

    // Aynı efektin üst üste binmesini önlemek için
    private Coroutine _burnRoutine;
    private Coroutine _slowRoutine;
    private Coroutine _voidRoutine;

    void Awake()
    {
        _damageable = GetComponent<IDamageable>();
        _movement = GetComponent<EnemyMovement>();
        _enemyStats = GetComponent<EnemyStats>();
    }

    public void ApplyBurn(float dmgPerSec, float duration)
    {
        if (_burnRoutine != null) StopCoroutine(_burnRoutine);
        _burnRoutine = StartCoroutine(BurnRoutine(dmgPerSec, duration));
    }

    IEnumerator BurnRoutine(float dmgPerSec, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return new WaitForSeconds(1f);
            _damageable?.TakeDamage(dmgPerSec, 0, transform.position);
            elapsed += 1f;
        }
        _burnRoutine = null;
    }

    public void ApplySlow(float multiplier, float duration)
    {
        if (_slowRoutine != null) StopCoroutine(_slowRoutine);
        _slowRoutine = StartCoroutine(SlowRoutine(multiplier, duration));
    }

    IEnumerator SlowRoutine(float multiplier, float duration)
    {
        if (_movement == null) yield break;

        float original = _movement.moveSpeed;
        _movement.moveSpeed = original * multiplier;

        yield return new WaitForSeconds(duration);

        _movement.moveSpeed = original;
        _slowRoutine = null;
    }

    public void ApplyVoidDebuff(float reduction, float duration)
    {
        if (_voidRoutine != null) StopCoroutine(_voidRoutine);
        _voidRoutine = StartCoroutine(VoidRoutine(reduction, duration));
    }

    IEnumerator VoidRoutine(float reduction, float duration)
    {
        if (_enemyStats == null) yield break;

        _enemyStats.damageMultiplier *= (1f - reduction);

        yield return new WaitForSeconds(duration);

        _enemyStats.damageMultiplier /= (1f - reduction);
        _voidRoutine = null;
    }
}