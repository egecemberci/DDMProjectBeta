using UnityEngine;

public enum PlayerState
{
    Idle, Moving, Sprinting,
    LightAttacking, HeavyAttacking,
    Dodging, Blocking,
    EnchantActivating,
    Stunned, Dead
}

public class PlayerStateMachine : MonoBehaviour
{
    public PlayerState CurrentState { get; private set; } = PlayerState.Idle;

    public void ChangeState(PlayerState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
    }

    public bool CanAct()
    {
        return CurrentState != PlayerState.Dead &&
               CurrentState != PlayerState.Stunned &&
               CurrentState != PlayerState.EnchantActivating;
    }

    public bool IsAttacking()
    {
        return CurrentState == PlayerState.LightAttacking ||
               CurrentState == PlayerState.HeavyAttacking;
    }
}