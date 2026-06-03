using UnityEngine;
using System;
using System.Collections;

public class EnemyStats : MonoBehaviour, IDamageable
{
    [Header("Stats")]
    public float maxHP           = 80f;
    public float poise           = 30f;
    public float damageMultiplier = 1f;  // Void efsunu bunu düşürür


[SerializeField] private float _currentHP;
public float CurrentHP => _currentHP;

    public event Action OnDied;

    private EnemyStateMachine _sm;
    private Animator          _anim;

    void Awake()
    {
        _sm       = GetComponent<EnemyStateMachine>();
       // _anim     = GetComponent<Animator>();
        _currentHP = maxHP;
    }

    public void TakeDamage(float damage, float poiseDamage, Vector3 attackerPos)
    {
        if (_sm.CurrentState == EnemyState.Dead) return;

    _currentHP = Mathf.Max(0, _currentHP - damage);
    if (_currentHP <= 0) { Die(); return; }

        if (poiseDamage >= poise) Stagger();
        else _anim?.SetTrigger("Hit");
    }

    void Stagger()
    {
        _sm.ChangeState(EnemyState.Stunned);
        _anim?.SetTrigger("Stagger");
    }

    // Animator Event — stagger animasyonu bitince çağrılır
    public void OnStaggerEnd() => _sm.ChangeState(EnemyState.Chasing);

   void Die()
   {
       _sm.ChangeState(EnemyState.Dead);
       if (_anim != null) _anim.SetTrigger("Death");
       GetComponent<Collider>().enabled = false;

       // NavMesh Agent'ı durdur
       var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
       if (agent != null) agent.enabled = false;

       OnDied?.Invoke();
       StartCoroutine(DeathRoutine());
   }

     IEnumerator DeathRoutine()
   {
       yield return new WaitForSeconds(2f); // animasyon oynasın

       // Yavaşça kaybol
       float elapsed  = 0f;
       float duration = 1.5f;
       Renderer[] renderers = GetComponentsInChildren<Renderer>();

       while (elapsed < duration)
       {
           elapsed += Time.deltaTime;
           float alpha = 1f - (elapsed / duration);

           foreach (var r in renderers)
           {
               foreach (var mat in r.materials)
               {
                   Color c = mat.color;
                   c.a = alpha;
                   mat.color = c;
               }
           }
           yield return null;
       }

       Destroy(gameObject);
   }
}