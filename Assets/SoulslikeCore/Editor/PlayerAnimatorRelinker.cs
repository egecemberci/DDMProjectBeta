#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Re-binds the PlayerAnimator controller's state motions to AnimationClips found
/// BY FILENAME, not by GUID. This makes the controller portable between projects:
/// the same animation clips can exist in another project with completely different
/// .meta/GUIDs, and this tool will re-link them as long as the clip filenames match.
///
/// Source of truth = PlayerAnimator.clipmap.json (state -> clip-name map), which is
/// captured from the working project so it does NOT depend on the controller's
/// (possibly broken) serialized clip references.
///
/// Runs automatically after relevant imports (drag-and-drop into a new project),
/// and is also available via the Tools menu.
/// </summary>
public static class PlayerAnimatorRelinker
{
    const string ControllerName = "PlayerAnimator";       // PlayerAnimator.controller
    const string ClipMapName    = "PlayerAnimator.clipmap"; // PlayerAnimator.clipmap.json (TextAsset)

    [System.Serializable] class Entry   { public string layer; public string state; public string clip; }
    [System.Serializable] class ClipMap { public List<Entry> entries = new List<Entry>(); }

    [MenuItem("Tools/Soulslike/Relink Player Animator (by filename)")]
    public static void RelinkMenu() => Relink(verbose: true);

    public static void Relink(bool verbose = false)
    {
        var controller = FindController();
        if (controller == null) { if (verbose) Debug.LogWarning("[Relinker] PlayerAnimator.controller not found."); return; }

        var map = LoadClipMap();
        if (map == null) { if (verbose) Debug.LogWarning("[Relinker] PlayerAnimator.clipmap.json not found."); return; }

        var want = new Dictionary<string, string>();
        foreach (var e in map.entries) want[e.layer + "|" + e.state] = e.clip;

        int assigned = 0, alreadyOk = 0, missing = 0;
        bool changed = false;

        foreach (var layer in controller.layers)
        {
            foreach (var cs in layer.stateMachine.states)
            {
                if (!want.TryGetValue(layer.name + "|" + cs.state.name, out var clipName)) continue;

                var current = cs.state.motion as AnimationClip;
                if (current != null && current.name == clipName) { alreadyOk++; continue; }

                var clip = FindClipByName(clipName);
                if (clip == null) { missing++; if (verbose) Debug.LogWarning($"[Relinker] clip not found by name: '{clipName}' (state '{cs.state.name}')"); continue; }

                cs.state.motion = clip;
                assigned++;
                changed = true;
            }
        }

        if (changed) { EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); }

        if (verbose || assigned > 0 || missing > 0)
            Debug.Log($"[Relinker] PlayerAnimator: assigned={assigned}, alreadyOk={alreadyOk}, missing={missing}.");
    }

    static AnimatorController FindController()
    {
        foreach (var guid in AssetDatabase.FindAssets($"{ControllerName} t:AnimatorController"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == ControllerName)
                return AssetDatabase.LoadAssetAtPath<AnimatorController>(p);
        }
        return null;
    }

    static ClipMap LoadClipMap()
    {
        foreach (var guid in AssetDatabase.FindAssets($"{ClipMapName} t:TextAsset"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == ClipMapName)
            {
                var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(p);
                if (ta != null) return JsonUtility.FromJson<ClipMap>(ta.text);
            }
        }
        return null;
    }

    // Finds an AnimationClip by exact name, whether it's a standalone .anim or an FBX sub-asset.
    static AnimationClip FindClipByName(string clipName)
    {
        var clip = SearchType(clipName, "AnimationClip");
        if (clip != null) return clip;
        return SearchType(clipName, "Model"); // FBX whose embedded clip matches
    }

    static AnimationClip SearchType(string clipName, string typeFilter)
    {
        foreach (var guid in AssetDatabase.FindAssets($"{clipName} t:{typeFilter}"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
            {
                if (o is AnimationClip c && c.name == clipName) return c;
            }
        }
        return null;
    }

    // Auto-run after imports so dragging this folder into a project just works.
    class Postprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var a in imported)
            {
                if (a.EndsWith(".controller") || a.EndsWith(".fbx") || a.EndsWith(".anim") || a.EndsWith(".json"))
                {
                    // Defer until after the import batch fully settles; the "alreadyOk" guard
                    // in Relink() makes the resulting re-import idempotent (no infinite loop).
                    EditorApplication.delayCall += () => Relink(verbose: false);
                    return;
                }
            }
        }
    }
}
#endif
