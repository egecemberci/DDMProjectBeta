using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class NPCDialogue : MonoBehaviour
{
    public GameObject invisibleWall;
    public GameObject interactPrompt;

    private bool playerNearby = false;
    private bool dialogueFinished = false;

    void Start()
    {
        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }

    void Update()
    {
        if (playerNearby && !dialogueFinished)
        {
            if (interactPrompt != null)
                interactPrompt.SetActive(true);

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                StartDialogue();
            }
        }
        else
        {
            if (interactPrompt != null)
                interactPrompt.SetActive(false);
        }
    }

    void StartDialogue()
    {
        Debug.Log("Hello traveler! Good luck on your journey.");

        dialogueFinished = true;

        if (invisibleWall != null)
            invisibleWall.SetActive(false);

        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            playerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            playerNearby = false;
    }
}