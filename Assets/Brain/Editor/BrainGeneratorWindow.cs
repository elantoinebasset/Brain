using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
            if (GUILayout.Button("Ajouter / recréer le panneau « Réinitialiser »", GUILayout.Height(26))) CreateResetPanel();
            if (GUILayout.Button("Ajouter / recréer le bloc « Consommation » + jauges", GUILayout.Height(26))) CreateCannabisSetup();
            if (GUILayout.Button("Ajouter / recréer le scénario THC (molécules + texte + alerte)", GUILayout.Height(26))) CreateThcScenario();
            if (GUILayout.Button("Ajouter les hallucinations (modèles 3D à remplir)", GUILayout.Height(26))) CreateHallucinations();
            if (GUILayout.Button("Ajouter les poignées à tous les panneaux", GUILayout.Height(26))) AddGrabHandlesToAllPanels();
            if (GUILayout.Button("Activer la rotation fluide de la caméra", GUILayout.Height(26))) AddSmoothTurn();
            if (GUILayout.Button("Ajouter / mettre à jour l'ambiance cinématique", GUILayout.Height(26))) CreateCinematic();
            EditorGUILayout.Space();
            if (GUILayout.Button("Réafficher le décor du template")) SetTemplateEnvironmentVisible(true);
            if (GUILayout.Button("Masquer le décor du template (et ses murs invisibles)")) SetTemplateEnvironmentVisible(false);
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
        // Consommation : état THC + bloc cliquable + jauges (dose / timeline) en haut à gauche
        const string StateName = "Etat THC";
        const string BlockRootName = "Consommation";
        const string HudName = "HUD - Consommation";

        static GameObject FindRoot(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.parent == null && t.name == name) return t.gameObject;
            return null;
        }

        static void CreateCannabisSetup()
        {
            // --- État ---
            var state = Object.FindFirstObjectByType<CannabisState>(FindObjectsInactive.Include);
            if (state == null)
            {
                var sgo = new GameObject(StateName);
                Undo.RegisterCreatedObjectUndo(sgo, "Etat THC");
                state = sgo.AddComponent<CannabisState>();
            }

            // --- Bloc ---
            Vector3 blockPos = new Vector3(0.45f, 1.05f, 0.65f);
            var oldBlock = FindRoot(BlockRootName);
            if (oldBlock) { blockPos = oldBlock.transform.position; Undo.DestroyObjectImmediate(oldBlock); }

            var root = new GameObject(BlockRootName);
            Undo.RegisterCreatedObjectUndo(root, "Bloc consommation");
            root.transform.position = blockPos;

            var mat = LitMat("Bloc_conso", Shader.Find("Universal Render Pipeline/Lit"),
                             new Color(0.35f, 0.85f, 0.35f), 0.55f, new Color(0.05f, 0.3f, 0.05f));
            AssetDatabase.SaveAssets();

            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Bloc";
            block.transform.SetParent(root.transform, false);
            block.transform.localScale = Vector3.one * 0.14f;
            block.GetComponent<Renderer>().sharedMaterial = mat;
            block.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            var cb = block.AddComponent<ConsumeBlock>();
            cb.state = state;

            var labelGo = new GameObject("Etiquette");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            var viewer = new Vector3(0f, blockPos.y, 0f);
            labelGo.transform.rotation = Quaternion.LookRotation(labelGo.transform.position - viewer);
            var label = labelGo.AddComponent<TextMeshPro>();
            label.text = "Consommer";
            label.fontSize = 0.6f;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(0.6f, 0.2f);
            label.fontStyle = FontStyles.Bold;
            cb.label = label;

            // --- HUD ---
            var oldHud = FindRoot(HudName);
            if (oldHud) Undo.DestroyObjectImmediate(oldHud);

            const float W = 560f, H = 150f;
            var cyan = new Color(0f, 0.83f, 1f);
            var hud = new GameObject(HudName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(hud, "HUD consommation");
            var rt = (RectTransform)hud.transform;
            rt.sizeDelta = new Vector2(W, H);
            rt.localScale = Vector3.one * 0.0005f; // 28 cm x 7,5 cm
            rt.position = new Vector3(-0.5f, 1.8f, 1.1f);
            var canvas = hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            hud.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

            AddUI<Image>("Fond", hud.transform, Vector2.zero, new Vector2(W, H)).color = new Color(0f, 0f, 0f, 0.45f);
            AddFrame(hud.transform, "Bordure", W, H, 6f, new Color(cyan.r, cyan.g, cyan.b, 0.8f));

            var doseLabel = AddText("Dose titre", hud.transform, new Vector2(-100f, 30f), new Vector2(320f, 44f), "Consommation", 32);
            doseLabel.alignment = TextAlignmentOptions.Left;
            doseLabel.textWrappingMode = TextWrappingModes.NoWrap;
            doseLabel.fontStyle = FontStyles.Bold;
            var doseValue = AddText("Dose valeur", hud.transform, new Vector2(150f, 30f), new Vector2(200f, 44f), "0 / 490 mg", 30);
            doseValue.alignment = TextAlignmentOptions.Right;
            doseValue.textWrappingMode = TextWrappingModes.NoWrap;
            var doseBar = Bar(hud.transform, "Dose barre", new Vector2(0f, -25f), new Color(0.45f, 0.9f, 0.45f), out var doseFillImg);
            for (int i = 1; i < 10; i++)
            {
                var tick = AddUI<Image>("Graduation " + i, doseBar, Vector2.zero, new Vector2(2f, 0f));
                var trt = tick.rectTransform;
                trt.anchorMin = new Vector2(i / 10f, 0f);
                trt.anchorMax = new Vector2(i / 10f, 1f);
                trt.sizeDelta = new Vector2(2f, 0f);
                tick.color = new Color(0f, 0f, 0f, 0.5f);
                tick.raycastTarget = false;
            }

            var hudComp = hud.AddComponent<CannabisHud>();
            hudComp.state = state;
            hudComp.doseFill = doseFillImg.rectTransform;
            hudComp.doseFillImage = doseFillImg;
            hudComp.doseValue = doseValue;

            var head = hud.AddComponent<HeadUpPanel>();
            head.distance = 1.2f;
            head.angleAboveCenter = 12f;
            head.angleLeftOfCenter = 30f;

            AddGrabHandle(hud);
            EditorSceneManager.MarkSceneDirty(hud.scene);
            Selection.activeGameObject = root;
        }

        static RectTransform Bar(Transform parent, string name, Vector2 pos, Color fillColor, out Image fill)
        {
            var bg = AddUI<Image>(name, parent, pos, new Vector2(500f, 26f));
            bg.color = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = false;
            fill = AddUI<Image>("Remplissage", bg.transform, Vector2.zero, Vector2.zero);
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            fill.color = fillColor;
            fill.raycastTarget = false;
            return bg.rectTransform;
        }

        // Scénario THC : molécules qui se fixent sur les neurones + séquence de texte à la 1re prise
        const string ScenarioName = "Scenario THC";

        static void CreateThcScenario()
        {
            var env = Object.FindFirstObjectByType<BrainEnvironment>();
            if (env == null)
            {
                EditorUtility.DisplayDialog("Cerveau VR", "Génère d'abord le cerveau.", "OK");
                return;
            }
            if (Object.FindFirstObjectByType<CannabisState>(FindObjectsInactive.Include) == null)
                CreateCannabisSetup();

            string saved = null;
            var old = FindRoot(ScenarioName);
            if (old && old.GetComponent<ThcScenario>()) saved = EditorJsonUtility.ToJson(old.GetComponent<ThcScenario>());
            if (old) Undo.DestroyObjectImmediate(old);

            var mat = GetOrCreate("Molecule_THC", Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", new Color(1.3f, 0.35f, 2.4f));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            var go = new GameObject(ScenarioName);
            Undo.RegisterCreatedObjectUndo(go, "Scénario THC");
            var sc = go.AddComponent<ThcScenario>();
            if (saved != null) EditorJsonUtility.FromJsonOverwrite(saved, sc);
            sc.env = env;
            sc.state = Object.FindFirstObjectByType<CannabisState>(FindObjectsInactive.Include);
            sc.sequence = Object.FindFirstObjectByType<TextSequence>(FindObjectsInactive.Include);
            sc.moleculeMaterial = mat;
            sc.trailMaterial = env.trailMaterial;

            // Neurones bloqués : éteints (gris, sans lumière) + halo violet pulsant
            var particle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var halo = Additive("Halo_THC", particle, SoftDotTexture(), new Color(0.8f, 0.25f, 1f, 0.9f));
            if (halo.HasProperty("_Cull")) halo.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(halo);
            Undo.RecordObject(env, "Scénario THC");
            env.blockedHaloMaterial = halo;
            env.blockedEmission = new Color(0.01f, 0.01f, 0.015f);
            env.blockedBaseColor = new Color(0.22f, 0.22f, 0.27f);
            env.blockedHaloColor = new Color(0.8f, 0.25f, 1f, 0.9f);
            EditorUtility.SetDirty(env);
            AssetDatabase.SaveAssets();

            sc.alert = CreateAlert(env, sc);

            // Éclairs (séquence 490 mg)
            var storm = go.AddComponent<LightningStorm>();
            storm.env = env;
            storm.boltMaterial = env.trailMaterial;
            // Style de l'asset « Zap VFX - URP » s'il est dans le projet
            const string zapDir = "Assets/Vefects/Zap VFX URP";
            storm.assetBoltMaterial = AssetDatabase.LoadAssetAtPath<Material>(zapDir + "/VFX/Zap/Materials/M_VFX_Zap_Lightning_05_LUT_Add.mat");
            storm.impactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(zapDir + "/VFX/Zap/Particles/VFX_Zap_05_Purple.prefab");
            // Couleurs alternées : violet, rouge, bleu, jaune-orangé (même ordre pour éclairs et impacts)
            var colorIds = new[] { ("05", "05_Purple"), ("01", "01_Red"), ("02", "02_Blue"), ("03", "03_Yellow") };
            var boltMats = new List<Material>();
            var impacts = new List<GameObject>();
            foreach (var (zapMatId, zapFxId) in colorIds)
            {
                var zapMat = AssetDatabase.LoadAssetAtPath<Material>(zapDir + "/VFX/Zap/Materials/M_VFX_Zap_Lightning_" + zapMatId + "_LUT_Add.mat");
                var zapFx = AssetDatabase.LoadAssetAtPath<GameObject>(zapDir + "/VFX/Zap/Particles/VFX_Zap_" + zapFxId + ".prefab");
                if (zapMat == null || zapFx == null) continue;
                boltMats.Add(zapMat);
                impacts.Add(zapFx);
            }
            storm.assetBoltMaterials = boltMats.ToArray();
            storm.impactPrefabs = impacts.ToArray();
            var zaps = new List<AudioClip>();
            foreach (var n in new[] { "SFX_Vefects_Zap_Small_01", "SFX_Vefects_Zap_Medium_01", "SFX_Vefects_Zap_Medium_02" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(zapDir + "/Audio/WAV/" + n + ".wav");
                if (clip != null) zaps.Add(clip);
            }
            storm.zapSounds = zaps.ToArray();
            if (storm.assetBoltMaterial != null) storm.boltColor = new Color(1f, 1f, 1f, 1f);
            sc.storm = storm;

            // Lumière au-dessus du joueur (éteinte jusqu'à la fin de la simulation)
            var oldLight = FindRoot(PlayerLightName);
            if (oldLight) Undo.DestroyObjectImmediate(oldLight);
            var lgo = new GameObject(PlayerLightName);
            Undo.RegisterCreatedObjectUndo(lgo, "Lumière joueur");
            lgo.transform.position = new Vector3(0f, 2.4f, 0f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 4f;
            l.intensity = 2.5f;
            l.color = new Color(1f, 0.95f, 0.9f);
            l.shadows = LightShadows.None;
            sc.playerLight = lgo.AddComponent<PlayerHaloLight>();
            sc.hallucinations = Object.FindFirstObjectByType<HallucinationSpawner>(FindObjectsInactive.Include);

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
        }

        // Hallucinations : objet créé une seule fois (pour ne pas perdre les modèles déjà glissés dedans)
        const string HallucinationsName = "Hallucinations";

        static void CreateHallucinations()
        {
            var existing = Object.FindFirstObjectByType<HallucinationSpawner>(FindObjectsInactive.Include);
            if (existing == null)
            {
                var go = new GameObject(HallucinationsName);
                Undo.RegisterCreatedObjectUndo(go, "Hallucinations");
                existing = go.AddComponent<HallucinationSpawner>();
            }
            existing.state = Object.FindFirstObjectByType<CannabisState>(FindObjectsInactive.Include);
            var sc = Object.FindFirstObjectByType<ThcScenario>(FindObjectsInactive.Include);
            if (sc != null)
            {
                Undo.RecordObject(sc, "Hallucinations");
                sc.hallucinations = existing;
                EditorUtility.SetDirty(sc);
            }
            EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
            Selection.activeGameObject = existing.gameObject;
        }

        // Effet ALERTE : picto, flash rouge léger, secousse légère
        const string AlertName = "Alerte THC";
        const string PlayerLightName = "Lumiere joueur";

        static ThcAlert CreateAlert(BrainEnvironment env, ThcScenario sc)
        {
            var old = FindRoot(AlertName);
            AudioClip keptSound = old && old.GetComponent<ThcAlert>() ? old.GetComponent<ThcAlert>().alertSound : null;
            if (old) Undo.DestroyObjectImmediate(old);

            var flash = GetOrCreate("Flash_alerte", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            flash.SetFloat("_Surface", 1f);
            flash.SetFloat("_Blend", 0f); // Alpha
            flash.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            flash.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            flash.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            flash.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            flash.SetFloat("_ZWrite", 0f);
            if (flash.HasProperty("_Cull")) flash.SetFloat("_Cull", 0f);
            flash.SetOverrideTag("RenderType", "Transparent");
            flash.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            flash.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            flash.DisableKeyword("_ALPHAMODULATE_ON");
            flash.SetTexture("_BaseMap", null);
            flash.renderQueue = 4000;
            flash.SetColor("_BaseColor", new Color(1f, 0.05f, 0.05f, 0f));
            EditorUtility.SetDirty(flash);
            AssetDatabase.SaveAssets();

            var go = new GameObject(AlertName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Alerte THC");
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(400f, 420f);
            rt.localScale = Vector3.one * 0.0006f; // 24 cm
            rt.position = new Vector3(0f, 1.7f, 1.1f);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var tri = AddUI<Image>("Triangle", go.transform, new Vector2(0f, 45f), new Vector2(320f, 290f));
            tri.sprite = AlertSprite();
            tri.raycastTarget = false;
            var mark = AddText("Point d'exclamation", tri.transform, new Vector2(0f, -30f), new Vector2(200f, 220f), "!", 170);
            mark.fontStyle = FontStyles.Bold;
            var label = AddText("Texte", go.transform, new Vector2(0f, -175f), new Vector2(1100f, 170f), "ALERTE", 60);
            label.enableAutoSizing = true;
            label.fontSizeMin = 26f;
            label.fontSizeMax = 60f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(1f, 0.35f, 0.3f);

            var head = go.AddComponent<HeadUpPanel>();
            head.distance = 1.1f;
            head.angleAboveCenter = 4f;
            head.angleLeftOfCenter = 0f;
            head.followSpeed = 8f;

            var alert = go.AddComponent<ThcAlert>();
            alert.scenario = sc;
            alert.env = env;
            alert.picto = group;
            alert.flashMaterial = flash;
            alert.label = label;
            alert.alertSound = keptSound;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return alert;
        }

        /// <summary>Triangle d'alerte rouge, généré une fois dans Assets/Brain/Sprites/Alerte.png.</summary>
        static Sprite AlertSprite()
        {
            const string path = "Assets/Brain/Sprites/Alerte.png";
            if (!System.IO.File.Exists(path))
            {
                const int N = 256;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var a = new Vector2(0f, 0.86f);
                var b = new Vector2(-0.96f, -0.8f);
                var c = new Vector2(0.96f, -0.8f);
                var fill = new Color(0.95f, 0.18f, 0.15f);
                var border = new Color(1f, 0.85f, 0.8f);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        var p = new Vector2((x + 0.5f) / N * 2f - 1f, (y + 0.5f) / N * 2f - 1f);
                        float d = Mathf.Min(EdgeDist(p, a, b), Mathf.Min(EdgeDist(p, b, c), EdgeDist(p, c, a)));
                        float alpha = Mathf.Clamp01(d / 0.015f);
                        var col = d < 0.05f ? border : fill;
                        col.a = alpha;
                        tex.SetPixel(x, y, col);
                    }
                tex.Apply();
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }
            return LoadSprite(path);
        }

        // Distance signée à une arête (positive à l'intérieur pour un triangle a,b,c dans le sens anti-horaire)
        static float EdgeDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 e = b - a;
            Vector2 n = new Vector2(-e.y, e.x).normalized;
            return Vector2.Dot(p - a, n);
        }

        // Ambiance cinématique : étalonnage + bloom (Volume), respiration du cerveau, ouverture au noir
        const string CinematicVolumeName = "Ambiance cinematique";
        const string BreathingName = "Respiration du cerveau";
        const string FaderName = "Fondu ecran";

        static void CreateCinematic()
        {
            var env = Object.FindFirstObjectByType<BrainEnvironment>();
            if (env == null)
            {
                EditorUtility.DisplayDialog("Cerveau VR", "Génère d'abord le cerveau.", "OK");
                return;
            }

            // --- 1. Profil de post-process ---
            EnsureFolder(RootDir, "Settings");
            string path = RootDir + "/Settings/Ambiance_cinematique.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.6f);
            bloom.scatter.Override(0.6f);

            // Neutralise le volume du template (ACES + exposition + saturation) qui assombrit tout sans HDR
            var tone = GetOrAdd<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.None);

            var ca = GetOrAdd<ColorAdjustments>(profile);
            ca.postExposure.Override(0f);
            ca.contrast.Override(12f);
            ca.saturation.Override(8f);

            var split = GetOrAdd<SplitToning>(profile);
            split.shadows.Override(new Color(0.46f, 0.48f, 0.56f));   // ombres un peu froides
            split.highlights.Override(new Color(0.56f, 0.5f, 0.47f));  // lumières un peu chaudes
            split.balance.Override(0f);

            var vig = GetOrAdd<Vignette>(profile);
            vig.intensity.Override(0.15f);
            vig.smoothness.Override(0.45f);
            vig.color.Override(Color.black);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var volGo = FindRoot(CinematicVolumeName);
            if (volGo == null)
            {
                volGo = new GameObject(CinematicVolumeName);
                Undo.RegisterCreatedObjectUndo(volGo, "Ambiance cinématique");
            }
            var vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;

            // Post-process activé sur la caméra du XR Origin (désactivé dans le template)
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!cam.CompareTag("MainCamera")) continue;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) continue;
                Undo.RecordObject(data, "Post-process caméra");
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            }

            // --- 2. Respiration du cerveau ---
            var breathing = Object.FindFirstObjectByType<BrainBreathing>(FindObjectsInactive.Include);
            if (breathing == null)
            {
                var bgo = new GameObject(BreathingName);
                Undo.RegisterCreatedObjectUndo(bgo, "Respiration");
                breathing = bgo.AddComponent<BrainBreathing>();
            }
            breathing.env = env;
            breathing.state = Object.FindFirstObjectByType<CannabisState>(FindObjectsInactive.Include);
            breathing.wall = env.transform.Find("Paroi cerebrale");

            // --- 3. Ouverture au noir ---
            var fader = Object.FindFirstObjectByType<ScreenFader>(FindObjectsInactive.Include);
            if (fader == null)
            {
                var fgo = new GameObject(FaderName);
                Undo.RegisterCreatedObjectUndo(fgo, "Fondu");
                fader = fgo.AddComponent<ScreenFader>();
            }
            fader.fadeMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Flash_alerte.mat");

            var sc = Object.FindFirstObjectByType<ThcScenario>(FindObjectsInactive.Include);
            if (sc != null)
            {
                Undo.RecordObject(sc, "Ambiance");
                sc.breathing = breathing;
                sc.fader = fader;
                EditorUtility.SetDirty(sc);
            }

            EditorSceneManager.MarkSceneDirty(volGo.scene);
            Selection.activeGameObject = volGo;
            if (fader.fadeMaterial == null)
                Debug.LogWarning("[Cerveau VR] Matériau Flash_alerte introuvable : recrée d'abord le scénario THC (alerte) pour avoir le fondu.");
        }

        static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var c)) { c.active = true; return c; }
            c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        static void AddSmoothTurn()
        {
            var existing = Object.FindFirstObjectByType<SmoothTurnSettings>(FindObjectsInactive.Include);
            if (existing == null)
            {
                var go = new GameObject("Rotation fluide");
                Undo.RegisterCreatedObjectUndo(go, "Rotation fluide");
                existing = go.AddComponent<SmoothTurnSettings>();
            }
            EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
            Selection.activeGameObject = existing.gameObject;
        }

        // Poignées pour déplacer les panneaux en VR
        const string HandleName = "Poignee";

        static void AddGrabHandlesToAllPanels()
        {
            int n = 0;
            foreach (var t in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.parent != null) continue;
                if (!(t.name.StartsWith("Panneau - ") || t.name.StartsWith("HUD - "))) continue;
                var c = t.GetComponent<Canvas>();
                if (c == null || c.renderMode != RenderMode.WorldSpace) continue;
                AddGrabHandle(t.gameObject);
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
                n++;
            }
            Debug.Log($"[Cerveau VR] Poignées ajoutées sur {n} panneau(x). Pense à sauvegarder la scène (Ctrl+S).");
        }

        static void AddGrabHandle(GameObject panel)
        {
            var rt = panel.transform as RectTransform;
            if (rt == null) return;
            var old = panel.transform.Find(HandleName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);

            float w = rt.sizeDelta.x, h = rt.sizeDelta.y;
            float hw = Mathf.Clamp(w * 0.4f, 160f, 320f);
            float hh = Mathf.Clamp(h * 0.06f, 18f, 34f);
            var img = AddUI<Image>(HandleName, panel.transform, new Vector2(0f, -h / 2f - hh / 2f - 22f), new Vector2(hw, hh));
            Undo.RegisterCreatedObjectUndo(img.gameObject, "Poignée");
            img.sprite = LoadSprite("Assets/VRTemplateAssets/Sprites/UI/Round Radius 10.png");
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 3f;
            img.raycastTarget = false;

            var col = img.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(hw + 60f, hh + 70f, 120f);
            img.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            var grab = img.gameObject.AddComponent<PanelGrabHandle>();
            grab.panel = panel.transform;
            grab.visual = img;
            img.color = grab.normalColor;
        }

        // Panneau "Réinitialiser le cerveau" (manette droite)
        const string ResetPanelName = "Panneau - Reinitialiser";
        const string ResetCalloutName = "Callout - Reinitialiser";

        static void CreateResetPanel()
        {
            Vector3 pos;
            Quaternion rot;
            GameObject old = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == ResetPanelName && t.parent == null) { old = t.gameObject; break; }
            if (old)
            {
                pos = old.transform.position;
                rot = old.transform.rotation;
                Undo.DestroyObjectImmediate(old);
            }
            else
            {
                pos = new Vector3(1.2f, 1.35f, 0.9f);
                rot = Quaternion.LookRotation(pos - new Vector3(0f, 1.35f, 0f));
            }

            var go = new GameObject(ResetPanelName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Panneau réinitialiser");
            var rt = (RectTransform)go.transform;
            rt.SetPositionAndRotation(pos, rot);
            rt.sizeDelta = new Vector2(600f, 600f);
            rt.localScale = Vector3.one * 0.001f;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var accent = new Color(1f, 0.45f, 0.45f);

            var bg = AddUI<Image>("Fond", go.transform, Vector2.zero, new Vector2(600f, 600f));
            bg.color = new Color(0f, 0f, 0f, 0.38f);
            AddFrame(go.transform, "Halo", 600f, 30f, new Color(accent.r, accent.g, accent.b, 0.28f));
            AddFrame(go.transform, "Halo fin", 600f, 20f, new Color(accent.r, accent.g, accent.b, 0.45f));
            AddFrame(go.transform, "Bordure", 600f, 11f, accent);

            var title = AddText("Titre", go.transform, new Vector2(0f, 180f), new Vector2(560f, 150f), "Réinitialiser\nle cerveau", 60);
            title.fontStyle = FontStyles.Bold;
            title.lineSpacing = -8f;

            var body = AddText("Consignes", go.transform, new Vector2(0f, -20f), new Vector2(570f, 170f),
                "Le cerveau revient à\nson état normal de départ", 40);
            body.lineSpacing = -6f;

            var btnImg = AddUI<Image>("Bouton reinitialiser", go.transform, new Vector2(0f, -191f), new Vector2(420f, 108f));
            btnImg.sprite = LoadSprite("Assets/VRTemplateAssets/Sprites/UI/Round Radius 10.png");
            btnImg.type = Image.Type.Sliced;
            btnImg.pixelsPerUnitMultiplier = 6f;
            btnImg.color = Color.white;
            var btn = btnImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var colors = btn.colors;
            colors.normalColor = accent;
            colors.highlightedColor = new Color(1f, 0.65f, 0.65f);
            colors.pressedColor = new Color(0.75f, 0.25f, 0.25f);
            colors.selectedColor = accent;
            btn.colors = colors;
            var label = AddText("Texte", btnImg.transform, Vector2.zero, new Vector2(420f, 108f), "Réinitialiser", 48);
            label.fontStyle = FontStyles.Bold;

            var reset = go.AddComponent<BrainResetPanel>();
            reset.resetButton = btn;
            reset.buttonLabel = label;

            if (panelMode == PanelMode.InfobulleMainGauche)
            {
                if (!SetupAsCallout(go, "Affordance Callouts Right", ResetCalloutName, 0.00035f, 0.15f, -0.02f))
                    EditorUtility.DisplayDialog("Cerveau VR", "« Affordance Callouts Right » introuvable dans la scène : le panneau reste fixe.", "OK");
            }
            else if (panelMode == PanelMode.Poignet)
            {
                var wm = go.AddComponent<WristMenu>();
                wm.hand = UnityEngine.XR.Hands.Handedness.Right;
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (t.name == "Right Controller" && t.gameObject.scene == go.scene) { wm.controllerFallback = t; break; }
            }

            AddGrabHandle(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
        }

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

            AddGrabHandle(go);
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
            => SetupAsCallout(panel, "Affordance Callouts Left", calloutName, scale, height, side);

        static bool SetupAsCallout(GameObject panel, string calloutsName, string calloutName, float scale, float height, float side)
        {
            Transform callouts = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == calloutsName && t.gameObject.scene == panel.scene) { callouts = t; break; }
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

            AddGrabHandle(go);
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
                // Les collisions des murs / meubles du template restaient actives = murs invisibles.
                // On garde seulement les collisions plates (sol), utiles pour la téléportation.
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (c.bounds.size.y < 0.3f) continue;
                    Undo.RecordObject(c, "Décor du template");
                    c.enabled = visible;
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
