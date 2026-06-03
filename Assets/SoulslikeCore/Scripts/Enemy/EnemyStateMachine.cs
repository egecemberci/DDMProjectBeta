using UnityEngine;

public enum EnemyState
{
    Idle, Chasing, Attacking, Stunned, Dead
}

public class EnemyStateMachine : MonoBehaviour
{
    public EnemyState CurrentState { get; private set; } = EnemyState.Idle;

    public void ChangeState(EnemyState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
    }

    public bool CanAct()
    {
        return CurrentState != EnemyState.Dead &&
               CurrentState != EnemyState.Stunned;
    }
}

