using UnityEngine;
using System.Collections;

public class EnemyCombat : MonoBehaviour
{
    [Header("Saldırı")]
    public float attackDamage   = 15f;
    public float attackRange    = 2f;
    public float attackCooldown = 2f;
    public float attackPoise    = 20f;      // oyuncunun poise'unu kırmak için
    public float attackWindup   = 0.6f;     // saldırı öncesi bekleme (telegraphing)

    private EnemyStateMachine _sm;
    private EnemyStats        _stats;
    private EnemyMovement     _movement;
    private Animator          _anim;
    private Transform         _player;
    private IDamageable       _playerDamageable;
    private float             _cooldownTimer;
    private bool              _isAttacking;

    void Awake()
    {
        _sm       = GetComponent<EnemyStateMachine>();
        _stats    = GetComponent<EnemyStats>();
        _movement = GetComponent<EnemyMovement>();
        _anim     = GetComponent<Animator>();

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            _player           = playerObj.transform;
            _playerDamageable = playerObj.GetComponent<IDamageable>();
        }
    }

    void Update()
    {
        if (!_sm.CanAct() || _player == null) return;

        _cooldownTimer -= Time.deltaTime;

        float dist = Vector3.Distance(transform.position, _player.position);

        if (dist <= attackRange && _cooldownTimer <= 0f && !_isAttacking)
        {
            StartCoroutine(AttackRoutine());
        }
    }

    IEnumerator AttackRoutine()
    {
        _isAttacking = true;
        _sm.ChangeState(EnemyState.Attacking);
        _movement.StopMovement();

        // Windup — saldırı öncesi tel
        if (_anim != null) _anim.SetTrigger("AttackWindup");
        yield return new WaitForSeconds(attackWindup);

        // Hala menzilde mi kontrol et
        float dist = Vector3.Distance(transform.position, _player.position);
        if (dist <= attackRange + 0.5f)
        {
            if (_anim != null) _anim.SetTrigger("Attack");
            DealDamage();
        }

        _cooldownTimer = attackCooldown;
        _isAttacking   = false;
        _sm.ChangeState(EnemyState.Chasing);
    }

    void DealDamage()
    {
        if (_playerDamageable == null) return;

        // Oyuncu block yapıyor mu kontrol et
        PlayerBlock block = _player.GetComponent<PlayerBlock>();

        if (block != null && block.IsBlocking)
        {
            // Block yönü doğru mu? Düşman önden mi saldırıyor?
            Vector3 dirToEnemy = (transform.position - _player.position).normalized;
            float   dot        = Vector3.Dot(_player.forward, dirToEnemy);

            if (dot > 0.3f) // oyuncu düşmana bakıyorsa block geçerli
            {
                float reducedDamage = block.ProcessDamage(attackDamage);
                if (reducedDamage > 0)
                    _playerDamageable.TakeDamage(reducedDamage, 0, transform.position);
                return;
            }
        }

        // Normal hasar
        _playerDamageable.TakeDamage(attackDamage, attackPoise, transform.position);
    }
}