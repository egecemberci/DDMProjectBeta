using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class DeathScreen : MonoBehaviour
{
    [Header("UI Elemanları")]
    public CanvasGroup deathPanel;
    public float       fadeInDuration   = 1.5f;
    public float       restartDelay     = 3f; // kaç saniye sonra yeniden başlasın

    private PlayerStats _stats;

    void Awake()
    {
        _stats = FindAnyObjectByType<PlayerStats>();
        _stats.OnDied += OnPlayerDied;

        deathPanel.alpha          = 0f;
        deathPanel.interactable   = false;
        deathPanel.blocksRaycasts = false;
    }

    void OnDestroy()
    {
        _stats.OnDied -= OnPlayerDied;
    }

    void OnPlayerDied()
    {
        StartCoroutine(ShowDeathScreen());
    }

    IEnumerator ShowDeathScreen()
    {
        yield return new WaitForSeconds(1.5f);

        deathPanel.interactable   = true;
        deathPanel.blocksRaycasts = true;

        // Ekranı fade in yap
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed          += Time.deltaTime;
            deathPanel.alpha  = elapsed / fadeInDuration;
            yield return null;
        }

        deathPanel.alpha = 1f;

        // Bekleme süresi sonra sahneyi yeniden yükle
        yield return new WaitForSeconds(restartDelay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}