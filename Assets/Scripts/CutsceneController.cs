using UnityEngine;
using UnityEngine.Video;
using UnityEngine.InputSystem;
using System.Collections;

public class CutsceneController : MonoBehaviour
{
    public VideoPlayer videoPlayer;

    bool isEnding = false;

    void Start()
    {
        videoPlayer.Play();
    }

    void Update()
    {
        if (isEnding) return;

        // Skip input
        if (Keyboard.current.spaceKey.wasPressedThisFrame ||
            Keyboard.current.enterKey.wasPressedThisFrame)
        {
            StartCoroutine(EndCutscene());
        }

        // SAFE END CHECK (reliable)
        if (videoPlayer.frame > 0 &&
            !videoPlayer.isPlaying &&
            videoPlayer.frame >= (long)videoPlayer.frameCount - 1)
        {
            StartCoroutine(EndCutscene());
        }
    }

    IEnumerator EndCutscene()
    {
        if (isEnding) yield break;

        isEnding = true;

        videoPlayer.Stop();

        yield return null;

        gameObject.SetActive(false);
    }
}