using UnityEngine;
using UnityEngine.AI;

public class EnemyMovement : MonoBehaviour
{
    public float moveSpeed      = 3.5f;
    public float chaseRange     = 12f;
    public float stopDistance   = 1.8f;  // saldırı mesafesi

    private NavMeshAgent       _agent;
    private EnemyStateMachine  _sm;
    private Animator           _anim;
    private Transform          _player;

    void Awake()
    {
        _agent  = GetComponent<NavMeshAgent>();
        _sm     = GetComponent<EnemyStateMachine>();
        //_anim   = GetComponent<Animator>();
        _player = GameObject.FindWithTag("Player")?.transform;

        _agent.speed        = moveSpeed;
        _agent.stoppingDistance = stopDistance;
    }

    void Update()
    {
        if (!_sm.CanAct() || _player == null) return;

        float dist = Vector3.Distance(transform.position, _player.position);

        if (dist <= chaseRange)
        {
            _sm.ChangeState(EnemyState.Chasing);
            _agent.SetDestination(_player.position);
        }
        else
        {
            _sm.ChangeState(EnemyState.Idle);
            _agent.ResetPath();
        }

        // Animator'a hız bilgisi ver
float speed = _agent.velocity.magnitude;
if (_anim != null) _anim.SetFloat("Speed", speed);
    }

    public void StopMovement()
    {
        _agent.ResetPath();
        _agent.velocity = Vector3.zero;
    }
}