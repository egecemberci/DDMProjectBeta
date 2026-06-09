using UnityEngine;
using System.Collections.Generic;

public class LockOnSystem : MonoBehaviour
{
    [Header("Ayarlar")]
    public float lockOnRange     = 15f;
    public float lockOnSpeed     = 5f;
    public float lockOnTargetHeight = 0.5f;
    public Transform cameraTarget;

    [Header("Hedef Değiştirme")]
    public float switchThreshold  = 0.3f;  // mouse ne kadar kaydırılınca geçiş olur
    public float switchCooldown   = 0.5f;  // geçişler arası bekleme süresi

    public bool      IsLockedOn   { get; private set; }
    public Transform LockedTarget { get; private set; }

    private PlayerInputHandler _input;
    private Camera             _cam;
    private float              _switchTimer;
    private float              _mouseAccumulator; // küçük hareketleri biriktir

    void Awake()
    {
        _input = GetComponent<PlayerInputHandler>();
        _cam   = Camera.main;
    }

    void Update()
    {
        if (_cam == null) _cam = Camera.main;

        if (_input.LockOnPressed) ToggleLockOn();

        if (_switchTimer > 0f) _switchTimer -= Time.deltaTime;

        if (IsLockedOn)
        {
            if (LockedTarget == null) { ClearLockOn(); return; }

            HandleTargetSwitch();
            UpdateRotation();
        }
    }

    void HandleTargetSwitch()
    {
        if (_switchTimer > 0f) return;

        // Mouse yatay hareketini biriktir
        float mouseX = _input.LookInput.x;
        _mouseAccumulator += mouseX * Time.deltaTime;

        // Eşiği aşınca hedef değiştir
        if (_mouseAccumulator > switchThreshold)
        {
            SwitchTarget(1);   // sağdaki düşmana geç
            _mouseAccumulator = 0f;
            _switchTimer      = switchCooldown;
        }
        else if (_mouseAccumulator < -switchThreshold)
        {
            SwitchTarget(-1);  // soldaki düşmana geç
            _mouseAccumulator = 0f;
            _switchTimer      = switchCooldown;
        }
    }

    void SwitchTarget(int direction)
    {
        List<Transform> enemies = GetEnemiesInRange();
        if (enemies.Count <= 1) return;

        // Mevcut hedefi bul
        int currentIndex = enemies.IndexOf(LockedTarget);
        if (currentIndex == -1) { LockedTarget = enemies[0]; return; }

        // Ekran üzerindeki X pozisyonuna göre sırala
        enemies.Sort((a, b) =>
        {
            float ax = _cam.WorldToScreenPoint(a.position).x;
            float bx = _cam.WorldToScreenPoint(b.position).x;
            return ax.CompareTo(bx);
        });

        currentIndex = enemies.IndexOf(LockedTarget);
        int nextIndex = currentIndex + direction;

        // Sınır kontrolü
        if (nextIndex < 0 || nextIndex >= enemies.Count) return;

        LockedTarget = enemies[nextIndex];
    }

    void UpdateRotation()
    {
        Vector3 dir = (LockedTarget.position - transform.position).normalized;
        dir.y = 0f;

        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation   = Quaternion.Slerp(
                transform.rotation, targetRot, lockOnSpeed * Time.deltaTime);

            Vector3 targetPos = LockedTarget.position + new Vector3(0, lockOnTargetHeight, 0);
            Vector3 lookDir   = targetPos - cameraTarget.position;
            Quaternion camRot = Quaternion.LookRotation(lookDir);
            cameraTarget.rotation = Quaternion.Slerp(
                cameraTarget.rotation, camRot, lockOnSpeed * Time.deltaTime);
        }
    }

    void ToggleLockOn()
    {
        if (IsLockedOn) { ClearLockOn(); return; }

        Transform nearest = FindNearestEnemy();
        if (nearest == null) return;

        LockedTarget = nearest;
        IsLockedOn   = true;
    }

    Transform FindNearestEnemy()
    {
        List<Transform> enemies = GetEnemiesInRange();
        if (enemies.Count == 0) return null;

        Transform nearest     = null;
        float     nearestDist = Mathf.Infinity;

        foreach (var e in enemies)
        {
            float dist = Vector3.Distance(transform.position, e.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest     = e;
            }
        }

        return nearest;
    }

    List<Transform> GetEnemiesInRange()
    {
        List<Transform> result = new List<Transform>();
        Collider[] hits = Physics.OverlapSphere(transform.position, lockOnRange);

        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Enemy")) continue;

            // Kamera görüş alanında mı kontrol et
            Vector3 screenPos = _cam.WorldToScreenPoint(hit.transform.position);
            if (screenPos.z < 0) continue; // arkadaysa ekleme

            result.Add(hit.transform);
        }

        return result;
    }

    void ClearLockOn()
    {
        IsLockedOn    = false;
        LockedTarget  = null;
        _mouseAccumulator = 0f;
    }

    public void ForceUnlock() => ClearLockOn();   // e.g. when the locked target dies
}