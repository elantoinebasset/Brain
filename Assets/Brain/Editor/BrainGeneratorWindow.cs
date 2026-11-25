using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

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
            if (GUILayout.Button("Ajouter / recréer le panneau « Créer un neurone »", GUILayout.Height(26))) CreateSpawnerPanel();
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

            // --- Axones (connexions) ---
            var axons = new BrainMeshBuilder(2);
            foreach (var pair in BuildPairs(positions))
            {
                var pts = AxonPath(positions[pair.x], positions[pair.y]);
                int idx = env.connections.Count;
                env.connections.Add(new BrainEnvironment.Connection { a = pair.x, b = pair.y, points = pts });
                env.neurons[pair.x].connections.Add(idx);
                env.neurons[pair.y].connections.Add(idx);
                NeuronMeshFactory.BuildAxon(axons, pts, somaRadii[pair.y]);
            }
            var axonMesh = axons.ToMesh("Axones");
            meshes.Add(axonMesh);
            CreateRenderer("Axones", root.transform, Vector3.zero, Quaternion.identity, axonMesh, new[] { mats.axon, mats.myelin });

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
            rt.sizeDelta = new Vector2(420f, 270f);
            rt.localScale = Vector3.one * 0.0015f;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var bg = AddUI<Image>("Fond", go.transform, Vector2.zero, new Vector2(420f, 270f));
            bg.color = new Color(0.10f, 0.03f, 0.12f, 0.88f);

            var title = AddText("Titre", go.transform, new Vector2(0f, 95f), new Vector2(400f, 50f), "Laboratoire neuronal", 34);
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(1f, 0.75f, 0.95f);
            AddText("Aide", go.transform, new Vector2(0f, 45f), new Vector2(390f, 60f),
                "Crée une orbe, attrape-la et relâche-la où tu veux : elle deviendra un neurone.", 19);

            var btnImg = AddUI<Image>("Bouton creer", go.transform, new Vector2(0f, -30f), new Vector2(300f, 70f));
            btnImg.color = new Color(0.55f, 0.25f, 0.9f);
            var btn = btnImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.85f, 0.75f, 1f);
            colors.pressedColor = new Color(0.6f, 0.6f, 0.7f);
            btn.colors = colors;
            AddText("Texte", btnImg.transform, Vector2.zero, new Vector2(300f, 70f), "Créer un neurone", 28).fontStyle = FontStyles.Bold;

            var counter = AddText("Compteur", go.transform, new Vector2(0f, -102f), new Vector2(400f, 34f), "Neurones créés : 0", 18);
            counter.color = new Color(0.75f, 0.9f, 1f);

            var spawner = go.AddComponent<NeuronSpawnerPanel>();
            spawner.env = env;
            spawner.spawnButton = btn;
            spawner.counterText = counter;
            spawner.orbMaterial = orbMat;

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
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
