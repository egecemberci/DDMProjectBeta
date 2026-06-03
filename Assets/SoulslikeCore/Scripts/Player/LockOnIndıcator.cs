using UnityEngine;
using UnityEngine.UI;

public class LockOnIndicator : MonoBehaviour
{
    public Image     indicator;
    public LockOnSystem lockOnSystem;
    public Vector3   worldOffset = new Vector3(0, 2f, 0); // düşmanın başı üstü

    private Camera _cam;
    private RectTransform _rect;

    void Awake()
    {
        _cam  = Camera.main;
        _rect = indicator.GetComponent<RectTransform>();
        indicator.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;

        if (lockOnSystem == null || !lockOnSystem.IsLockedOn || lockOnSystem.LockedTarget == null)
        {
            indicator.gameObject.SetActive(false);
            return;
        }

        indicator.gameObject.SetActive(true);

        // Düşmanın dünya pozisyonunu ekran pozisyonuna çevir
        Vector3 worldPos   = lockOnSystem.LockedTarget.position + worldOffset;
        Vector3 screenPos  = _cam.WorldToScreenPoint(worldPos);

        // Düşman arkadaysa gizle
        if (screenPos.z < 0)
        {
            indicator.gameObject.SetActive(false);
            return;
        }

        _rect.position = screenPos;
    }
}