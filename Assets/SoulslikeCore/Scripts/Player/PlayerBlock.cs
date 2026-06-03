using UnityEngine;

public class PlayerBlock : MonoBehaviour
{
    [Header("Ayarlar")]
    public float blockStaminaCost  = 5f;   // saniyede stamina tüketimi
    public float damageReduction   = 0.7f; // blok yapınca hasarın %70'i engellenir
    public float parryWindow       = 0.3f; // blok başlangıcında parry penceresi

    public bool IsBlocking { get; private set; }
    public bool IsParrying { get; private set; }

    private PlayerStateMachine _sm;
    private PlayerStats        _stats;
    private PlayerInputHandler _input;
    private Animator           _anim;
    private float              _parryTimer;

    void Awake()
    {
        _sm    = GetComponent<PlayerStateMachine>();
        _stats = GetComponent<PlayerStats>();
        _input = GetComponent<PlayerInputHandler>();
        _anim  = GetComponentInChildren<Animator>();
    }

   void Update()
   {
       if (!_sm.CanAct() && !IsBlocking) return;

       if (_input.BlockHeld && _stats.HasStamina(1f))
       {
           if (!IsBlocking) StartBlock(); // sadece bir kez çağrılır
           else HoldBlock();              // basılı tutulunca sadece stamina tüket
       }
       else
       {
           if (IsBlocking) StopBlock();
       }

       if (_parryTimer > 0f)
       {
           _parryTimer -= Time.deltaTime;
           IsParrying   = _parryTimer > 0f;
       }
   }

    void StartBlock()
    {
        IsBlocking  = true;
        IsParrying  = true;
        _parryTimer = parryWindow;
        _sm.ChangeState(PlayerState.Blocking);
        if (_anim != null) _anim.SetBool("IsBlocking", true);
    }

    void HoldBlock()
    {
        _stats.UseStamina(blockStaminaCost * Time.deltaTime);
    }

    void StopBlock()
    {
        IsBlocking = false;
        IsParrying = false;
        _sm.ChangeState(PlayerState.Idle);
        if (_anim != null) _anim.SetBool("IsBlocking", false);
    }

    // PlayerStats.TakeDamage'dan çağrılır
    public float ProcessDamage(float incomingDamage)
    {
        if (IsParrying)
        {
            // Parry — sıfır hasar, karşı saldırı fırsatı
            if (_anim != null) _anim.SetTrigger("Parry");
            return 0f;
        }

        if (IsBlocking)
        {
            // Blok — hasarı azalt, stamina harca
            _stats.UseStamina(incomingDamage * 0.5f);
            return incomingDamage * (1f - damageReduction);
        }

        return incomingDamage;
    }
}