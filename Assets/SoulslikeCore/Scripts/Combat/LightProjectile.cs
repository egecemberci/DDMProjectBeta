using UnityEngine;

public class LightProjectile : MonoBehaviour
{
    private float _damage;
    private float _speed;
    private bool _initialized;

    public void Init(float damage, float speed)
    {
        _damage = damage;
        _speed = speed;
        _initialized = true;
        Destroy(gameObject, 5f); // 5 saniye sonra yok ol
    }

    void Update()
    {
        if (!_initialized) return;
        transform.Translate(Vector3.forward * _speed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Enemy")) return;

        if (other.TryGetComponent<IDamageable>(out var target))
            target.TakeDamage(_damage, 0, transform.position);

        Destroy(gameObject);
    }
}