using UnityEngine;

public enum PlayerState
{
    Idle, Moving, Sprinting,
    LightAttacking, HeavyAttacking,
    Dodging, Blocking,
    UsingItem,
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
               CurrentState != PlayerState.UsingItem &&   // drinking a potion — locked in place
               CurrentState != PlayerState.Blocking;   // block is uninterruptible — nothing else acts while blocking
    }

    public bool IsAttacking()
    {
        return CurrentState == PlayerState.LightAttacking ||
               CurrentState == PlayerState.HeavyAttacking;
    }
}