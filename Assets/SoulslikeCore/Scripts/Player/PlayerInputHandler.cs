using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{
    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    public bool BlockHeld    { get; private set; }
    public bool SprintHeld   { get; private set; }
    private bool _lightAttackPressed;
    private bool _heavyAttackPressed;
    private bool _dodgePressed;
    private bool _interactPressed;
    private bool _useItemPressed;
    private bool _jumpPressed;
    private bool _lockOnPressed;
    private bool _spellPressed;

    public bool LightAttackPressed
    {
        get { bool v = _lightAttackPressed; _lightAttackPressed = false; return v; }
    }

    public bool HeavyAttackPressed
    {
        get { bool v = _heavyAttackPressed; _heavyAttackPressed = false; return v; }
    }

    public bool DodgePressed
    {
        get { bool v = _dodgePressed; _dodgePressed = false; return v; }
    }

    public bool InteractPressed
    {
        get { bool v = _interactPressed; _interactPressed = false; return v; }
    }

    public bool UseItemPressed
    {
        get { bool v = _useItemPressed; _useItemPressed = false; return v; }
    }

    public bool JumpPressed
    {
        get { bool v = _jumpPressed; _jumpPressed = false; return v; }
    }

    public bool LockOnPressed
    {
        get { bool v = _lockOnPressed; _lockOnPressed = false; return v; }
    }

    public bool SpellPressed
    {
        get { bool v = _spellPressed; _spellPressed = false; return v; }
    }

    public void OnMove(InputValue v)        => MoveInput      = v.Get<Vector2>();
    public void OnLook(InputValue v)        => LookInput      = v.Get<Vector2>();
    public void OnBlock(InputValue v)       => BlockHeld      = v.isPressed;
    public void OnSprint(InputValue v)      => SprintHeld     = v.isPressed;
    public void OnLightAttack(InputValue v) { if (v.isPressed) _lightAttackPressed = true; }
    public void OnHeavyAttack(InputValue v) { if (v.isPressed) _heavyAttackPressed = true; }
    public void OnDodge(InputValue v)       { if (v.isPressed) _dodgePressed       = true; }
    public void OnInteract(InputValue v)    { if (v.isPressed) _interactPressed    = true; }
    public void OnUseItem(InputValue v)     { if (v.isPressed) _useItemPressed     = true; }
    public void OnJump(InputValue v)        { if (v.isPressed) _jumpPressed        = true; }
    public void OnLockOn(InputValue v)      { if (v.isPressed) _lockOnPressed      = true; }
    public void OnSpell(InputValue v)       { if (v.isPressed) _spellPressed       = true; }
}