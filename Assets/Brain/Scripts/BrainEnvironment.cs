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
            public bool removed;
        }

        [Serializable]
        public class Connection
        {
            public int a;
            public int b;
            public Vector3[] points;
            public Renderer renderer;
            public bool removed;
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

        [Header("THC : neurones bloqués (éteints + halo violet)")]
        [ColorUsage(false, true)] public Color blockedEmission = new Color(0.01f, 0.01f, 0.015f);
        public Color blockedBaseColor = new Color(0.22f, 0.22f, 0.27f);
        public Material blockedHaloMaterial;
        public Color blockedHaloColor = new Color(0.8f, 0.25f, 1f, 0.9f);
        public float blockedHaloSize = 1.3f;

        [Header("THC : activité perturbée (0 = normale, 1 = chaos)")]
        [Range(0f, 1f)] public float perturbation;
        [ColorUsage(false, true)] public Color flickerEmission = new Color(1.2f, 1.6f, 2.4f);
        [Tooltip("Accélération des neurotransmetteurs (particules) à perturbation max (0.6 = +60 %)")]
        public float transmitterSpeedBoost = 0.6f;
        [Tooltip("Agitation ajoutée aux particules à perturbation max")]
        public float transmitterNoiseBoost = 0.4f;
        [Tooltip("Part des particules retirées à perturbation max (0.6 = 60 % en moins)")]
        [Range(0f, 1f)] public float transmitterReduction = 0.6f;

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
        [Tooltip("Rayon de la zone qu'on peut attraper autour d'un neurone")]
        public float grabRadius = 0.4f;

        class Pulse
        {
            public GameObject go;
            public TrailRenderer trail;
            public int conn;
            public bool reverse;
            public float dist;
            public float cutAt;   // signal coupé en route (THC)
            public float seed;
        }

        readonly List<Pulse> pool = new List<Pulse>();
        readonly List<Pulse> active = new List<Pulse>();
        readonly List<float[]> cumulative = new List<float[]>();
        readonly List<float> lengths = new List<float>();
        readonly List<float> flash = new List<float>();
        readonly List<float> lastFire = new List<float>();
        readonly HashSet<int> blocked = new HashSet<int>();
        readonly HashSet<int> blockedConns = new HashSet<int>();
        readonly List<KeyValuePair<int, float>> flickers = new List<KeyValuePair<int, float>>();
        float flickerTimer;
        ParticleSystem transmitters;
        float baseSimSpeed = 1f, baseNoise, baseRate;
        bool baseNoiseEnabled;
        float shownPerturbation = -1f;
        class Halo { public Transform t; public Renderer r; public float phase; }
        readonly List<Halo> halos = new List<Halo>();
        MaterialPropertyBlock haloMpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
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

            for (int i = 0; i < neurons.Count; i++)
                if (!neurons[i].removed && neurons[i].renderer != null)
                    NeuronHandle.Attach(this, neurons[i].renderer.gameObject, i, grabRadius);

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

        /// <summary>Neurone dont les récepteurs sont occupés par le THC : il ne s'active plus.</summary>
        public bool IsBlocked(int i) => blocked.Contains(i);
        public int BlockedCount => blocked.Count;

        public void SetBlocked(int i)
        {
            if (i < 0 || i >= neurons.Count || neurons[i].removed) return;
            if (!blocked.Add(i)) return;
            if (i < flash.Count) flash[i] = 0f;
            ApplyBlockedLook(neurons[i].renderer);
            foreach (int c in neurons[i].connections)
                if (c >= 0 && c < connections.Count && !connections[c].removed)
                {
                    blockedConns.Add(c);
                    ApplyBlockedLook(connections[c].renderer);
                }
            if (blockedHaloMaterial != null && neurons[i].renderer != null) CreateHalo(neurons[i].renderer.transform);
        }

        public bool IsAlive(int i) => i >= 0 && i < neurons.Count && !neurons[i].removed && neurons[i].renderer != null;
        public Vector3 NeuronWorldPosition(int i) => neurons[i].renderer.transform.position;

        void ApplyBlockedLook(Renderer r)
        {
            if (r == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, blockedBaseColor);
            mpb.SetColor(EmissionId, blockedEmission);
            r.SetPropertyBlock(mpb);
        }

        void CreateHalo(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Halo THC";
            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = blockedHaloMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            halos.Add(new Halo { t = go.transform, r = r, phase = Random.value * 6f });
        }

        void UpdatePerturbation(float dt)
        {
            // Connexions qui clignotent
            for (int k = flickers.Count - 1; k >= 0; k--)
            {
                if (Time.time < flickers[k].Value) continue;
                RestoreConnection(flickers[k].Key);
                flickers.RemoveAt(k);
            }
            flickerTimer -= dt;
            if (flickerTimer <= 0f && connections.Count > 0)
            {
                flickerTimer = 0.08f;
                if (Random.value < perturbation)
                {
                    int n = 1 + Mathf.FloorToInt(perturbation * 3f);
                    for (int k = 0; k < n; k++)
                    {
                        int c = Random.Range(0, connections.Count);
                        var r = connections[c].removed ? null : connections[c].renderer;
                        if (r == null) continue;
                        if (mpb == null) mpb = new MaterialPropertyBlock();
                        r.GetPropertyBlock(mpb);
                        mpb.SetColor(EmissionId, flickerEmission * Random.Range(0.5f, 1.2f));
                        r.SetPropertyBlock(mpb);
                        flickers.Add(new KeyValuePair<int, float>(c, Time.time + Random.Range(0.05f, 0.18f)));
                    }
                }
            }

            // Neurotransmetteurs (particules) de plus en plus agités
            if (transmitters == null && shownPerturbation < 0f)
            {
                foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.name.StartsWith("Neurotransmetteurs")) { transmitters = ps; break; }
                if (transmitters != null)
                {
                    baseSimSpeed = transmitters.main.simulationSpeed;
                    var nz = transmitters.noise;
                    baseNoiseEnabled = nz.enabled;
                    baseNoise = nz.enabled ? nz.strength.constant : 0f;
                    baseRate = transmitters.emission.rateOverTime.constant;
                }
                shownPerturbation = 0f;
            }
            if (transmitters != null && Mathf.Abs(perturbation - shownPerturbation) > 0.01f)
            {
                shownPerturbation = perturbation;
                var main = transmitters.main;
                main.simulationSpeed = baseSimSpeed * (1f + perturbation * transmitterSpeedBoost);
                var nz = transmitters.noise;
                nz.enabled = baseNoiseEnabled || perturbation > 0.01f;
                nz.strength = baseNoise + perturbation * transmitterNoiseBoost;
                nz.frequency = 0.5f + perturbation * 0.6f;
                var em = transmitters.emission;
                em.rateOverTime = baseRate * (1f - perturbation * transmitterReduction);
            }
        }

        void RestoreConnection(int c)
        {
            if (c < 0 || c >= connections.Count || connections[c].removed) return;
            var r = connections[c].renderer;
            if (r == null) return;
            if (blockedConns.Contains(c)) ApplyBlockedLook(r);
            else r.SetPropertyBlock(null);
        }

        void UpdateHalos()
        {
            if (halos.Count == 0) return;
            var cam = Camera.main;
            if (haloMpb == null) haloMpb = new MaterialPropertyBlock();
            float time = Time.time;
            for (int k = halos.Count - 1; k >= 0; k--)
            {
                var h = halos[k];
                if (h.t == null) { halos.RemoveAt(k); continue; }
                if (cam) h.t.rotation = Quaternion.LookRotation(h.t.position - cam.transform.position);
                float pulse = Mathf.Sin(time * 2.2f + h.phase);
                float parentScale = h.t.parent ? Mathf.Max(1e-4f, h.t.parent.lossyScale.x) : 1f;
                h.t.localScale = Vector3.one * blockedHaloSize * (0.92f + 0.12f * pulse) / parentScale;
                var c = blockedHaloColor;
                c.a *= 0.65f + 0.35f * pulse;
                h.r.GetPropertyBlock(haloMpb);
                haloMpb.SetColor(BaseColorId, c);
                h.r.SetPropertyBlock(haloMpb);
            }
        }

        /// <summary>Fait s'activer un neurone : il flashe et envoie des impulsions à ses voisins.</summary>
        public void Fire(int i)
        {
            if (neurons[i].removed || blocked.Contains(i)) return;
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
            if (pool.Count == 0 || connections[c].removed) return false;
            var p = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            p.conn = c;
            p.reverse = connections[c].a != from;
            p.dist = 0f;
            p.seed = Random.value * 100f;
            p.cutAt = Random.value < perturbation * 0.45f ? lengths[c] * Random.Range(0.2f, 0.85f) : float.MaxValue;
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
                // Activité désordonnée : rafales d'activations
                if (Random.value < perturbation * 0.35f)
                    for (int b = 0; b < 3; b++) Fire(Random.Range(0, neurons.Count));
                spontTimer = spontaneousInterval * Random.Range(0.5f, 1.5f) * (1f - 0.5f * perturbation);
            }

            for (int k = active.Count - 1; k >= 0; k--)
            {
                var p = active[k];
                int c = p.conn;
                float speed = pulseSpeed * Mathf.Max(0.2f, 1f + perturbation * (Mathf.PerlinNoise(p.seed, Time.time * 3f) * 2.2f - 0.8f));
                p.dist += speed * dt;
                if (p.dist >= p.cutAt)
                {
                    // Signal coupé en route
                    p.trail.emitting = false;
                    p.go.SetActive(false);
                    active.RemoveAt(k);
                    pool.Add(p);
                    continue;
                }
                if (connections[c].removed)
                {
                    p.trail.emitting = false;
                    p.go.SetActive(false);
                    active.RemoveAt(k);
                    pool.Add(p);
                    continue;
                }
                if (p.dist >= lengths[c])
                {
                    p.trail.emitting = false;
                    p.go.SetActive(false);
                    active.RemoveAt(k);
                    pool.Add(p);

                    int target = p.reverse ? connections[c].a : connections[c].b;
                    if (!blocked.Contains(target))
                    {
                        flash[target] = Mathf.Max(flash[target], 0.5f);
                        if (Random.value < propagationChance) Fire(target);
                    }
                }
                else
                {
                    p.go.transform.localPosition = Sample(c, p.reverse ? lengths[c] - p.dist : p.dist);
                }
            }

            UpdateHalos();
            UpdatePerturbation(dt);

            for (int i = 0; i < flash.Count; i++)
            {
                if (flash[i] <= 0f || blocked.Contains(i)) continue;
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
            NeuronHandle.Attach(this, r.gameObject, idx, grabRadius);

            var candidates = new List<int>();
            for (int i = 0; i < idx; i++)
                if (!neurons[i].removed && Vector3.Distance(neurons[i].position, local) <= autoConnectDistance) candidates.Add(i);
            candidates.Sort((x, y) => Vector3.Distance(neurons[x].position, local)
                .CompareTo(Vector3.Distance(neurons[y].position, local)));

            int count = Mathf.Min(autoConnectMax, candidates.Count);
            for (int k = 0; k < count; k++) StartCoroutine(GrowConnection(idx, candidates[k], 0.3f + k * 0.6f));
            return idx;
        }

        IEnumerator GrowConnection(int a, int b, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (neurons[a].removed || neurons[b].removed) yield break;

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
                if (neurons[a].removed || neurons[b].removed)
                {
                    Destroy(go);
                    Destroy(tip.gameObject);
                    yield break;
                }
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

            if (neurons[a].removed || neurons[b].removed)
            {
                Destroy(go);
                yield break;
            }
            connections.Add(new Connection { a = a, b = b, points = pts, renderer = mr });
            int c = connections.Count - 1;
            cumulative.Add(cum);
            lengths.Add(total);
            neurons[a].connections.Add(c);
            neurons[b].connections.Add(c);

            flash[a] = 1f;
            flash[b] = 1f;
            Launch(c, a);
        }

        /// <summary>Retire un neurone (et ses connexions) puis fait apparaître une orbe à sa place.</summary>
        public NeuronOrb ConvertNeuronToOrb(int idx, Vector3 worldPos)
        {
            RemoveNeuron(idx);
            var panel = FindFirstObjectByType<NeuronSpawnerPanel>();
            var mat = panel != null && panel.orbMaterial != null ? panel.orbMaterial : pulseMaterial;
            float radius = panel != null ? panel.orbRadius : 0.09f;
            return NeuronOrb.Create(this, panel, worldPos, radius, mat);
        }

        public void RemoveNeuron(int idx)
        {
            if (idx < 0 || idx >= neurons.Count || neurons[idx].removed) return;
            neurons[idx].removed = true;
            foreach (int c in neurons[idx].connections.ToArray()) RemoveConnection(c);
            neurons[idx].connections.Clear();
        }

        public void RemoveConnection(int c)
        {
            var conn = connections[c];
            if (conn.removed) return;
            conn.removed = true;
            neurons[conn.a].connections.Remove(c);
            neurons[conn.b].connections.Remove(c);
            if (conn.renderer != null) Destroy(conn.renderer.gameObject);
        }

        void SetEmission(int i, Color c)
        {
            var r = neurons[i].renderer;
            if (r == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
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
