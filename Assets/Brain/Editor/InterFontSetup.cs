using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace BrainVR.EditorTools
{
    /// <summary>
    /// Tools > Cerveau VR - Appliquer la police Inter
    /// Crée une version DYNAMIQUE d'Inter (accents, « », ’ …), la met en police TMP par défaut,
    /// l'ajoute en fallback aux assets Inter du template (qui ne contiennent que l'ASCII)
    /// et remplace LiberationSans sur tous les textes (prefabs + scènes ouvertes).
    /// </summary>
    public static class InterFontSetup
    {
        const string SourceFontPath = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf";
        const string FontDir = "Assets/Brain/Fonts";
        const string DynamicAssetPath = FontDir + "/Inter-Regular SDF Dynamique.asset";

        static readonly string[] TemplateInterAssets =
        {
            "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset",
            "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF NoBackfaceCulling.asset",
        };

        [MenuItem("Tools/Cerveau VR - Appliquer la police Inter")]
        public static void Apply()
        {
            var inter = GetOrCreateDynamicInter();
            if (inter == null) return;

            // 1. Les assets Inter du template gardent leurs matériaux spéciaux (overlay, x-ray),
            //    on leur ajoute juste Inter dynamique en fallback pour les accents.
            var templateFonts = new HashSet<TMP_FontAsset>();
            foreach (var p in TemplateInterAssets)
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(p);
                if (fa == null) continue;
                templateFonts.Add(fa);
                if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!fa.fallbackFontAssetTable.Contains(inter))
                {
                    fa.fallbackFontAssetTable.Insert(0, inter);
                    EditorUtility.SetDirty(fa);
                }
            }

            // 2. Police par défaut de TextMeshPro (tout nouveau texte sera en Inter).
            var settings = TMP_Settings.instance;
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                var prop = so.FindProperty("m_defaultFontAsset");
                if (prop != null)
                {
                    prop.objectReferenceValue = inter;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(settings);
                }
            }

            // 3. Prefabs du projet.
            int prefabs = 0, prefabTexts = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".prefab") || path.StartsWith("Assets/TextMesh Pro")) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                int n = 0;
                foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                    if (Retarget(t, inter, templateFonts, false)) n++;
                if (n > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabs++;
                    prefabTexts += n;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            // 4. Scènes ouvertes (y compris objets désactivés).
            int sceneTexts = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                int n = 0;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                        if (Retarget(t, inter, templateFonts, true)) n++;
                if (n > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    sceneTexts += n;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Cerveau VR] Police Inter appliquée : {prefabTexts} textes dans {prefabs} prefabs, " +
                      $"{sceneTexts} textes dans la/les scène(s) ouverte(s). Pense à sauvegarder la scène (Ctrl+S).");
        }

        static bool Retarget(TMP_Text t, TMP_FontAsset inter, HashSet<TMP_FontAsset> keep, bool undo)
        {
            var current = t.font;
            if (current == inter) return false;
            if (current != null && keep.Contains(current)) return false; // déjà Inter (template) + fallback

            if (undo) Undo.RecordObject(t, "Police Inter");
            t.font = inter;
            t.fontSharedMaterial = inter.material;
            EditorUtility.SetDirty(t);
            if (undo && PrefabUtility.IsPartOfPrefabInstance(t))
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            return true;
        }

        public static TMP_FontAsset GetOrCreateDynamicInter()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicAssetPath);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (font == null)
            {
                Debug.LogError($"[Cerveau VR] Police introuvable : {SourceFontPath}");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(FontDir))
                AssetDatabase.CreateFolder("Assets/Brain", "Fonts");

            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                   AtlasPopulationMode.Dynamic, true);
            fa.name = Path.GetFileNameWithoutExtension(DynamicAssetPath);
            AssetDatabase.CreateAsset(fa, DynamicAssetPath);

            // L'atlas et le matériau doivent être sauvegardés DANS l'asset, sinon ils sont perdus.
            fa.atlasTextures[0].name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);

            // Pré-génère les caractères courants (ASCII + Latin-1 + typographie FR)
            // pour éviter un à-coup la première fois qu'ils s'affichent dans le casque.
            var sb = new StringBuilder();
            for (int c = 32; c <= 126; c++) sb.Append((char)c);
            for (int c = 160; c <= 255; c++) sb.Append((char)c);
            sb.Append("œŒ«»‘’“”–—…€°•→←↑↓×");
            fa.TryAddCharacters(sb.ToString(), out _);

            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(DynamicAssetPath);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicAssetPath);
        }
    }
}
