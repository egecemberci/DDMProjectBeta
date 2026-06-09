using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

// Healing potion. The player spawns/respawns (scene reload) with `maxPotions`. Press R to drink:
// plays the drink animation, the player is locked in place for its duration, then heals HP + stamina.
public class PlayerPotion : MonoBehaviour
{
    [Header("Potion")]
    public int   maxPotions   = 3;
    public float healHp       = 50f;
    public float healStamina  = 50f;
    [Range(0f,1f)] public float healAtFraction = 0.6f;   // when during the drink anim the heal lands

    [Header("Animation")]
    public string drinkTrigger      = "Drink";   // animator trigger -> the Drink state
    public string drinkState        = "Drink";   // state name, used to read the clip length
    public float  drinkFallbackTime = 1.65f;     // used if the clip length can't be read

    [Header("Held model — the iksir, glued to the hand (hidden until drinking)")]
    public GameObject potionModel;

    int  _count;
    bool _drinking;
    PlayerStateMachine _sm;
    PlayerStats        _stats;
    Animator           _anim;

    public int  PotionCount => _count;
    public bool IsDrinking  => _drinking;

    void Awake()
    {
        _sm    = GetComponent<PlayerStateMachine>();
        _stats = GetComponent<PlayerStats>();
        _anim  = GetComponentInChildren<Animator>();
        _count = maxPotions;                                  // full on every spawn / respawn (scene reload re-runs Awake)
        if (potionModel != null) potionModel.SetActive(false);
    }

    void Update()
    {
        if (_drinking) return;
        if (Keyboard.current == null || !Keyboard.current.rKey.wasPressedThisFrame) return;
        if (_count <= 0) return;
        if (!_sm.CanAct()) return;                            // not mid-attack/dodge/block/stunned/dead
        StartCoroutine(Drink());
    }

    IEnumerator Drink()
    {
        _drinking = true;
        _count--;
        _sm.ChangeState(PlayerState.UsingItem);              // locks movement (CanAct == false)
        if (potionModel != null) potionModel.SetActive(true);
        if (_anim != null) { _anim.speed = 1f; _anim.SetTrigger(drinkTrigger); }

        // resolve the drink duration from the clip if possible
        float len = drinkFallbackTime;
        if (_anim != null)
        {
            yield return null;                               // let the trigger transition into the Drink state
            var st = _anim.GetCurrentAnimatorStateInfo(0);
            if (st.IsName(drinkState) && st.length > 0.05f) len = st.length;
        }

        bool healed = false;
        float t = 0f;
        while (t < len)
        {
            if (_sm.CurrentState != PlayerState.UsingItem) break;   // interrupted (e.g. died) -> stop
            if (!healed && t >= len * healAtFraction) { ApplyHeal(); healed = true; }
            t += Time.deltaTime;
            yield return null;
        }
        if (!healed && _sm.CurrentState == PlayerState.UsingItem) ApplyHeal();

        if (potionModel != null) potionModel.SetActive(false);
        if (_sm.CurrentState == PlayerState.UsingItem) _sm.ChangeState(PlayerState.Idle);
        _drinking = false;
    }

    void ApplyHeal()
    {
        if (_stats == null) return;
        _stats.Heal(healHp);
        _stats.AddStamina(healStamina);
    }
}
