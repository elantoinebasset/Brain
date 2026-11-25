using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace BrainVR
{
    /// <summary>
    /// Anime l'intérieur du cerveau : impulsions nerveuses le long des axones,
    /// flash des neurones quand ils s'activent, et ambiance (brouillard, lumière ambiante).
    /// Les données (neurones, connexions) sont remplies par Tools > Cerveau VR.
    /// </summary>
    public class BrainEnvironment : MonoBehaviour
    {
        [Serializable]
        public class Neuron
        {
            public Renderer renderer;
            public Vector3 position;
            public List<int> connections = new List<int>();
        }

        [Serializable]
        public class Connection
        {
            public int a;
            public int b;
            public Vector3[] points;
        }

        [Header("Données générées (Tools > Cerveau VR)")]
        public List<Neuron> neurons = new List<Neuron>();
        public List<Connection> connections = new List<Connection>();

        [Header("Ambiance")]
        public bool applyAtmosphere = true;
        public Color fogColor = new Color(0.16f, 0.04f, 0.11f);
        public float fogDensity = 0.04f;
        public Color ambientColor = new Color(0.45f, 0.18f, 0.38f);
        [Range(0f, 1f)] public float reflectionIntensity = 0.15f;

        [Header("Impulsions nerveuses")]
        public Mesh pulseMesh;
        public Material pulseMaterial;
        public Material trailMaterial;
        public Color trailColor = new Color(0.45f, 0.85f, 1f, 1f);
        public int maxPulses = 48;
        public float pulseSpeed = 2.5f;
        public float pulseSize = 0.07f;
        public float trailTime = 0.35f;

        [Header("Activité neuronale")]
        public float spontaneousInterval = 0.8f;
        [Range(0f, 1f)] public float propagationChance = 0.6f;
        public float refractoryTime = 1.5f;
        [ColorUsage(false, true)] public Color neuronBaseEmission = new Color(0.18f, 0.04f, 0.25f);
        [ColorUsage(false, true)] public Color neuronFlashEmission = new Color(1.6f, 0.6f, 2.6f);
        public float flashDecay = 2f;

        [Header("Neurones créés en jeu")]
        public Transform neuronsContainer;
        public Material neuronMaterial;
        public Material axonMaterial;
        public Material myelinMaterial;
        [Tooltip("Distance max pour qu'un nouveau neurone se connecte à ses voisins")]
        public float autoConnectDistance = 4.5f;
        public int autoConnectMax = 3;
        [Tooltip("Vitesse de pousse d'une nouvelle connexion (m/s)")]
        public float growSpeed = 0.7f;

        class Pulse
        {
            public GameObject go;
            public TrailRenderer trail;
            public int conn;
            public bool reverse;
            public float dist;
        }

        readonly List<Pulse> pool = new List<Pulse>();
        readonly List<Pulse> active = new List<Pulse>();
        readonly List<float[]> cumulative = new List<float[]>();
        readonly List<float> lengths = new List<float>();
        readonly List<float> flash = new List<float>();
        readonly List<float> lastFire = new List<float>();
        float spontTimer;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            if (applyAtmosphere) ApplyAtmosphere();
        }

        public void ApplyAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(ambientColor);
            RenderSettings.ambientProbe = sh;
            RenderSettings.skybox = null;
            RenderSettings.reflectionIntensity = reflectionIntensity;
        }

        void Start()
        {
            if (pulseMesh == null)
            {
                Debug.LogWarning("[Cerveau VR] Pas de mesh d'impulsion : régénère le cerveau via Tools > Cerveau VR.");
                enabled = false;
                return;
            }

            for (int i = 0; i < neurons.Count; i++)
            {
                flash.Add(0f);
                lastFire.Add(-999f);
            }
            foreach (var conn in connections)
            {
                var cum = NeuronMeshFactory.Cumulative(conn.points);
                cumulative.Add(cum);
                lengths.Add(cum[cum.Length - 1]);
            }

            mpb = new MaterialPropertyBlock();
            for (int p = 0; p < maxPulses; p++) pool.Add(CreatePulse());
            spontTimer = 0.5f;
        }

        Pulse CreatePulse()
        {
            var go = new GameObject("Impulsion");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * pulseSize;
            go.AddComponent<MeshFilter>().sharedMesh = pulseMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = pulseMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = trailMaterial;
            tr.time = trailTime;
            tr.minVertexDistance = 0.04f;
            tr.widthMultiplier = pulseSize * 0.9f;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;

            go.SetActive(false);
            return new Pulse { go = go, trail = tr };
        }

        /// <summary>Fait s'activer un neurone : il flashe et envoie des impulsions à ses voisins.</summary>
        public void Fire(int i)
        {
            if (Time.time - lastFire[i] < refractoryTime) return;
            lastFire[i] = Time.time;
            flash[i] = 1f;

            var conns = neurons[i].connections;
            if (conns.Count == 0) return;
            bool launched = false;
            foreach (int c in conns)
                if (Random.value < propagationChance) launched |= Launch(c, i);
            if (!launched) Launch(conns[Random.Range(0, conns.Count)], i);
        }

        bool Launch(int c, int from)
        {
            if (pool.Count == 0) return false;
            var p = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            p.conn = c;
            p.reverse = connections[c].a != from;
            p.dist = 0f;
            p.go.transform.localPosition = Sample(c, p.reverse ? lengths[c] : 0f);
            p.go.SetActive(true);
            p.trail.Clear();
            p.trail.emitting = true;
            active.Add(p);
            return true;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            spontTimer -= dt;
            if (spontTimer <= 0f && neurons.Count > 0)
            {
                Fire(Random.Range(0, neurons.Count));
                spontTimer = spontaneousInterval * Random.Range(0.5f, 1.5f);
            }

            for (int k = active.Count - 1; k >= 0; k--)
            {
                var p = active[k];
                int c = p.conn;
                p.dist += pulseSpeed * dt;
                if (p.dist >= lengths[c])
                {
                    p.trail.emitting = false;
                    p.go.SetActive(false);
                    active.RemoveAt(k);
                    pool.Add(p);

                    int target = p.reverse ? connections[c].a : connections[c].b;
                    flash[target] = Mathf.Max(flash[target], 0.5f);
                    if (Random.value < propagationChance) Fire(target);
                }
                else
                {
                    p.go.transform.localPosition = Sample(c, p.reverse ? lengths[c] - p.dist : p.dist);
                }
            }

            for (int i = 0; i < flash.Count; i++)
            {
                if (flash[i] <= 0f) continue;
                flash[i] = Mathf.Max(0f, flash[i] - dt * flashDecay);
                float f = flash[i];
                SetEmission(i, Color.Lerp(neuronBaseEmission, neuronFlashEmission, f * f));
            }
        }

        // =====================================================================
        // Neurones ajoutés en jeu

        /// <summary>Crée l'objet 3D d'un nouveau neurone (échelle 0, à animer), sans l'enregistrer.</summary>
        public GameObject CreateNeuronObject(Vector3 worldPos)
        {
            var b = new BrainMeshBuilder(1);
            NeuronMeshFactory.BuildNeuron(b, Random.Range(0.17f, 0.28f));
            var go = new GameObject("Neurone (cree)");
            go.transform.SetParent(neuronsContainer != null ? neuronsContainer : transform, true);
            go.transform.SetPositionAndRotation(worldPos, Random.rotation);
            go.transform.localScale = Vector3.zero;
            go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh("Neurone_cree");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = neuronMaterial;
            NeuronMeshFactory.ConfigureRenderer(mr);
            return go;
        }

        /// <summary>Enregistre un neurone et fait pousser des connexions vers ses voisins proches.</summary>
        public int RegisterNeuron(Renderer r, Vector3 worldPos)
        {
            var local = transform.InverseTransformPoint(worldPos);
            neurons.Add(new Neuron { renderer = r, position = local });
            flash.Add(1f);
            lastFire.Add(Time.time);
            int idx = neurons.Count - 1;

            var candidates = new List<int>();
            for (int i = 0; i < idx; i++)
                if (Vector3.Distance(neurons[i].position, local) <= autoConnectDistance) candidates.Add(i);
            candidates.Sort((x, y) => Vector3.Distance(neurons[x].position, local)
                .CompareTo(Vector3.Distance(neurons[y].position, local)));

            int count = Mathf.Min(autoConnectMax, candidates.Count);
            for (int k = 0; k < count; k++) StartCoroutine(GrowConnection(idx, candidates[k], 0.3f + k * 0.6f));
            return idx;
        }

        IEnumerator GrowConnection(int a, int b, float delay)
        {
            yield return new WaitForSeconds(delay);

            var pts = NeuronMeshFactory.AxonPath(neurons[a].position, neurons[b].position);
            for (int i = 0; i < pts.Length; i++) pts[i].y = Mathf.Max(pts[i].y, 0.35f);
            var cum = NeuronMeshFactory.Cumulative(pts);
            float total = cum[cum.Length - 1];

            var go = new GameObject("Connexion (croissance)");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "Axone_croissance" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { axonMaterial, myelinMaterial };
            NeuronMeshFactory.ConfigureRenderer(mr);

            // Petite pointe lumineuse au bout de l'axone qui pousse
            var tip = new GameObject("Pointe de croissance").transform;
            tip.SetParent(transform, false);
            tip.localScale = Vector3.one * 0.07f;
            tip.gameObject.AddComponent<MeshFilter>().sharedMesh = pulseMesh;
            var tipR = tip.gameObject.AddComponent<MeshRenderer>();
            tipR.sharedMaterial = pulseMaterial;
            NeuronMeshFactory.ConfigureRenderer(tipR);

            var partial = new List<Vector3>();
            var radii = new List<float>();
            float d = 0f;
            while (d < total)
            {
                d = Mathf.Min(total, d + growSpeed * Time.deltaTime);
                partial.Clear();
                radii.Clear();
                for (int i = 0; i < pts.Length && cum[i] < d; i++) partial.Add(pts[i]);
                Vector3 end = NeuronMeshFactory.SampleAt(pts, cum, d);
                partial.Add(end);
                if (partial.Count >= 2)
                {
                    for (int i = 0; i < partial.Count; i++) radii.Add(NeuronMeshFactory.AxonRadius);
                    radii[radii.Count - 1] = NeuronMeshFactory.AxonRadius * 0.5f;
                    var bld = new BrainMeshBuilder(2);
                    bld.AddTube(partial, radii, 6, 0);
                    bld.Fill(mesh);
                }
                tip.localPosition = end;
                yield return null;
            }

            // Connexion terminée : axone complet avec myéline + bouton synaptique
            var fin = new BrainMeshBuilder(2);
            NeuronMeshFactory.BuildAxon(fin, pts, 0.22f);
            fin.Fill(mesh);
            go.name = "Connexion (creee)";
            Destroy(tip.gameObject);

            connections.Add(new Connection { a = a, b = b, points = pts });
            int c = connections.Count - 1;
            cumulative.Add(cum);
            lengths.Add(total);
            neurons[a].connections.Add(c);
            neurons[b].connections.Add(c);

            flash[a] = 1f;
            flash[b] = 1f;
            Launch(c, a);
        }

        void SetEmission(int i, Color c)
        {
            var r = neurons[i].renderer;
            if (r == null) return;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, c);
            r.SetPropertyBlock(mpb);
        }

        Vector3 Sample(int c, float d)
        {
            var pts = connections[c].points;
            var cum = cumulative[c];
            if (d <= 0f) return pts[0];
            if (d >= lengths[c]) return pts[pts.Length - 1];
            int i = Array.BinarySearch(cum, d);
            if (i >= 0) return pts[i];
            i = ~i;
            float t = (d - cum[i - 1]) / Mathf.Max(1e-5f, cum[i] - cum[i - 1]);
            return Vector3.LerpUnclamped(pts[i - 1], pts[i], t);
        }
    }
}
