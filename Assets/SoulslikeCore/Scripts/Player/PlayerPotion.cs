using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerPotion : MonoBehaviour
{
    [Header("Potion")]
    public int maxPotions = 3;
    public float healHp = 50f;
    public float healStamina = 50f;
    [Range(0f,1f)] public float healAtFraction = 0.6f;

    [Header("Animation")]
    public string drinkTrigger = "Drink";
    public string drinkState = "Drink";
    public float drinkFallbackTime = 1.65f;

    [Header("Held model")]
    public GameObject potionModel;

    int _count;
    bool _drinking;

    PlayerStateMachine _sm;
    PlayerStats _stats;
    Animator _anim;

    public int PotionCount => _count;
    public bool IsDrinking => _drinking;

    void Awake()
    {
        _sm = GetComponent<PlayerStateMachine>();
        _stats = GetComponent<PlayerStats>();
        _anim = GetComponentInChildren<Animator>();

        _count = maxPotions;

        if (potionModel != null)
            potionModel.SetActive(false);
    }

    void Update()
    {
        if (_drinking) return;

        if (Keyboard.current == null || !Keyboard.current.rKey.wasPressedThisFrame)
            return;

        if (_count <= 0) return;
        if (!_sm.CanAct()) return;

        StartCoroutine(Drink());
    }

    IEnumerator Drink()
    {
        _drinking = true;
        _count--;

        _sm.ChangeState(PlayerState.UsingItem);

        if (potionModel != null)
            potionModel.SetActive(true);

        if (_anim != null)
            _anim.SetTrigger(drinkTrigger);

        float len = drinkFallbackTime;

        if (_anim != null)
        {
            yield return null;
            var st = _anim.GetCurrentAnimatorStateInfo(0);
            if (st.IsName(drinkState) && st.length > 0.05f)
                len = st.length;
        }

        bool healed = false;
        float t = 0f;

        while (t < len)
        {
            if (_sm.CurrentState != PlayerState.UsingItem)
                break;

            if (!healed && t >= len * healAtFraction)
            {
                _stats.Heal(healHp);
                _stats.AddStamina(healStamina);
                healed = true;
            }

            t += Time.deltaTime;
            yield return null;
        }

        if (!healed && _sm.CurrentState == PlayerState.UsingItem)
        {
            _stats.Heal(healHp);
            _stats.AddStamina(healStamina);
        }

        if (potionModel != null)
            potionModel.SetActive(false);

        if (_sm.CurrentState == PlayerState.UsingItem)
            _sm.ChangeState(PlayerState.Idle);

        _drinking = false;
    }

    public void ResetPotions()
    {
        _count = maxPotions;
    }
}