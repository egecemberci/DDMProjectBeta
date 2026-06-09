using UnityEngine;
using UnityEditor;
using System.IO;

// One-shot helper: pulls the Generic npc clips out of their FBX files into standalone .anim assets
// (so they're easy to reference), then assigns them to the Enemy prefab's MinionEnemy.
// Run via: Tools ▸ Extract Minion Clips.
public static class MinionClipExtractor
{
    [MenuItem("Tools/Extract Minion Clips")]
    public static void Extract()
    {
        const string dir = "Assets/SoulslikeCore/Animations/Minion";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/SoulslikeCore/Animations", "Minion");

        var walk = ExtractClip("Assets/GFTSFiles/AnimDemo/hostilenpcwalk.fbx",   dir + "/MinionWalk.anim");
        var atk  = ExtractClip("Assets/GFTSFiles/AnimDemo/hostilenpcattack.fbx", dir + "/MinionAttack.anim");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SoulslikeCore/Prefabs/Enemy.prefab");
        bool assigned = false;
        if (prefab != null)
        {
            var me = prefab.GetComponent<MinionEnemy>();
            if (me != null)
            {
                if (walk != null) me.walkClip   = walk;
                if (atk  != null) me.attackClip = atk;
                EditorUtility.SetDirty(me);
                PrefabUtility.SavePrefabAsset(prefab);
                assigned = true;
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MinionClipExtractor] walk=" + (walk != null) + " attack=" + (atk != null) +
                  " | assignedToPrefab=" + assigned +
                  (walk != null ? " | walkLen=" + walk.length.ToString("F2") + "s fps=" + walk.frameRate : "") +
                  (atk  != null ? " | attackLen=" + atk.length.ToString("F2") + "s fps=" + atk.frameRate + " (" + Mathf.RoundToInt(atk.length * atk.frameRate) + " frames)" : ""));
    }

    static AnimationClip ExtractClip(string fbxPath, string outPath)
    {
        AnimationClip src = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            var c = o as AnimationClip;
            if (c != null && !c.name.StartsWith("__preview")) { src = c; break; }
        }
        if (src == null) { Debug.LogWarning("[MinionClipExtractor] no clip in " + fbxPath); return null; }

        var copy = Object.Instantiate(src);
        copy.name = Path.GetFileNameWithoutExtension(outPath);
        AssetDatabase.CreateAsset(copy, outPath);
        return copy;
    }
}
