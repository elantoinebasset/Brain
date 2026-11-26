using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using UnityEditor.Events;
using UnityEngine.Events;
using Unity.VRTemplate;

namespace BrainVR.EditorTools
{
    /// <summary>
    /// Tools > Cerveau VR : génère dans la scène ouverte l'intérieur d'un cerveau
    /// (paroi plissée, neurones avec dendrites, axones myélinisés, poussière, lumières).
    /// </summary>
    public class BrainGeneratorWindow : EditorWindow
    {
        const string RootDir = "Assets/Brain";
        const string MatDir = RootDir + "/Materials";
        const string GenDir = RootDir + "/Generated";
        const string RootName = "Cerveau (environnement)";
        const string PanelName = "Panneau - Creer un neurone";
        const float CavityCenterY = 1f;
        const float FloorY = -0.05f;

        int seed = 1234;
        int neuronCount = 40;
        int connectionsPerNeuron = 3;
        float maxConnectionDistance = 6.5f;
        float cavityRadius = 14f;
        float cavityHeight = 10f;
        float playerClearRadius = 2.8f;
        bool hideTemplateEnvironment = true;
        enum PanelMode { Fixe, Poignet, InfobulleMainGauche }
        static PanelMode panelMode = PanelMode.InfobulleMainGauche;
        const string CalloutName = "Callout - Creer un neurone";

        struct Mats
        {
            public Material tissue, neuron, axon, myelin, pulse, trail, dust;
        }

        [MenuItem("Tools/Cerveau VR")]
        static void Open() => GetWindow<BrainGeneratorWindow>("Cerveau VR");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Intérieur de cerveau — générateur", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Génère l'environnement dans la scène ouverte, autour de l'origine (là où est le XR Origin).", MessageType.Info);
            seed = EditorGUILayout.IntField("Graine aléatoire", seed);
            neuronCount = EditorGUILayout.IntSlider("Nombre de neurones", neuronCount, 5, 80);
            connectionsPerNeuron = EditorGUILayout.IntSlider("Connexions max / neurone", connectionsPerNeuron, 1, 6);
            maxConnectionDistance = EditorGUILayout.Slider("Distance max d'une connexion", maxConnectionDistance, 2f, 12f);
            cavityRadius = EditorGUILayout.Slider("Rayon de la cavité", cavityRadius, 8f, 25f);
            cavityHeight = EditorGUILayout.Slider("Hauteur de la cavité", cavityHeight, 5f, 15f);
            playerClearRadius = EditorGUILayout.Slider("Zone libre autour du joueur", playerClearRadius, 1f, 6f);
            hideTemplateEnvironment = EditorGUILayout.Toggle("Masquer le décor du template", hideTemplateEnvironment);

