using UnityEngine;
using System.Collections;

// Concrete boss using the TwohandSW male set: a lean two-attack katana duelist.
// The defensive chassis (poise-conversion, close-dodge, tired deflect, intro, death)
// comes from BossBrainBase — this only defines the moveset + clip/phase bindings.
public class KatanaBoss : BossBrainBase
{
    [Header("Katana moveset")]
    public string attackATrigger = "AttackA";
    public string attackBTrigger = "AttackB";
    [Range(0f,1f)] public float attackAChance = 0.80f;   // 80% A / 20% B
    public float attackADamage = 35f;    // burly — hits hard
    public float attackBDamage = 50f;
    public float attackAReach  = 2.50f;  // long two-handed reach
    public float attackBReach  = 2.50f;
    public float attackARecover = 1.00f; // "stuck"/punish window after Attack A
    public float attackBRecover = 1.75f; // "stuck"/punish window after Attack B
    public float thinkDodgeCooldown = 10f;// min gap between the special "think dodge" reposition

    [Header("Attack phases — hurtbox LIVE only during the 'swing' frames (clips are 60fps)")]
    public float attackClipFps       = 60f;
    public int   attackAWindupFrames = 52;   // attackA windup: frames 0..52 (no hurtbox)
    public int   attackASwingFrames  = 18;   // attackA swing : next 18 frames (hurtbox); rest = "stuck"
    public int   attackBWindupFrames = 50;   // attackB windup: frames 0..50
    public int   attackBSwingFrames  = 20;   // attackB swing : next 20 frames; rest = "stuck"
    [Tooltip("Frame within attack A / B at which the swing sound (swingClip) plays. Same for both.")]
    public int   swingSoundFrame     = 33;   // daviddumaisaudio fires at this frame of both A and B

    float _thinkDodgeReadyAt;

    // Cycle: (think-dodge reposition once per cooldown -> walk back in) -> commit A/B -> punish window. (Dodging is now
    // purely reactive — the chassis steps the boss back 1m whenever it's damaged, on a cooldown.)
    protected override IEnumerator MeleeAttack()
    {
        if (Time.time >= _thinkDodgeReadyAt)        // the "thinking" step — replaces the Mimic's block-while-deciding
        {
            _thinkDodgeReadyAt = Time.time + thinkDodgeCooldown;
            yield return ThinkDodge();              // step back; the long 2.5m reach means no re-approach needed
        }
        BeginAttack();                              // ALWAYS commit to an attack (incl. right after a think-dodge)
        if (Random.value < attackAChance) yield return DoAttackA();
        else                              yield return DoAttackB();
    }

    IEnumerator DoAttackA()   // windup(52f) -> swing(18f, hurtbox) -> stuck(rest, = punish window)
    {
        float s0 = attackAWindupFrames / attackClipFps;
        float s1 = (attackAWindupFrames + attackASwingFrames) / attackClipFps;
        yield return Swing(attackATrigger, attackADamage, attackAReach, forwardNudge, s0, s1, 8f, swingSoundFrame / attackClipFps);
        EndAttack();
        if (IsPoiseBroken) yield break;
        yield return Recover(attackARecover);                        // "stuck" frames play out here (punish window)
    }                                                                // no break-out dodge — the boss only dodges when hit

    IEnumerator DoAttackB()   // windup(50f) -> swing(20f, hurtbox) -> stuck(rest)
    {
        float s0 = attackBWindupFrames / attackClipFps;
        float s1 = (attackBWindupFrames + attackBSwingFrames) / attackClipFps;
        yield return Swing(attackBTrigger, attackBDamage, attackBReach, forwardNudge, s0, s1, 12f, swingSoundFrame / attackClipFps);
        EndAttack();
        if (IsPoiseBroken) yield break;
        yield return Recover(attackBRecover);
    }
}
