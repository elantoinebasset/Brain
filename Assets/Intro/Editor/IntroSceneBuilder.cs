using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BrainVR.Intro.EditorTools
{
    /// <summary>
    /// Tools > Intro VR > Créer la scène Intro :
    /// crée Assets/Intro/Scenes/Intro.unity (XR Origin, panneaux, voix off, fondu, particules)
    /// et la place en premier dans la liste des scènes du build, avant la scène du cerveau.
    /// Ne modifie PAS la scène du cerveau (SampleScene).
    /// </summary>
    public static class IntroSceneBuilder
    {
        const string IntroDir = "Assets/Intro";
        const string ScenePath = IntroDir + "/Scenes/Intro.unity";
        const string BrainScenePath = "Assets/Scenes/SampleScene.unity";
        const string XrOriginPrefab = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";
        const string HandsPermissionsPrefab = "Assets/VRTemplateAssets/Prefabs/Setup/Hands Permissions Manager.prefab";
        const string FadeMaterialPath = "Assets/Brain/Materials/Flash_alerte.mat";
        const string DustMaterialPath = "Assets/Brain/Materials/Poussiere_neuronale.mat";

        // Palette du doc
        static readonly Color Crimson = Hex(0xD6, 0x0A, 0x3D);
        static readonly Color YellowGreen = Hex(0x76, 0xCE, 0x12);
        static readonly Color LemonLime = Hex(0xE2, 0xEF, 0x2E);
        static readonly Color RebeccaPurple = Hex(0x59, 0x3C, 0x8F);
        static readonly Color Background = new Color(0.04f, 0.01f, 0.04f);
        static readonly Color PanelColor = new Color(0.06f, 0.02f, 0.06f, 0.88f);

        static TMP_FontAsset font;

        [MenuItem("Tools/Intro VR/Créer la scène Intro")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("Intro VR", "La scène Intro existe déjà. La recréer (elle sera remplacée) ?", "Recréer", "Annuler"))
                return;

            var xrPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginPrefab);
            if (xrPrefab == null)
            {
                EditorUtility.DisplayDialog("Intro VR", "Prefab XR Origin introuvable :\n" + XrOriginPrefab, "OK");
                return;
            }

            font = global::BrainVR.EditorTools.InterFontSetup.GetOrCreateDynamicInter();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- XR Origin (même rig que la scène du cerveau) ---
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(xrPrefab, scene);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var hands = AssetDatabase.LoadAssetAtPath<GameObject>(HandsPermissionsPrefab);
            if (hands != null) PrefabUtility.InstantiatePrefab(hands, scene);

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                SceneManager.MoveGameObjectToScene(es, scene);
            }

            // --- Ambiance : fond sombre, lumière douce ---
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.08f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Background;
            RenderSettings.fogDensity = 0.03f;
            var cam = rig.GetComponentInChildren<Camera>(true);
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Background;
            }
            AddLight("Lumiere crimson", new Vector3(-3f, 3f, 4f), Crimson, 6f, 12f);
            AddLight("Lumiere violette", new Vector3(3f, 2f, 5f), RebeccaPurple, 6f, 12f);

            // --- Particules « neurones / molécules » qui accélèrent à la transition ---
            var tunnel = CreateTunnel();

            // --- Fondu au noir (script ScreenFader d'Antoine) ---
            var faderGo = new GameObject("Fondu ecran");
            var fader = faderGo.AddComponent<ScreenFader>();
            fader.fadeMaterial = AssetDatabase.LoadAssetAtPath<Material>(FadeMaterialPath);
            fader.fadeInDuration = 2f;
            if (fader.fadeMaterial == null) Debug.LogWarning("[Intro] Matériau " + FadeMaterialPath + " introuvable : pas de fondu.");

            // --- Canvas VR ---
            var canvasGo = new GameObject("Canvas Intro", typeof(RectTransform));
            var canvasRt = (RectTransform)canvasGo.transform;
            canvasRt.SetPositionAndRotation(new Vector3(0f, 1.5f, 2.2f), Quaternion.identity);
            canvasRt.sizeDelta = new Vector2(1400f, 800f);
            canvasRt.localScale = Vector3.one * 0.0012f;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();

            // 1. Titre
            var titlePanel = Panel("Panneau - Titre", canvasRt, 0.75f);
            var title = Label("Titre", titlePanel, new Vector2(0f, 150f), new Vector2(1300f, 220f), "Entrez dans la simulation", 110, FontStyles.Bold);
            title.color = Color.white;
            Label("Sous-titre", titlePanel, new Vector2(0f, 10f), new Vector2(1200f, 80f),
                 "Un voyage au cœur du cerveau sous l'effet du THC", 44).color = new Color(1f, 1f, 1f, 0.75f);
            var startButton = MakeButton("Bouton - Entrer", titlePanel, new Vector2(0f, -170f), new Vector2(560f, 130f), "Entrer", Crimson, Color.white);

            // 2. Narration (sous-titres de la voix off)
            var narrationPanel = Panel("Panneau - Voix off", canvasRt, 0.88f);
            var narrationGroup = narrationPanel.gameObject.AddComponent<CanvasGroup>();
            narrationGroup.interactable = false;
            narrationGroup.blocksRaycasts = false;
            var lineTitle = Label("Titre phrase", narrationPanel, new Vector2(0f, 260f), new Vector2(1300f, 120f), "", 64, FontStyles.Bold);
            lineTitle.color = LemonLime;
            var lineText = Label("Texte phrase", narrationPanel, new Vector2(0f, -20f), new Vector2(1250f, 480f), "", 50);
            lineText.alignment = TextAlignmentOptions.Center;
            Label("Aide", narrationPanel, new Vector2(0f, -350f), new Vector2(1200f, 50f),
                 "Bouton A : phrase suivante", 30).color = new Color(1f, 1f, 1f, 0.45f);

            // 3. Règles
            var rulesPanel = Panel("Panneau - Regles", canvasRt, 0.92f);
            Label("Titre regles", rulesPanel, new Vector2(0f, 300f), new Vector2(1300f, 110f), "Règles de la simulation", 72, FontStyles.Bold).color = LemonLime;
            var rulesText = Label("Texte regles", rulesPanel, new Vector2(0f, 20f), new Vector2(1240f, 430f), "", 40);
            rulesText.alignment = TextAlignmentOptions.TopLeft;
            var rulesButton = MakeButton("Bouton - Continuer", rulesPanel, new Vector2(0f, -310f), new Vector2(480f, 110f), "J'ai compris", YellowGreen, Color.black);

            // 4. Choix de la quantité
            var dosePanel = Panel("Panneau - Choix dose", canvasRt, 0.92f);
            Label("Titre dose", dosePanel, new Vector2(0f, 300f), new Vector2(1300f, 110f), "Choisissez la quantité", 72, FontStyles.Bold).color = LemonLime;
            Label("Info dose", dosePanel, new Vector2(0f, 200f), new Vector2(1250f, 80f),
                 "Base : 1 joint ≈ 49 mg de THC", 40).color = new Color(1f, 1f, 1f, 0.8f);
            var dose49 = MakeButton("Bouton - 49 mg", dosePanel, new Vector2(-320f, -10f), new Vector2(560f, 260f),
                 "<size=140%><b>49 mg</b></size>\n1 joint", YellowGreen, Color.black);
            var dose490 = MakeButton("Bouton - 490 mg", dosePanel, new Vector2(320f, -10f), new Vector2(560f, 260f),
                 "<size=140%><b>490 mg</b></size>\n10 joints", Crimson, Color.white);
            Label("Remarque dose", dosePanel, new Vector2(0f, -260f), new Vector2(1250f, 120f),
                 "Avec 49 mg, vous pourrez augmenter la dose dans le cerveau jusqu'à 490 mg.\n" +
                 "Avec 490 mg, vous vivez directement une exposition massive.", 32).color = new Color(1f, 1f, 1f, 0.6f);

            // --- Déroulé ---
            var flowGo = new GameObject("Intro (deroule)");
            var voice = flowGo.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            var seq = flowGo.AddComponent<IntroSequencer>();
            seq.titleField = lineTitle;
            seq.textField = lineText;
            seq.group = narrationGroup;
            seq.voiceSource = voice;

            var flow = flowGo.AddComponent<IntroFlow>();
            flow.brainSceneName = System.IO.Path.GetFileNameWithoutExtension(BrainScenePath);
            flow.titlePanel = titlePanel.gameObject;
            flow.narrationPanel = narrationPanel.gameObject;
            flow.rulesPanel = rulesPanel.gameObject;
            flow.dosePanel = dosePanel.gameObject;
            flow.startButton = startButton;
            flow.rulesContinueButton = rulesButton;
            flow.dose49Button = dose49;
            flow.dose490Button = dose490;
            flow.rulesText = rulesText;
            rulesText.text = flow.rules;
            flow.sequencer = seq;
            flow.fader = fader;
            flow.tunnel = tunnel;

            // Seul l'écran titre est visible au départ
            narrationPanel.gameObject.SetActive(false);
            rulesPanel.gameObject.SetActive(false);
            dosePanel.gameObject.SetActive(false);

            // --- Sauvegarde + liste des scènes du build ---
            EnsureFolder("Assets", "Intro");
            EnsureFolder(IntroDir, "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuild();

            Selection.activeGameObject = flowGo;
            Debug.Log("[Intro] Scène créée : " + ScenePath + ". Lance Play : Entrée = suivant, 1 / 2 = dose, Espace = phrase suivante.");
        }

        [MenuItem("Tools/Intro VR/Mettre Intro en premier dans le build")]
        public static void AddToBuild()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            if (!scenes.Any(s => s.path == BrainScenePath))
                scenes.Add(new EditorBuildSettingsScene(BrainScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Intro] Ordre du build : " + string.Join(" → ", scenes.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path))));
        }

        // ---------------------------------------------------------------------
        static RectTransform Panel(string name, RectTransform parent, float alpha)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            var c = PanelColor;
            c.a = alpha;
            img.color = c;
            // Liseré crimson autour du panneau
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(Crimson.r, Crimson.g, Crimson.b, 0.9f);
            outline.effectDistance = new Vector2(5f, -5f);
            return rt;
        }

        static TextMeshProUGUI Label(string name, RectTransform parent, Vector2 pos, Vector2 size, string text, float fontSize,
                                    FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(string name, RectTransform parent, Vector2 pos, Vector2 size, string label, Color color, Color textColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = Color.white;
            var b = go.GetComponent<Button>();
            var cb = b.colors;
            cb.normalColor = color;
            cb.highlightedColor = Color.Lerp(color, Color.white, 0.35f);
            cb.pressedColor = Color.Lerp(color, LemonLime, 0.6f);
            cb.selectedColor = color;
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            var t = Label("Texte", rt, Vector2.zero, size - new Vector2(30f, 20f), label, 50, FontStyles.Bold);
            t.color = textColor;
            return b;
        }

        static void AddLight(string name, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        static ParticleSystem CreateTunnel()
        {
            var go = new GameObject("Particules - plongee");
            // Les particules naissent loin devant et viennent vers le joueur
            go.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, 25f), Quaternion.Euler(0f, 180f, 0f));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 12f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(LemonLime, RebeccaPurple);
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 40f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone; // émet le long de l'axe Z local (vers le joueur)
            shape.angle = 0f;
            shape.radius = 7f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(DustMaterialPath);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static Color Hex(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
    }
}