            EditorGUILayout.Space();
            if (GUILayout.Button("Générer / Régénérer", GUILayout.Height(32))) Generate();
            if (GUILayout.Button("Nouvelle graine + générer"))
            {
                seed = new System.Random().Next(0, 99999);
                Generate();
            }
            EditorGUILayout.Space();
            panelMode = (PanelMode)EditorGUILayout.EnumPopup("Affichage du panneau", panelMode);
            if (GUILayout.Button("Ajouter / recréer le panneau « Créer un neurone »", GUILayout.Height(26))) CreateSpawnerPanel();
            if (GUILayout.Button("Ajouter / recréer le panneau de textes", GUILayout.Height(26))) CreateTextPanel();
            EditorGUILayout.Space();
            if (GUILayout.Button("Réafficher le décor du template")) SetTemplateEnvironmentVisible(true);
            if (GUILayout.Button("Supprimer le cerveau"))
            {
                var old = GameObject.Find(RootName);
                if (old) Undo.DestroyObjectImmediate(old);
            }
        }

        // =====================================================================
        void Generate()
        {
            var old = GameObject.Find(RootName);
            if (old) Undo.DestroyObjectImmediate(old);

            EnsureFolder("Assets", "Brain");
            EnsureFolder(RootDir, "Materials");
            EnsureFolder(RootDir, "Generated");

            var mats = CreateMaterials();
            Random.InitState(seed);

            string meshPath = GenDir + "/BrainMeshes.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var meshes = new List<Mesh>();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Générer le cerveau");
            var env = root.AddComponent<BrainEnvironment>();

            // --- Paroi (cavité plissée) ---
            var cavity = BuildCavity();
            meshes.Add(cavity);
            CreateRenderer("Paroi cerebrale", root.transform, Vector3.zero, Quaternion.identity, cavity, new[] { mats.tissue });

            // --- Neurones ---
            var positions = PlaceNeurons();
            var neuronsRoot = new GameObject("Neurones");
            neuronsRoot.transform.SetParent(root.transform, false);
            var somaRadii = new float[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                var b = new BrainMeshBuilder(1);
                float r = Random.Range(0.17f, 0.3f);
                somaRadii[i] = r;
                NeuronMeshFactory.BuildNeuron(b, r);
                var m = b.ToMesh("Neurone_" + i);
                meshes.Add(m);
                var mr = CreateRenderer("Neurone " + i, neuronsRoot.transform, positions[i], Random.rotation, m, new[] { mats.neuron });
                env.neurons.Add(new BrainEnvironment.Neuron { renderer = mr, position = positions[i] });
            }

            // --- Axones (connexions) : un objet par connexion pour pouvoir les retirer en jeu ---
            var axonsRoot = new GameObject("Axones");
            axonsRoot.transform.SetParent(root.transform, false);
            foreach (var pair in BuildPairs(positions))
            {
                var pts = AxonPath(positions[pair.x], positions[pair.y]);
                int idx = env.connections.Count;
                var ab = new BrainMeshBuilder(2);
                NeuronMeshFactory.BuildAxon(ab, pts, somaRadii[pair.y]);
                var am = ab.ToMesh("Axone_" + idx);
                meshes.Add(am);
                var amr = CreateRenderer("Axone " + idx, axonsRoot.transform, Vector3.zero, Quaternion.identity, am, new[] { mats.axon, mats.myelin });
                env.connections.Add(new BrainEnvironment.Connection { a = pair.x, b = pair.y, points = pts, renderer = amr });
                env.neurons[pair.x].connections.Add(idx);
                env.neurons[pair.y].connections.Add(idx);
            }

            // --- Impulsions ---
            var pb = new BrainMeshBuilder(1);
            pb.AddSphere(Vector3.zero, 0.5f, Quaternion.identity, Vector3.one, 2, 0f, Vector3.zero, 0);
            var pulseMesh = pb.ToMesh("Impulsion");
            meshes.Add(pulseMesh);
            env.pulseMesh = pulseMesh;
            env.pulseMaterial = mats.pulse;
            env.trailMaterial = mats.trail;
            env.neuronsContainer = neuronsRoot.transform;
            env.neuronMaterial = mats.neuron;
            env.axonMaterial = mats.axon;
            env.myelinMaterial = mats.myelin;

            CreateDust(root.transform, mats.dust);
            CreateLights(root.transform);

            // --- Sauvegarde des meshes dans un seul asset ---
            AssetDatabase.CreateAsset(meshes[0], meshPath);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
            AssetDatabase.SaveAssets();

            env.ApplyAtmosphere();
            if (hideTemplateEnvironment) SetTemplateEnvironmentVisible(false);
            TuneDirectionalLights(root);

            var panel = Object.FindFirstObjectByType<NeuronSpawnerPanel>();
            if (panel) { Undo.RecordObject(panel, "Panneau"); panel.env = env; }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Cerveau VR] Généré : {positions.Count} neurones, {env.connections.Count} connexions.");
        }

        // =====================================================================
        // Cavité
        Mesh BuildCavity()
        {
            var ico = BrainMeshBuilder.Icosphere(5);
            var center = new Vector3(0f, CavityCenterY, 0f);
            var verts = new Vector3[ico.verts.Count];
            for (int i = 0; i < verts.Length; i++)
            {
                var u = ico.verts[i];
                var p = center + new Vector3(u.x * cavityRadius, u.y * cavityHeight, u.z * cavityRadius);
                float folds = BrainNoise.Ridged(p * 0.22f);
                folds = Mathf.SmoothStep(0f, 1f, folds);
                float detail = BrainNoise.Sample(p * 0.8f + new Vector3(5f, 9f, 2f));
                float scale = 1f - 0.08f * folds - 0.015f * detail;
                p = center + (p - center) * scale;
                if (p.y < FloorY)
                {
                    // Sol : légèrement bosselé mais toujours sous y=0 (téléportation OK)
                    float bump = BrainNoise.Sample(new Vector3(p.x, 0f, p.z) * 0.35f);
                    p.y = FloorY - 0.3f * bump;
                }
                verts[i] = p;
            }
            var b = new BrainMeshBuilder(1);
            b.AddRaw(verts, ico.tris, true, 0); // faces vers l'intérieur
            return b.ToMesh("Cavite");
        }

        float CavityTop(float d)
        {
            float k = d / cavityRadius;
            return CavityCenterY + cavityHeight * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
        }

        // =====================================================================
        // Neurones
        List<Vector3> PlaceNeurons()
        {
            var list = new List<Vector3>();
            int tries = 0;
            while (list.Count < neuronCount && tries < neuronCount * 300)
            {
                tries++;
                float ang = Random.value * Mathf.PI * 2f;
                float d = Mathf.Lerp(playerClearRadius, cavityRadius - 3f, Mathf.Sqrt(Random.value));
                float top = CavityTop(d) - 2.2f;
                if (top < 1.2f) continue;
                float y = Random.Range(0.9f, top);
                var p = new Vector3(Mathf.Cos(ang) * d, y, Mathf.Sin(ang) * d);
                bool ok = true;
                foreach (var q in list)
                    if ((q - p).sqrMagnitude < 1.8f * 1.8f) { ok = false; break; }
                if (ok) list.Add(p);
            }
            return list;
        }

        // =====================================================================
        // Connexions
        List<Vector2Int> BuildPairs(List<Vector3> pos)
        {
            var seen = new HashSet<long>();
            var res = new List<Vector2Int>();
            for (int i = 0; i < pos.Count; i++)
            {
                var order = new List<int>();
                for (int j = 0; j < pos.Count; j++) if (j != i) order.Add(j);
                int ci = i;
                order.Sort((x, y) => (pos[x] - pos[ci]).sqrMagnitude.CompareTo((pos[y] - pos[ci]).sqrMagnitude));

                int wanted = Random.Range(Mathf.Min(2, connectionsPerNeuron), connectionsPerNeuron + 1);
                int added = 0;
                foreach (int j in order)
                {
                    if (added >= wanted) break;
                    if (added > 0 && Vector3.Distance(pos[i], pos[j]) > maxConnectionDistance) break;
                    int a = Mathf.Min(i, j), c = Mathf.Max(i, j);
                    if (seen.Add((long)a * 100000 + c)) res.Add(new Vector2Int(i, j));
                    added++;
                }
            }
            return res;
        }

        Vector3[] AxonPath(Vector3 a, Vector3 b)
        {
            var pts = NeuronMeshFactory.AxonPath(a, b);
            for (int i = 0; i < pts.Length; i++)
            {
                var p = pts[i];
                float hd = new Vector2(p.x, p.z).magnitude;
                p.y = Mathf.Clamp(p.y, 0.35f, Mathf.Max(0.5f, CavityTop(hd) - 0.8f));
                pts[i] = p;
            }
            return pts;
        }

        // =====================================================================
        // Ambiance
        // =====================================================================
        // Panneau "Créer un neurone"
        static void CreateSpawnerPanel()
        {
            var env = Object.FindFirstObjectByType<BrainEnvironment>();
            if (env == null)
            {
                EditorUtility.DisplayDialog("Cerveau VR", "Génère d'abord le cerveau.", "OK");
                return;
            }

            // Matériaux nécessaires en jeu (utile si le cerveau a été généré avant cette version)
            Undo.RecordObject(env, "Panneau neurone");
            env.neuronMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Neurone.mat");
            env.axonMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Axone.mat");
            env.myelinMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Myeline.mat");
            if (env.neuronsContainer == null) env.neuronsContainer = env.transform.Find("Neurones");

            var orbMat = GetOrCreate("Orbe", Shader.Find("Universal Render Pipeline/Unlit"));
            orbMat.SetColor("_BaseColor", new Color(0.9f, 1.4f, 1.8f));
            EditorUtility.SetDirty(orbMat);
            AssetDatabase.SaveAssets();

            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            var old = GameObject.Find(PanelName);
            if (old)
            {
                pos = old.transform.position;
                rot = old.transform.rotation;
                Undo.DestroyObjectImmediate(old);
            }
            else
            {
                pos = new Vector3(-1.2f, 1.35f, 0.9f);
                var viewer = new Vector3(0f, 1.35f, 0f);
                rot = Quaternion.LookRotation(pos - viewer); // le canvas se lit depuis son côté -Z
            }

            var go = new GameObject(PanelName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Panneau neurone");
            var rt = (RectTransform)go.transform;
            rt.SetPositionAndRotation(pos, rot);
            rt.sizeDelta = new Vector2(600f, 600f);
            rt.localScale = Vector3.one * 0.001f; // 60 cm x 60 cm

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var cyan = new Color(0f, 0.83f, 1f);

            // Fond sombre translucide
            var bg = AddUI<Image>("Fond", go.transform, Vector2.zero, new Vector2(600f, 600f));
            bg.color = new Color(0f, 0f, 0f, 0.38f);

            // Bordure cyan + halo lumineux
            AddFrame(go.transform, "Halo", 600f, 30f, new Color(cyan.r, cyan.g, cyan.b, 0.28f));
            AddFrame(go.transform, "Halo fin", 600f, 20f, new Color(cyan.r, cyan.g, cyan.b, 0.45f));
            AddFrame(go.transform, "Bordure", 600f, 11f, cyan);

            // Icône main
            var icon = AddUI<Image>("Icone main", go.transform, new Vector2(-195f, 197f), new Vector2(90f, 140f));
            icon.sprite = LoadSprite("Assets/Brain/Sprites/IconeMain.png");
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            // Titre
            var title = AddText("Titre", go.transform, new Vector2(60f, 180f), new Vector2(400f, 150f), "Créez un\nneurone", 62);
            title.fontStyle = FontStyles.Bold;
            title.lineSpacing = -8f;

            // Consignes
            var body = AddText("Consignes", go.transform, new Vector2(0f, -20f), new Vector2(570f, 170f),
                "Cliquez sur <b>Créer</b>\nGraber le neurone\nPlacez la où vous voulez", 40);
            body.lineSpacing = -6f;

            // Bouton
            var btnImg = AddUI<Image>("Bouton creer", go.transform, new Vector2(0f, -191f), new Vector2(372f, 108f));
            btnImg.sprite = LoadSprite("Assets/VRTemplateAssets/Sprites/UI/Round Radius 10.png");
            btnImg.type = Image.Type.Sliced;
            btnImg.pixelsPerUnitMultiplier = 6f;
            btnImg.color = Color.white;
            var btn = btnImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var colors = btn.colors;
            colors.normalColor = cyan;
            colors.highlightedColor = new Color(0.55f, 0.92f, 1f);
            colors.pressedColor = new Color(0f, 0.55f, 0.75f);
            colors.selectedColor = cyan;
            btn.colors = colors;
            AddText("Texte", btnImg.transform, Vector2.zero, new Vector2(372f, 108f), "Créer", 52).fontStyle = FontStyles.Bold;

            TMP_Text counter = null;

            var spawner = go.AddComponent<NeuronSpawnerPanel>();
            spawner.env = env;
            spawner.spawnButton = btn;
            spawner.counterText = counter;
            spawner.orbMaterial = orbMat;

            if (panelMode == PanelMode.InfobulleMainGauche)
            {
                if (!SetupAsLeftCallout(go, spawner))
                    EditorUtility.DisplayDialog("Cerveau VR", "« Affordance Callouts Left » introuvable dans la scène : le panneau reste fixe.", "OK");
            }
            else if (panelMode == PanelMode.Poignet)
            {
                var wm = go.AddComponent<WristMenu>();
                wm.hand = UnityEngine.XR.Hands.Handedness.Left;
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t.name == "Left Controller" && t.gameObject.scene == go.scene) { wm.controllerFallback = t; break; }
                // L'orbe apparaît juste au-dessus du panneau, un peu vers le joueur
                spawner.spawnOffset = new Vector3(0f, 0.16f, -0.06f);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
        }

        static void AddFrame(Transform parent, string name, float size, float thickness, Color c)
            => AddFrame(parent, name, size, size, thickness, c);

        static void AddFrame(Transform parent, string name, float w, float h2, float thickness, Color c)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(w, h2);
            float hw = w / 2f, hh = h2 / 2f, t = thickness / 2f;
            AddUI<Image>("Haut", root.transform, new Vector2(0f, hh - t), new Vector2(w, thickness)).color = c;
            AddUI<Image>("Bas", root.transform, new Vector2(0f, -hh + t), new Vector2(w, thickness)).color = c;
            AddUI<Image>("Gauche", root.transform, new Vector2(-hw + t, 0f), new Vector2(thickness, h2 - 2f * thickness)).color = c;
            AddUI<Image>("Droite", root.transform, new Vector2(hw - t, 0f), new Vector2(thickness, h2 - 2f * thickness)).color = c;
            foreach (var img in root.GetComponentsInChildren<Image>()) img.raycastTarget = false;
        }

        static Sprite LoadSprite(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.Sprite)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Branche le panneau sur les infobulles de la manette gauche (Affordance Callouts Left).</summary>
        static bool SetupAsLeftCallout(GameObject panel, NeuronSpawnerPanel spawner)
        {
            if (!SetupAsLeftCallout(panel, CalloutName, 0.00035f, 0.15f, 0.02f)) return false;
            spawner.spawnOffset = new Vector3(0f, 0.16f, -0.06f);
            return true;
        }

        static bool SetupAsLeftCallout(GameObject panel, string calloutName, float scale, float height, float side)
        {
            Transform callouts = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "Affordance Callouts Left" && t.gameObject.scene == panel.scene) { callouts = t; break; }
            if (callouts == null) return false;
            var gaze = callouts.GetComponent<CalloutGazeController>();
            if (gaze == null) return false;

            var oldCallout = callouts.Find(calloutName);
            if (oldCallout) Undo.DestroyObjectImmediate(oldCallout.gameObject);

            var calloutGo = new GameObject(calloutName);
            Undo.RegisterCreatedObjectUndo(calloutGo, "Callout");
            calloutGo.transform.SetParent(callouts, false);
            var callout = calloutGo.AddComponent<Callout>();
            var so = new SerializedObject(callout);
            so.FindProperty("m_LazyTooltip").objectReferenceValue = panel.transform;
            so.FindProperty("m_Curve").objectReferenceValue = null;
            so.FindProperty("m_DwellTime").floatValue = 0.5f;
            so.FindProperty("m_Unparent").boolValue = true;
            so.FindProperty("m_TurnOffAtStart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            Undo.RecordObject(gaze, "Callout");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var entered = (UnityEvent)typeof(CalloutGazeController).GetField("m_FacingEntered", flags).GetValue(gaze);
            var exited = (UnityEvent)typeof(CalloutGazeController).GetField("m_FacingExited", flags).GetValue(gaze);
            CleanMissing(entered, calloutName);
            CleanMissing(exited, calloutName);
            UnityEventTools.AddVoidPersistentListener(entered, callout.GazeHoverStart);
            UnityEventTools.AddVoidPersistentListener(exited, callout.GazeHoverEnd);
            EditorUtility.SetDirty(gaze);
            PrefabUtility.RecordPrefabInstancePropertyModifications(gaze);

            panel.transform.localScale = Vector3.one * scale;
            var follow = panel.AddComponent<CalloutPanelFollow>();
            follow.target = calloutGo.transform;
            follow.height = height;
            follow.side = side;
            panel.transform.position = callouts.position + Vector3.up * height;
            return true;
        }

        static void RemoveLeftCallout(string calloutName)
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var targets = new List<Transform>();
            foreach (var t in all) if (t && t.name == "Affordance Callouts Left") targets.Add(t);
            foreach (var t in targets)
            {
                var old = t.Find(calloutName);
                if (old) Undo.DestroyObjectImmediate(old.gameObject);
                var gaze = t.GetComponent<CalloutGazeController>();
                if (gaze == null) continue;
                Undo.RecordObject(gaze, "Callout");
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                CleanMissing((UnityEvent)typeof(CalloutGazeController).GetField("m_FacingEntered", flags).GetValue(gaze), calloutName);
                CleanMissing((UnityEvent)typeof(CalloutGazeController).GetField("m_FacingExited", flags).GetValue(gaze), calloutName);
                EditorUtility.SetDirty(gaze);
                PrefabUtility.RecordPrefabInstancePropertyModifications(gaze);
            }
        }

        static void CleanMissing(UnityEvent evt, string calloutName)
        {
            for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                var target = evt.GetPersistentTarget(i);
                if (target == null || (target is Component c && c.gameObject.name == calloutName))
                    UnityEventTools.RemovePersistentListener(evt, i);
            }
        }

        static T AddUI<T>(string name, Transform parent, Vector2 anchoredPos, Vector2 size) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return go.AddComponent<T>();
        }

        static TextMeshProUGUI AddText(string name, Transform parent, Vector2 pos, Vector2 size, string text, float fontSize)
        {
            var t = AddUI<TextMeshProUGUI>(name, parent, pos, size);
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        // =====================================================================
        // Panneau de textes (infobulle main gauche)
        const string TextPanelName = "Panneau - Textes";
        const string TextSequenceName = "Sequence de textes";
        const string TextCalloutName = "Callout - Textes";

        static void CreateTextPanel()
        {
            // On garde les textes déjà écrits si le panneau existe déjà
            string savedSequence = null;
            var oldSeqGo = GameObject.Find(TextSequenceName);
            if (oldSeqGo && oldSeqGo.GetComponent<TextSequence>())
                savedSequence = EditorJsonUtility.ToJson(oldSeqGo.GetComponent<TextSequence>());
            if (oldSeqGo) Undo.DestroyObjectImmediate(oldSeqGo);
            var oldPanel = GameObject.Find(TextPanelName);
            if (oldPanel) Undo.DestroyObjectImmediate(oldPanel);

            const float W = 1000f, H = 600f;
            var cyan = new Color(0f, 0.83f, 1f);

            var go = new GameObject(TextPanelName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Panneau textes");
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(W, H);
            rt.localScale = Vector3.one * 0.00045f; // 45 cm x 27 cm, à 1,3 m des yeux
            rt.position = new Vector3(0f, 1.9f, 1.2f);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

            AddUI<Image>("Fond", go.transform, Vector2.zero, new Vector2(W, H)).color = new Color(0f, 0f, 0f, 0.38f);
            AddFrame(go.transform, "Halo", W, H, 30f, new Color(cyan.r, cyan.g, cyan.b, 0.28f));
            AddFrame(go.transform, "Halo fin", W, H, 20f, new Color(cyan.r, cyan.g, cyan.b, 0.45f));
            AddFrame(go.transform, "Bordure", W, H, 11f, cyan);

            var text = AddText("Texte", go.transform, new Vector2(10f, 5f), new Vector2(860f, 490f), "", 40);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.lineSpacing = -2f;
            text.richText = true;

            // Rond « A »
            var ring = AddUI<Image>("Bouton A", go.transform, new Vector2(428f, -222f), new Vector2(82f, 82f));
            ring.sprite = LoadSprite("Assets/VRTemplateAssets/Sprites/UI/Circle_60x60 Outline 4.png");
            ring.color = cyan;
            ring.raycastTarget = false;
            AddText("A", ring.transform, new Vector2(0f, 2f), new Vector2(82f, 82f), "A", 48);

            var seqGo = new GameObject(TextSequenceName);
            Undo.RegisterCreatedObjectUndo(seqGo, "Sequence de textes");
            var seq = seqGo.AddComponent<TextSequence>();
            if (savedSequence != null) EditorJsonUtility.FromJsonOverwrite(savedSequence, seq);
            else seq.groups = DefaultGroups();
            seq.panel = go;
            seq.textField = text;

            // Panneau en haut du champ de vision (plus d'infobulle sur la manette)
            go.AddComponent<HeadUpPanel>();
            RemoveLeftCallout(TextCalloutName);

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = seqGo;
        }

        static List<TextGroup> DefaultGroups() => new List<TextGroup>
        {
            new TextGroup
            {
                title = "Bienvenue dans le cerveau !",
                text = "Le cerveau est un organe essentiel du système nerveux central, situé dans la boîte crânienne.\n\n" +
                       "Il est composé de milliards de neurones qui communiquent entre eux grâce à des signaux électriques et chimiques.",
                wordInterval = 0.12f, delayAfter = 5f
            },
            new TextGroup
            {
                text = "Divisé en plusieurs parties, dont les principales sont le cerveau, le cervelet et le tronc cérébral, " +
                       "il assure le contrôle des mouvements volontaires, la mémoire, le langage, les émotions, les sensations " +
                       "et les fonctions vitales comme la respiration et le rythme cardiaque.",
                wordInterval = 0.12f, delayAfter = 5f
            },
            new TextGroup
            {
                text = "Vous allez désormais consommer une certaine quantité de cannabis . Celui-ci contient une molécule " +
                       "psychoactive appelée **THC**, ou **tétrahydrocannabinol**. Vous allez absorber **49 mg** de cette " +
                       "substance l’équivalent d’environ un joint. Puis vous passerez à **490 mg**, l’équivalent de 10 joints.",
                wordInterval = 0.12f, delayAfter = 6f
            },
        };

        static void CreateDust(Transform root, Material mat)
        {
            var go = new GameObject("Neurotransmetteurs (particules)");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 4f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.8f), new Color(0.45f, 0.85f, 1f));
            main.maxParticles = 500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = 40f;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 10f;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.3f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        static void CreateLights(Transform root)
        {
            AddLight(root, "Lumiere rose", new Vector3(5f, 4f, 3f), new Color(1f, 0.35f, 0.6f), 45f, 18f);
            AddLight(root, "Lumiere violette", new Vector3(-6f, 5f, -2f), new Color(0.6f, 0.3f, 1f), 45f, 18f);
            AddLight(root, "Lumiere cyan", new Vector3(0f, 6f, -7f), new Color(0.3f, 0.8f, 1f), 35f, 16f);
            AddLight(root, "Lumiere centrale", new Vector3(0f, 3f, 0f), new Color(1f, 0.6f, 0.8f), 8f, 9f);
        }

        static void AddLight(Transform root, string name, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;
        }

        static void TuneDirectionalLights(GameObject brainRoot)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional || l.transform.IsChildOf(brainRoot.transform)) continue;
                Undo.RecordObject(l, "Lumière cerveau");
                l.color = new Color(1f, 0.55f, 0.75f);
                l.intensity = 0.25f;
            }
        }

        static void SetTemplateEnvironmentVisible(bool visible)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name != "Environment") continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    Undo.RecordObject(r, "Décor du template");
                    r.enabled = visible;
                }
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        static MeshRenderer CreateRenderer(string name, Transform parent, Vector3 localPos, Quaternion rot, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mr;
        }

        // =====================================================================
        // Matériaux
        static Mats CreateMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particle = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            var m = new Mats
            {
                tissue = LitMat("Tissu_cerebral", lit, new Color(0.62f, 0.20f, 0.32f), 0.5f, new Color(0.10f, 0.015f, 0.06f)),
                neuron = LitMat("Neurone", lit, new Color(0.65f, 0.45f, 0.95f), 0.6f, new Color(0.18f, 0.04f, 0.25f)),
                axon = LitMat("Axone", lit, new Color(0.45f, 0.30f, 0.75f), 0.5f, new Color(0.12f, 0.05f, 0.22f)),
                myelin = LitMat("Myeline", lit, new Color(0.9f, 0.82f, 0.95f), 0.7f, new Color(0.05f, 0.04f, 0.06f)),
                pulse = GetOrCreate("Impulsion", unlit),
                trail = Additive("Trainee_impulsion", particle, null, Color.white),
                dust = Additive("Poussiere_neuronale", particle, SoftDotTexture(), new Color(1f, 1f, 1f, 0.8f)),
            };
            m.pulse.SetColor("_BaseColor", new Color(0.8f, 1.5f, 2f));
            EditorUtility.SetDirty(m.pulse);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material GetOrCreate(string name, Shader s)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(s);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = s;
            return m;
        }

        static Material LitMat(string name, Shader s, Color baseCol, float smooth, Color emission)
        {
            var m = GetOrCreate(name, s);
            m.SetColor("_BaseColor", baseCol);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Additive(string name, Shader s, Texture tex, Color col)
        {
            var m = GetOrCreate(name, s);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f); // Additif
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", col);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D SoftDotTexture()
        {
            string path = GenDir + "/PointDoux.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            tex = new Texture2D(64, 64, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "PointDoux" };
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x + 0.5f) / 32f - 1f, dy = (y + 0.5f) / 32f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            tex.Apply();
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
