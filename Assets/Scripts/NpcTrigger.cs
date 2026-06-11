using UnityEngine;

public class NPCDialogueTrigger : MonoBehaviour
{
    public DialogueSystem.DialogueLine[] dialogue;
    public DialogueSystem dialogueSystem;

    private bool hasTalked = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !hasTalked)
        {
            dialogueSystem.StartDialogue(dialogue);
            hasTalked = true;
        }
    }
}