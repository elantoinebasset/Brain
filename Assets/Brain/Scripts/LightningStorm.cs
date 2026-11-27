using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrainVR
{
    public class LightningStorm : MonoBehaviour
    {
        public BrainEnvironment env;
        [Tooltip("Matériau additif (par défaut : celui des traînées d'impulsion)")]
        public Material boltMaterial;
        public Color boltColor = new Color(1f, 0.85f, 1f, 1f);
        public float boltWidth = 0.08f;
        [Tooltip("Nombre de segments d'un éclair")]
        public int segments = 12;
        [Tooltip("Zigzag maximal (m)")]
        public float jitter = 0.35f;
        [Tooltip("Temps entre deux vagues d'éclairs (s)")]
        public float tickInterval = 0.25f;
        [Tooltip("Distance max d'un éclair vers un neurone encore sain (m)")]
        public float spreadDistance = 7f;
        [Tooltip("Probabilité qu'un neurone atteint lance un éclair à chaque vague")]
        [Range(0f, 1f)] public float boltChance = 0.8f;
        [Tooltip("Petits éclairs à l'intérieur / autour des neurones atteints, par vague")]
        public int innerBoltsPerTick = 5;
        [Tooltip("Durée de vie d'un éclair (multiplicateur)")]
        public float boltLifetime = 1.8f;
        public int maxBolts = 80;

        [Header("Style de l'asset « Zap VFX » (laisser vide = éclairs simples)")]
        [Tooltip("Ex. M_VFX_Zap_Lightning_05_LUT_Add (texture de 4 éclairs empilés)")]
        public Material assetBoltMaterial;
        [Tooltip("Plusieurs couleurs : chaque éclair en prend une au hasard (remplace le matériau ci-dessus si rempli)")]
        public Material[] assetBoltMaterials;
        public float assetBoltWidth = 0.45f;
        [Tooltip("Nombre de lignes d'éclairs dans la texture de l'asset")]
        public int assetTextureRows = 4;
        [Tooltip("Zigzag ajouté par-dessus la texture (m)")]
        public float assetJitter = 0.12f;
        public int assetSegments = 4;

        [Header("Impact sur chaque neurone touché")]
        [Tooltip("Ex. VFX_Zap_05_Purple (laisser vide = pas d'impact)")]
        public GameObject impactPrefab;
        [Tooltip("Impacts de couleur, dans le même ordre que les matériaux d'éclairs (l'impact prend la couleur de l'éclair)")]
        public GameObject[] impactPrefabs;
        public float impactScale = 0.35f;
        public float impactLifetime = 2f;
        [Tooltip("Parties de l'effet à désactiver (sol, brûlure, lumières)")]
        public string[] impactPartsToDisable = { "Scorch", "Zap Add Floor", "Point Light", "Light Spawn" };

        [Header("Sons des impacts")]
        public AudioClip[] zapSounds;
        [Range(0f, 1f)] public float zapVolume = 0.6f;
        [Tooltip("Temps minimum entre deux sons (s)")]
        public float minSoundInterval = 0.15f;

        class Bolt
        {
            public LineRenderer lr;
            public Vector3 a, b;
            public float until, nextJitter, amp;
            public int color;
        }

        readonly HashSet<int> struck = new HashSet<int>();
        readonly List<Bolt> pool = new List<Bolt>();
        readonly List<Bolt> active = new List<Bolt>();
        readonly List<int> tmp = new List<int>();
        bool running;
        float tick;
        float lastSound = -10f;
        MaterialPropertyBlock mpb;
        static readonly int ZapStId = Shader.PropertyToID("_ZapTexture_ST");
        bool UseAsset => assetBoltMaterial != null || ColorCount > 0;
        int ColorCount => assetBoltMaterials != null ? assetBoltMaterials.Length : 0;

        int PickColor() => ColorCount > 0 ? Random.Range(0, ColorCount) : 0;

        Material BoltMaterial(int color)
        {
            if (ColorCount > 0 && assetBoltMaterials[color] != null) return assetBoltMaterials[color];
            return assetBoltMaterial != null ? assetBoltMaterial : boltMaterial;
        }

        GameObject ImpactPrefab(int color)
        {
            if (impactPrefabs != null && impactPrefabs.Length > 0)
                return impactPrefabs[Mathf.Clamp(color, 0, impactPrefabs.Length - 1)];
            return impactPrefab;
        }

        public bool IsRunning => running;
        public int StruckCount => struck.Count;

        void Start()
        {
            if (env == null) env = FindFirstObjectByType<BrainEnvironment>();
            if (boltMaterial == null && env != null) boltMaterial = env.trailMaterial;
        }

        [ContextMenu("Lancer les éclairs (test)")]
        public void StartStorm()
        {
            if (env == null) env = FindFirstObjectByType<BrainEnvironment>();
            if (env == null) return;
            running = true;
            for (int i = 0; i < env.neurons.Count; i++)
                if (env.IsAlive(i) && env.IsBlocked(i)) struck.Add(i);
            if (struck.Count == 0)
                for (int i = 0; i < env.neurons.Count; i++)
                    if (env.IsAlive(i)) { Strike(i); break; }
            tick = 0f;
        }

        void Update()
        {
#if UNITY_EDITOR
            // Test sans casque : touche L (vue Game active) = lancer les éclairs
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.lKey.wasPressedThisFrame && !running) StartStorm();
#endif
            if (!running || env == null) return;
            float dt = Time.deltaTime;

            tick -= dt;
            if (tick <= 0f)
            {
                tick = tickInterval * Random.Range(0.7f, 1.3f);
                Step();
            }

            for (int k = active.Count - 1; k >= 0; k--)
            {
                var b = active[k];
                if (Time.time >= b.until)
                {
                    b.lr.enabled = false;
                    active.RemoveAt(k);
                    pool.Add(b);
                    continue;
                }
                if (Time.time >= b.nextJitter)
                {
                    Shape(b);
                    b.nextJitter = Time.time + 0.04f;
                }
            }
        }

        void Step()
        {
            // Neurones atteints (vivants) et neurones encore sains
            tmp.Clear();
            var healthy = new List<int>();
            for (int i = 0; i < env.neurons.Count; i++)
            {
                if (!env.IsAlive(i)) continue;
                if (struck.Contains(i)) tmp.Add(i); else healthy.Add(i);
            }
            if (tmp.Count == 0) return;

            bool spread = false;
            for (int n = tmp.Count - 1; n > 0; n--)
            {
                int j = Random.Range(0, n + 1);
                (tmp[n], tmp[j]) = (tmp[j], tmp[n]);
            }
            int launches = Mathf.Min(tmp.Count, 12);
            for (int k = 0; k < launches; k++)
            {
                if (Random.value > boltChance) continue;
                int from = tmp[k];
                Vector3 pa = env.NeuronWorldPosition(from);
                int target = -1;
                float best = spreadDistance;
                foreach (int h in healthy)
                {
                    float d = Vector3.Distance(pa, env.NeuronWorldPosition(h));
                    if (d < best) { best = d; target = h; }
                }
                if (target >= 0)
                {
                    Strike(target, Fire(pa, env.NeuronWorldPosition(target), 0.22f));
                    healthy.Remove(target);
                    spread = true;
                }
                else
                {
                    // Éclair chaotique entre deux neurones déjà atteints
                    int other = tmp[Random.Range(0, tmp.Count)];
                    if (other != from && Vector3.Distance(pa, env.NeuronWorldPosition(other)) < spreadDistance * 1.5f)
                        Fire(pa, env.NeuronWorldPosition(other), 0.15f);
                }
            }

            // Garantie : la propagation finit toujours par atteindre tout le monde
            if (!spread && healthy.Count > 0)
            {
                int bestFrom = -1, bestTo = -1;
                float best = float.MaxValue;
                foreach (int h in healthy)
                    foreach (int s in tmp)
                    {
                        float d = Vector3.Distance(env.NeuronWorldPosition(h), env.NeuronWorldPosition(s));
                        if (d < best) { best = d; bestFrom = s; bestTo = h; }
                    }
                if (bestTo >= 0)
                {
                    Strike(bestTo, Fire(env.NeuronWorldPosition(bestFrom), env.NeuronWorldPosition(bestTo), 0.25f));
                }
            }

            // Petits éclairs dans / autour des neurones atteints
            for (int k = 0; k < innerBoltsPerTick; k++)
            {
                int i = tmp[Random.Range(0, tmp.Count)];
                Vector3 c = env.NeuronWorldPosition(i);
                Fire(c + Random.insideUnitSphere * 0.15f, c + Random.onUnitSphere * Random.Range(0.4f, 0.9f), 0.12f, 0.5f);
            }
        }

        void Strike(int i, int color = -1)
        {
            if (!struck.Add(i)) return;
            env.SetBlocked(i);
            Vector3 pos = env.NeuronWorldPosition(i);
            var prefab = ImpactPrefab(color >= 0 ? color : PickColor());
            if (prefab != null)
            {
                var fx = Instantiate(prefab, pos, Quaternion.identity, transform);
                fx.transform.localScale = prefab.transform.localScale * impactScale;
                foreach (var part in fx.GetComponentsInChildren<Transform>(true))
                    foreach (var name in impactPartsToDisable)
                        if (part != fx.transform && part.name == name) part.gameObject.SetActive(false);
                Destroy(fx, impactLifetime);
            }
            if (zapSounds != null && zapSounds.Length > 0 && Time.time - lastSound >= minSoundInterval)
            {
                var clip = zapSounds[Random.Range(0, zapSounds.Length)];
                if (clip != null) AudioSource.PlayClipAtPoint(clip, pos, zapVolume);
                lastSound = Time.time;
            }
        }

        int Fire(Vector3 a, Vector3 b, float life, float widthMul = 1f)
        {
            int color = PickColor();
            if (active.Count >= maxBolts) return color;
            Bolt bolt;
            if (pool.Count > 0)
            {
                bolt = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
            }
            else bolt = new Bolt { lr = CreateLine() };
            bolt.a = a;
            bolt.b = b;
            bolt.color = color;
            bolt.lr.sharedMaterial = BoltMaterial(color);
            bolt.amp = (UseAsset ? assetJitter : jitter) * Mathf.Clamp01(Vector3.Distance(a, b) / 3f + 0.2f);
            bolt.until = Time.time + life * boltLifetime * Random.Range(0.8f, 1.4f);
            bolt.nextJitter = 0f;
            bolt.lr.widthMultiplier = (UseAsset ? assetBoltWidth : boltWidth) * widthMul * Random.Range(0.7f, 1.3f);
            bolt.lr.enabled = true;
            Shape(bolt);
            active.Add(bolt);
            return color;
        }

        void Shape(Bolt b)
        {
            int n = Mathf.Max(2, (UseAsset ? assetSegments : segments) + 1);
            if (b.lr.positionCount != n) b.lr.positionCount = n;
            if (UseAsset)
            {
                // Choisit une des lignes d'éclair de la texture (change à chaque crépitement)
                int rows = Mathf.Max(1, assetTextureRows);
                float h = 1f / rows;
                if (mpb == null) mpb = new MaterialPropertyBlock();
                b.lr.GetPropertyBlock(mpb);
                mpb.SetVector(ZapStId, new Vector4(Random.value < 0.5f ? 1f : -1f, h, 0f, Random.Range(0, rows) * h));
                b.lr.SetPropertyBlock(mpb);
            }
            Vector3 dir = b.b - b.a;
            Vector3 side = Vector3.Cross(dir.normalized, Random.onUnitSphere).normalized;
            Vector3 up = Vector3.Cross(dir.normalized, side).normalized;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector3 p = Vector3.Lerp(b.a, b.b, t);
                if (i > 0 && i < n - 1)
                {
                    float env01 = Mathf.Sin(t * Mathf.PI);
                    p += (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * b.amp * env01;
                }
                b.lr.SetPosition(i, p);
            }
        }

        LineRenderer CreateLine()
        {
            var go = new GameObject("Eclair");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = BoltMaterial(0);
            lr.textureMode = LineTextureMode.Stretch;
            lr.useWorldSpace = true;
            lr.positionCount = Mathf.Max(2, segments + 1);
            lr.startColor = boltColor;
            lr.endColor = boltColor;
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.4f));
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = LightProbeUsage.Off;
            lr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            lr.enabled = false;
            return lr;
        }
    }
}
