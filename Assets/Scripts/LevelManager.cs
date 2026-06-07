using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : MonoBehaviour
{
    private string[] levels = {
        "Canyonlevel",
        "Desertlevel",
        "Librarylevel"
    };

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            LoadNextLevel();
        }

        if (Input.GetKeyDown(KeyCode.O))
        {
            LoadPreviousLevel();
        }
    }

    void LoadNextLevel()
    {
        int currentIndex = GetCurrentLevelIndex();
        int nextIndex = currentIndex + 1;

        if (nextIndex < levels.Length)
        {
            SceneManager.LoadScene(levels[nextIndex]);
        }
    }

    void LoadPreviousLevel()
    {
        int currentIndex = GetCurrentLevelIndex();
        int prevIndex = currentIndex - 1;

        if (prevIndex >= 0)
        {
            SceneManager.LoadScene(levels[prevIndex]);
        }
    }

    int GetCurrentLevelIndex()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i] == currentScene)
                return i;
        }

        return 0;
    }
}