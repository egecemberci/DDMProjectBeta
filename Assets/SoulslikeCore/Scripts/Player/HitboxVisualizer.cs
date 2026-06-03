using UnityEngine;

public class HitboxVisualizer : MonoBehaviour
{
    [Header("Hafif Saldırı")]
    public Vector3 lightAttackOffset = new Vector3(0, 0, 1.5f);
    public float   lightAttackRadius = 1f;
    public Color   lightAttackColor  = new Color(1f, 0.5f, 0f, 0.3f);

    [Header("Ağır Saldırı")]
    public Vector3 heavyAttackOffset = new Vector3(0, 0, 1.8f);
    public float   heavyAttackRadius = 1.2f;
    public Color   heavyAttackColor  = new Color(1f, 0f, 0f, 0.3f);

    [Header("Dodge")]
    public float dodgeRadius = 0.8f;
    public Color dodgeColor  = new Color(0f, 1f, 1f, 0.2f);

    private PlayerStateMachine _sm;

    void Awake()
    {
        _sm = GetComponent<PlayerStateMachine>();
    }

    void OnDrawGizmos()
    {
        if (_sm == null)
        {
            DrawSoluk();
            return;
        }

        switch (_sm.CurrentState)
        {
            case PlayerState.LightAttacking:
                DrawHitbox(lightAttackOffset, lightAttackRadius, lightAttackColor);
                break;
            case PlayerState.HeavyAttacking:
                DrawHitbox(heavyAttackOffset, heavyAttackRadius, heavyAttackColor);
                break;
            case PlayerState.Dodging:
                DrawDodge();
                break;
            default:
                DrawSoluk();
                break;
        }
    }

    void DrawHitbox(Vector3 offset, float radius, Color color)
    {
        Vector3 pos  = transform.position + transform.TransformDirection(offset);

        Gizmos.color = color;
        Gizmos.DrawSphere(pos, radius);

        Gizmos.color = new Color(color.r, color.g, color.b, 1f);
        Gizmos.DrawWireSphere(pos, radius);
    }

    void DrawDodge()
    {
        Gizmos.color = dodgeColor;
        Gizmos.DrawSphere(transform.position + Vector3.up, dodgeRadius);

        Gizmos.color = new Color(dodgeColor.r, dodgeColor.g, dodgeColor.b, 1f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up, dodgeRadius);
    }

    void DrawSoluk()
    {
        Gizmos.color = new Color(lightAttackColor.r, lightAttackColor.g, lightAttackColor.b, 0.08f);
        Gizmos.DrawSphere(transform.position + transform.TransformDirection(lightAttackOffset), lightAttackRadius);

        Gizmos.color = new Color(heavyAttackColor.r, heavyAttackColor.g, heavyAttackColor.b, 0.08f);
        Gizmos.DrawSphere(transform.position + transform.TransformDirection(heavyAttackOffset), heavyAttackRadius);
    }
}