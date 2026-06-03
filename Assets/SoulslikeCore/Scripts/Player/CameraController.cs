using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Kamera")]
    public Transform cameraTarget;
    public float     horizontalSpeed = 1f;
    public float     verticalSpeed   = 0.8f;
    public float     minVertical     = -20f;
    public float     maxVertical     = 60f;

    private PlayerInputHandler _input;
    private LockOnSystem       _lockOn;
    private float _yaw;
    private float _pitch;

    void Awake()
    {
        _input  = FindAnyObjectByType<PlayerInputHandler>();
        _lockOn = FindAnyObjectByType<LockOnSystem>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void LateUpdate()
    {
        if (_input == null) return;

        // Lock-on aktifse mouse birikimini sıfırla ve kamerayı döndürme
        if (_lockOn != null && _lockOn.IsLockedOn)
        {
            // Yaw ve pitch'i mevcut rotasyondan güncelle
            // böylece lock-on bitince kamera yerinde kalır
            _yaw   = cameraTarget.eulerAngles.y;
            _pitch = cameraTarget.eulerAngles.x;
            return;
        }

        Vector2 look = _input.LookInput;

        _yaw   += look.x * horizontalSpeed;
        _pitch -= look.y * verticalSpeed;
        _pitch  = Mathf.Clamp(_pitch, minVertical, maxVertical);

        if (cameraTarget != null)
            cameraTarget.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }
}