using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace BrainVR
{
    public enum ThcAction { Aucune, FlotDeMolecules, PerturbationMaximale, Eclairs, Obscurite }

    /// <summary>Une étape de la séquence 490 mg : un texte (panneau) ou une ALERTE (picto + son).</summary>
    [Serializable]
    public class ThcBeat
    {
        [TextArea(2, 8)] public string text;
        [Tooltip("Affiché dans le picto d'alerte (avec son) au lieu du panneau de textes")]
        public bool isAlert;
        [Tooltip("Alerte avec flash rouge + secousse")]
        public bool flash;
        [Tooltip("Son de cette alerte (vide = son par défaut de « Alerte THC »)")]
        public AudioClip sound;
        [Tooltip("Ce qui se passe dans le cerveau au début de l'étape")]
        public ThcAction action;
        [Min(0.01f)] public float wordInterval = 0.12f;
        [Min(0f)] public float delayAfter = 4f;
        [Tooltip("Durée d'affichage d'une alerte (s)")]
        [Min(0.5f)] public float alertDuration = 3.5f;
    }

    /// <summary>
    /// Scénario de consommation :
    /// - 1re prise : joue la séquence commentée (panneau de textes) et verrouille le bloc jusqu'à la fin ;
    /// - à chaque prise : des molécules de THC entrent devant le joueur et se fixent sur quelques neurones,
    ///   qui deviennent « bloqués » (ils ne s'activent plus).
    /// </summary>
    public class ThcScenario : MonoBehaviour
    {
        [Header("Références (trouvées automatiquement si vides)")]
        public CannabisState state;
        public TextSequence sequence;
        public BrainEnvironment env;
        public ThcAlert alert;
        public LightningStorm storm;
        public PlayerHaloLight playerLight;
        public HallucinationSpawner hallucinations;
        public BrainBreathing breathing;
        public ScreenFader fader;

        [Header("Molécules de THC")]
        public Material moleculeMaterial;
        public Material trailMaterial;
        public Color trailColor = new Color(0.75f, 0.35f, 1f, 1f);
        public float moleculeSize = 0.07f;
        public int moleculesPerDose = 6;
        [Tooltip("Vitesse de déplacement (m/s)")]
        public float moleculeSpeed = 2.2f;
        [Tooltip("Délai entre deux molécules qui entrent (s)")]
        public float spawnInterval = 0.35f;
        [Tooltip("Distance du point d'entrée devant le joueur (m)")]
        public float entryDistance = 6f;
        public float entryHeight = 2f;

        [Header("Neurones bloqués")]
        [Tooltip("Part des neurones bloqués ajoutée à chaque prise (0.06 = 6 %)")]
        [Range(0f, 0.3f)] public float blockedFractionPerDose = 0.06f;
        public int minBlockedPerDose = 2;
        [Tooltip("On choisit de préférence les neurones à moins de cette distance du joueur (pour qu'on les voie)")]
        public float preferredRadius = 9f;

        [Header("Séquence de texte (1re prise)")]
        [Tooltip("Index du texte pendant lequel les molécules entrent (« Regardez, il entre… »)")]
        public int entryGroupIndex = 1;
        [Tooltip("{dose} = dose en mg, {joints} = « 1 joint », « 2 joints »…")]
        public List<TextGroup> groups = DefaultGroups();

        [Header("Activité perturbée selon la dose")]
        [Tooltip("Perturbation atteinte juste avant la dose max (la séquence 490 mg monte à 1)")]
        [Range(0f, 1f)] public float perturbationBeforeMax = 0.7f;

        [Header("Séquence 490 mg (dose max)")]
        [Tooltip("{dose} = dose en mg, {joints} = « 10 joints »")]
        public List<ThcBeat> maxDoseBeats = DefaultMaxBeats();
        [Tooltip("Part des neurones encore sains visés par le flot de molécules")]
        [Range(0f, 1f)] public float floodFraction = 0.5f;
        public int floodMolecules = 50;
        [Tooltip("Durée de l'assombrissement final (s)")]
        public float darkenDuration = 12f;
        [Tooltip("Densité du brouillard une fois tout noir (plus haut = on voit moins loin)")]
        public float darkFogDensity = 0.6f;

        [Header("Événements")]
        [Tooltip("Appelé quand la 1re molécule d'une prise se fixe (déclenche l'alerte)")]
        public UnityEvent onMoleculesBound = new UnityEvent();

        class Molecule
        {
            public Transform t;
            public TrailRenderer trail;
            public Vector3 p0, p1, p2;
            public float u, duration, phase;
            public int neuron;
            public Transform anchor;
            public Vector3 anchorLocal;
            public int batch;
        }

        readonly List<Molecule> flying = new List<Molecule>();
        readonly List<Molecule> attached = new List<Molecule>();
        readonly Queue<int> pending = new Queue<int>();   // neurone visé par chaque molécule à lancer
        readonly HashSet<int> targeted = new HashSet<int>();
        float spawnTimer;
        float currentSpawnInterval;
        bool maxPlayed, maxPerturbation;
        int batchCounter;
        readonly HashSet<int> alertedBatches = new HashSet<int>();
        readonly Queue<int> pendingBatch = new Queue<int>();
        bool sequencePlayed, sequenceRunning, waitingEntry;
        Vector3 entryPoint;
        bool entrySet;

        void Start()
        {
            if (state == null) state = CannabisState.Instance ?? FindFirstObjectByType<CannabisState>();
            if (sequence == null) sequence = FindFirstObjectByType<TextSequence>();
            if (env == null) env = FindFirstObjectByType<BrainEnvironment>();
            if (alert == null) alert = FindFirstObjectByType<ThcAlert>();
            if (storm == null) storm = FindFirstObjectByType<LightningStorm>();
            if (playerLight == null) playerLight = FindFirstObjectByType<PlayerHaloLight>(FindObjectsInactive.Include);
            if (hallucinations == null) hallucinations = FindFirstObjectByType<HallucinationSpawner>();
            if (breathing == null) breathing = FindFirstObjectByType<BrainBreathing>();
            if (fader == null) fader = FindFirstObjectByType<ScreenFader>();
            currentSpawnInterval = spawnInterval;
            CannabisState.Consumed += OnConsumed;
            if (sequence != null)
            {
                sequence.onGroupStarted.AddListener(OnGroupStarted);
                sequence.onSequenceFinished.AddListener(OnSequenceFinished);
            }
        }

        void OnDestroy()
        {
            CannabisState.Consumed -= OnConsumed;
            if (state != null) state.Locked = false;
        }

        // ---------------------------------------------------------------------
        void OnConsumed(CannabisState s)
        {
            if (s.IsMaxed && !maxPlayed)
            {
                maxPlayed = true;
                StartCoroutine(MaxDoseSequence(s));
                return;
            }
            QueueMolecules(PickNeurons());
            if (!sequencePlayed && sequence != null && groups.Count > 0)
            {
                sequencePlayed = true;
                sequenceRunning = true;
                waitingEntry = true;
                if (state != null) state.Locked = true;
                sequence.Play(Formatted(s));
            }
        }

        void OnGroupStarted(int i)
        {
            if (sequenceRunning && i >= entryGroupIndex) waitingEntry = false;
        }

        void OnSequenceFinished()
        {
            if (!sequenceRunning) return;
            sequenceRunning = false;
            waitingEntry = false;
            if (state != null) state.Locked = false;
        }

        static string Fill(string text, CannabisState s)
        {
            float step = s.doseStepMg > 0f ? s.doseStepMg : 49f;
            int joints = Mathf.Max(1, Mathf.RoundToInt(s.DoseMg / step));
            return (text ?? "").Replace("{dose}", s.DoseMg.ToString("0"))
                               .Replace("{joints}", joints + (joints > 1 ? " joints" : " joint"));
        }

        // ---------------------------------------------------------------------
        // Séquence 490 mg
        IEnumerator MaxDoseSequence(CannabisState s)
        {
            if (state != null) state.Locked = true;
            waitingEntry = true;
            // Courte transition avant la séquence finale
            if (fader != null) fader.Dip();
            for (int b = 0; b < maxDoseBeats.Count; b++)
            {
                var beat = maxDoseBeats[b];
                string text = Fill(beat.text, s);
                DoAction(beat.action);
                bool last = b == maxDoseBeats.Count - 1;

                if (beat.isAlert)
                {
                    if (alert != null) alert.Show(text, beat.flash, beat.sound, beat.alertDuration);
                    yield return new WaitForSeconds(beat.alertDuration);
                }
                else if (sequence != null)
                {
                    if (last) sequence.hideWhenFinished = false; // le message de fin reste affiché
                    sequence.Play(new List<TextGroup> { new TextGroup { text = text, wordInterval = beat.wordInterval, delayAfter = beat.delayAfter } }, 0.1f);
                    yield return null;
                    while (!sequence.IsFinished) yield return null;
                }
                else yield return new WaitForSeconds(5f);
            }
        }

        void DoAction(ThcAction action)
        {
            switch (action)
            {
                case ThcAction.FlotDeMolecules:
                    waitingEntry = false;
                    currentSpawnInterval = 0.08f;
                    var targets = PickNeurons(floodFraction);
                    if (targets.Count == 0) targets = PickNeurons();
                    QueueMolecules(targets, floodMolecules);
                    break;
                case ThcAction.PerturbationMaximale:
                    maxPerturbation = true;
                    break;
                case ThcAction.Eclairs:
                    maxPerturbation = true;
                    if (storm != null) storm.StartStorm();
                    break;
                case ThcAction.Obscurite:
                    if (breathing != null) breathing.enabled = false; // remet les lumières à leur base avant de les éteindre
                    StartCoroutine(Darken());
                    if (hallucinations != null) hallucinations.Stop();
                    if (playerLight != null) playerLight.TurnOn();
                    break;
            }
        }

        IEnumerator Darken()
        {
            Color amb0 = RenderSettings.ambientLight;
            Color fog0 = RenderSettings.fogColor;
            float dens0 = RenderSettings.fogDensity;
            RenderSettings.fog = true;
            var lights = env != null ? env.GetComponentsInChildren<Light>() : new Light[0];
            var intens = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) intens[i] = lights[i].intensity;

            float t = 0f;
            while (t < darkenDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / darkenDuration);
                var amb = Color.Lerp(amb0, Color.black, k);
                RenderSettings.ambientLight = amb;
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight(amb);
                RenderSettings.ambientProbe = sh;
                RenderSettings.fogColor = Color.Lerp(fog0, Color.black, k);
                RenderSettings.fogDensity = Mathf.Lerp(dens0, darkFogDensity, k);
                for (int i = 0; i < lights.Length; i++) if (lights[i]) lights[i].intensity = intens[i] * (1f - k);
                yield return null;
            }
        }

        List<TextGroup> Formatted(CannabisState s)
        {
            float step = s.doseStepMg > 0f ? s.doseStepMg : 49f;
            int joints = Mathf.Max(1, Mathf.RoundToInt(s.DoseMg / step));
            string dose = s.DoseMg.ToString("0");
            string j = joints + (joints > 1 ? " joints" : " joint");
            var list = new List<TextGroup>();
            foreach (var g in groups)
                list.Add(new TextGroup
                {
                    title = (g.title ?? "").Replace("{dose}", dose).Replace("{joints}", j),
                    text = (g.text ?? "").Replace("{dose}", dose).Replace("{joints}", j),
                    wordInterval = g.wordInterval,
                    delayAfter = g.delayAfter
                });
            return list;
        }

        // ---------------------------------------------------------------------
        List<int> PickNeurons(float fraction = -1f)
        {
            var result = new List<int>();
            if (env == null) return result;
            var cam = Camera.main;
            Vector3 eye = cam ? cam.transform.position : Vector3.up * 1.6f;
            Vector3 fwd = cam ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;

            // Score : d'abord les neurones devant le joueur et pas trop loin (pour qu'on voie le blocage)
            var candidates = new List<(int idx, float score)>();
            int alive = 0;
            for (int i = 0; i < env.neurons.Count; i++)
            {
                var n = env.neurons[i];
                if (n.removed || n.renderer == null) continue;
                alive++;
                if (env.IsBlocked(i) || targeted.Contains(i)) continue;
                Vector3 to = n.renderer.transform.position - eye;
                float dist = to.magnitude;
                float angle = Vector3.Angle(fwd, Vector3.ProjectOnPlane(to, Vector3.up));
                float score = angle / 45f + Mathf.Max(0f, dist - preferredRadius) / 3f + Random.value * 0.6f;
                candidates.Add((i, score));
            }
            int count = fraction >= 0f
                ? Mathf.RoundToInt(candidates.Count * fraction)
                : Mathf.Max(minBlockedPerDose, Mathf.RoundToInt(alive * blockedFractionPerDose));
            candidates.Sort((x, y) => x.score.CompareTo(y.score));
            for (int k = 0; k < candidates.Count && result.Count < count; k++) result.Add(candidates[k].idx);
            foreach (var i in result) targeted.Add(i);
            return result;
        }

        void QueueMolecules(List<int> targets, int count = -1)
        {
            if (count < 0) count = moleculesPerDose;
            if (env == null) return;
            if (targets.Count == 0)
            {
                // Plus de neurone libre : les molécules renforcent ceux déjà touchés
                foreach (var i in targeted) targets.Add(i);
                if (targets.Count == 0) return;
            }
            batchCounter++;
            for (int k = 0; k < count; k++)
            {
                pending.Enqueue(targets[k % targets.Count]);
                pendingBatch.Enqueue(batchCounter);
            }
            entrySet = false;
        }

        // ---------------------------------------------------------------------
        void Update()
        {
            float dt = Time.deltaTime;

            // Activité neuronale perturbée selon la dose
            if (env != null && state != null)
            {
                float target = maxPerturbation ? 1f : state.DoseRatio * perturbationBeforeMax;
                env.perturbation = Mathf.MoveTowards(env.perturbation, target, dt * 0.25f);
            }

            if (pending.Count > 0 && !waitingEntry)
            {
                if (!entrySet)
                {
                    var cam = Camera.main;
                    if (cam)
                    {
                        Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                        entryPoint = cam.transform.position + fwd * entryDistance + Vector3.up * entryHeight;
                    }
                    else entryPoint = new Vector3(0f, 3.5f, entryDistance);
                    entrySet = true;
                    spawnTimer = 0f;
                }
                spawnTimer -= dt;
                if (spawnTimer <= 0f)
                {
                    Launch(pending.Dequeue(), pendingBatch.Count > 0 ? pendingBatch.Dequeue() : 0);
                    spawnTimer = currentSpawnInterval;
                }
            }

            for (int k = flying.Count - 1; k >= 0; k--)
            {
                var m = flying[k];
                if (m.anchor == null) { Kill(m); flying.RemoveAt(k); continue; }
                m.p2 = m.anchor.TransformPoint(m.anchorLocal);
                m.u += dt / m.duration;
                float u = Mathf.Clamp01(m.u);
                float e = u * u * (3f - 2f * u);
                Vector3 a = Vector3.Lerp(m.p0, m.p1, e);
                Vector3 b = Vector3.Lerp(m.p1, m.p2, e);
                Vector3 pos = Vector3.Lerp(a, b, e);
                // Légère ondulation pendant le trajet
                pos += new Vector3(Mathf.Sin(Time.time * 5f + m.phase), Mathf.Cos(Time.time * 4f + m.phase), 0f) * 0.05f * (1f - u);
                m.t.position = pos;
                if (u >= 1f)
                {
                    flying.RemoveAt(k);
                    Attach(m);
                }
            }

            for (int k = attached.Count - 1; k >= 0; k--)
            {
                var m = attached[k];
                if (m.t == null || m.anchor == null) { Kill(m); attached.RemoveAt(k); continue; }
                float s = 1f + 0.18f * Mathf.Sin(Time.time * 3f + m.phase);
                m.t.localScale = Vector3.one * moleculeSize * s / Mathf.Max(1e-4f, m.anchor.lossyScale.x);
            }
        }

        void Launch(int neuron, int batch)
        {
            if (neuron < 0 || neuron >= env.neurons.Count) return;
            var n = env.neurons[neuron];
            if (n.removed || n.renderer == null) return;

            var go = new GameObject("Molecule THC");
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * moleculeSize;
            go.AddComponent<MeshFilter>().sharedMesh = env.pulseMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = moleculeMaterial != null ? moleculeMaterial : env.pulseMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = trailMaterial != null ? trailMaterial : env.trailMaterial;
            tr.time = 0.5f;
            tr.minVertexDistance = 0.03f;
            tr.widthMultiplier = moleculeSize * 0.8f;
            tr.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = ShadowCastingMode.Off;

            // Récepteur : un point à la surface du corps du neurone
            var anchor = n.renderer.transform;
            float soma = 0.3f; // rayon max du corps du neurone : la molécule reste visible à la surface
            Vector3 dir = Random.onUnitSphere;
            Vector3 local = anchor.InverseTransformDirection(dir) * soma / Mathf.Max(1e-4f, anchor.lossyScale.x);

            var m = new Molecule
            {
                t = go.transform,
                trail = tr,
                anchor = anchor,
                anchorLocal = local,
                neuron = neuron,
                batch = batch,
                phase = Random.value * 10f
            };
            m.p0 = entryPoint + Random.insideUnitSphere * 0.4f;
            m.p2 = anchor.TransformPoint(local);
            Vector3 mid = (m.p0 + m.p2) * 0.5f;
            m.p1 = mid + Random.insideUnitSphere * 1.5f + Vector3.up * 0.8f;
            float dist = Vector3.Distance(m.p0, m.p1) + Vector3.Distance(m.p1, m.p2);
            m.duration = Mathf.Max(1.5f, dist / Mathf.Max(0.1f, moleculeSpeed));
            go.transform.position = m.p0;
            flying.Add(m);
        }

        void Attach(Molecule m)
        {
            m.t.SetParent(m.anchor, true);
            m.t.position = m.anchor.TransformPoint(m.anchorLocal);
            if (m.trail) m.trail.emitting = false;
            env.SetBlocked(m.neuron);
            attached.Add(m);
            if (alertedBatches.Add(m.batch)) onMoleculesBound.Invoke();
        }

        static void Kill(Molecule m)
        {
            if (m.t != null) Destroy(m.t.gameObject);
        }

        // ---------------------------------------------------------------------
        public static List<ThcBeat> DefaultMaxBeats() => new List<ThcBeat>
        {
            new ThcBeat { text = "Vous avez choisi de consommer **{dose} mg de THC**, l'équivalent de **{joints}**.", delayAfter = 3f },
            new ThcBeat { isAlert = true, flash = true, text = "EXPOSITION MASSIVE AU THC DÉTECTÉE !" },
            new ThcBeat { text = "Vous avez choisi de consommer l'équivalent de **{joints}** en une seule fois, soit environ **{dose} mg de THC** " +
                                 "présents dans le cannabis, sur la base de 49 mg par joint." },
            new ThcBeat { text = "**Attention** : cette quantité représente le THC contenu dans les joints, " +
                                 "et non la quantité réellement absorbée par votre organisme." },
            new ThcBeat { action = ThcAction.FlotDeMolecules,
                          text = "Elle pénètre dans votre organisme et atteint le cerveau, ici, elle agit directement sur le " +
                                 "**système endocannabinoïde**, un système naturel qui participe à la régulation de nombreuses fonctions cérébrales." },
            new ThcBeat { isAlert = true, action = ThcAction.PerturbationMaximale, text = "ACTIVATION MASSIVE DES RÉCEPTEURS CANNABINOÏDES !" },
            new ThcBeat { text = "Avec une exposition aussi importante, leur fonctionnement est fortement perturbé. " +
                                 "La régulation de la libération des **neurotransmetteurs**, ces substances chimiques qui permettent " +
                                 "aux neurones de communiquer entre eux, est bouleversée." },
            new ThcBeat { text = "Le THC modifie notamment la libération du **glutamate** et du **GABA**, " +
                                 "deux neurotransmetteurs essentiels à l'équilibre de l'activité cérébrale." },
            new ThcBeat { isAlert = true, action = ThcAction.Eclairs, text = "PERTURBATION DE L'HIPPOCAMPE !" },
            new ThcBeat { isAlert = true, text = "DYSFONCTIONNEMENT DU CORTEX PRÉFRONTAL !" },
            new ThcBeat { text = "Cette région intervient dans la prise de décision, le raisonnement et le contrôle des comportements. " +
                                 "Une exposition massive au THC peut fortement altérer ces fonctions. Le jugement devient moins fiable, " +
                                 "l'attention diminue et la capacité à prendre des décisions est perturbée.", delayAfter = 5f },
            new ThcBeat { isAlert = true, text = "PERTURBATION DES CIRCUITS MOTEURS !" },
            new ThcBeat { isAlert = true, flash = true, alertDuration = 4.5f, text = "ATTENTION : INTOXICATION AIGUË AU THC !" },
            new ThcBeat { text = "Une telle consommation en une seule fois expose à des effets intenses : confusion, panique, anxiété importante, " +
                                 "accélération du rythme cardiaque, troubles de la perception et parfois hallucinations ou perte de connaissance.", delayAfter = 5f },
            new ThcBeat { text = "Notre centre de contrôle est désormais fortement perturbé. " +
                                 "Les communications entre les neurones ne fonctionnent plus normalement.", delayAfter = 4f },
            new ThcBeat { action = ThcAction.Obscurite, delayAfter = 30f,
                          text = "**Fin de simulation** : une exposition massive au THC peut entraîner une intoxication aiguë " +
                                 "et nécessite une vigilance médicale." },
        };

        public static List<TextGroup> DefaultGroups() => new List<TextGroup>
        {
            new TextGroup
            {
                title = "Consommation de THC",
                text = "Vous avez choisi de consommer **{dose} mg de THC**, l'équivalent de **{joints}**.",
                wordInterval = 0.12f, delayAfter = 3f
            },
            new TextGroup
            {
                text = "Regardez, il entre dans le cerveau, agissant directement sur le **système endocannabinoïde**, " +
                       "un système naturel qui participe à la régulation de nombreuses fonctions cérébrales.",
                wordInterval = 0.12f, delayAfter = 4f
            },
            new TextGroup
            {
                text = "Le THC se fixe maintenant sur les **récepteurs** présents à la surface des neurones. " +
                       "Normalement, ces récepteurs sont activés par des molécules produites naturellement par notre organisme, " +
                       "comme l'**anandamide**.",
                wordInterval = 0.12f, delayAfter = 4f
            },
            new TextGroup
            {
                text = "Ces récepteurs permettent notamment de réguler la libération des **neurotransmetteurs**, " +
                       "les substances chimiques qui transmettent les informations entre les neurones.",
                wordInterval = 0.12f, delayAfter = 4f
            },
            new TextGroup
            {
                text = "Dès lors que le THC se fixe sur les récepteurs, il perturbe cette régulation. " +
                       "Il modifie alors la libération de neurotransmetteurs, qui jouent un rôle essentiel dans l'activité cérébrale.",
                wordInterval = 0.12f, delayAfter = 4f
            },
            new TextGroup
            {
                text = "Vous avez désormais du mal à **mémoriser** de nouvelles informations.",
                wordInterval = 0.12f, delayAfter = 4f
            },
            new TextGroup
            {
                text = "Au niveau du **cortex préfrontal**, qui intervient dans la concentration, la prise de décision et le raisonnement, " +
                       "le cannabis peut diminuer les capacités d'attention et ralentir certaines fonctions cognitives.",
                wordInterval = 0.12f, delayAfter = 4f
            },
            new TextGroup
            {
                text = "Et ici, voici le **cervelet** et des circuits moteurs, le THC peut perturber la coordination des mouvements, " +
                       "l'équilibre et les réflexes.",
                wordInterval = 0.12f, delayAfter = 5f
            },
        };
    }
}
