using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class DialogueSystem : MonoBehaviour
{
    [System.Serializable]
    public class DialogueLine
    {
        public string speaker;
        [TextArea] public string text;
    }

    public TextMeshProUGUI subtitleText;

    public MonoBehaviour playerMovement; // drag movement script here

    private Queue<DialogueLine> lines = new Queue<DialogueLine>();

    private bool isActive = false;

    void Update()
    {
        if (!isActive) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            EndDialogue();
        }

        if (Keyboard.current.spaceKey.wasPressedThisFrame ||
            Keyboard.current.enterKey.wasPressedThisFrame)
        {
            DisplayNextLine();
        }
    }

    public void StartDialogue(DialogueLine[] dialogue)
    {
        isActive = true;

        playerMovement.enabled = false;

        lines.Clear();

        foreach (var line in dialogue)
            lines.Enqueue(line);

        subtitleText.gameObject.SetActive(true);

        DisplayNextLine();
    }

    void DisplayNextLine()
    {
        if (lines.Count == 0)
        {
            EndDialogue();
            return;
        }

        DialogueLine line = lines.Dequeue();
        subtitleText.text = $"<b>{line.speaker}</b>\n{line.text}";
    }

    void EndDialogue()
    {
        isActive = false;

        subtitleText.text = "";
        subtitleText.gameObject.SetActive(false);

        playerMovement.enabled = true;
    }
}