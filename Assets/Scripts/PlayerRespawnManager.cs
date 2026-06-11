using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerRespawnManager : MonoBehaviour
{
    public PlayerStats playerStats;
    public GameObject restartPrompt;

    private Vector3 lastCheckpoint;
    private bool isDead;

    private InputAction restartAction;

    void Awake()
    {
        lastCheckpoint = playerStats.transform.position;
        restartPrompt.SetActive(false);
    }

    void OnEnable()
    {
        playerStats.OnDied += HandleDeath;
    }

    void OnDisable()
    {
        playerStats.OnDied -= HandleDeath;
    }

    void HandleDeath()
    {
        isDead = true;
        restartPrompt.SetActive(true);

        var input = playerStats.GetComponent<PlayerInput>();
        restartAction = input.actions.FindAction("Restart");

        restartAction.performed += OnRestart;
        restartAction.Enable();
    }

    void OnRestart(InputAction.CallbackContext ctx)
    {
        Respawn();
    }

    void Respawn()
    {
        isDead = false;
        restartPrompt.SetActive(false);

        if (restartAction != null)
            restartAction.performed -= OnRestart;

        // reset position
        playerStats.transform.position = lastCheckpoint;

        // reset stats
        playerStats.ResetStats();

        // reset state machine
        var sm = playerStats.GetComponent<PlayerStateMachine>();
        if (sm != null)
            sm.ChangeState(PlayerState.Idle);

        // 🔥 FIX #1: FORCE ANIMATOR RESET (THIS IS YOUR PROBLEM)
        var anim = playerStats.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.Rebind();
            anim.Update(0f);
        }

        // 🔥 FIX #2: STOP ANY RAGDOLL / PHYSICS (if present)
        Rigidbody[] rbs = playerStats.GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rbs)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // re-enable input
        var input = playerStats.GetComponent<PlayerInput>();
        if (input != null)
            input.ActivateInput();

        // reset potions
        var potion = playerStats.GetComponent<PlayerPotion>();
        if (potion != null)
            potion.ResetPotions();
    }
}