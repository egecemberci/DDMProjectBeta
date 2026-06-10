using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    void Start()
    {
        Cursor.visible = true;
    }

    public void OnPlayButton()
    {
        SceneManager.LoadScene("GameScene");
    }

    public void OnSettingsButton()
    {
        Debug.Log("Settings açıldı");
    }

    public void OnExitButton()
    {
        Application.Quit();
        Debug.Log("Oyun kapatıldı");
    }
}