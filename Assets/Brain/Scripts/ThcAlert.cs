using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrainVR
{
    /// <summary>
    /// Effet ALERTE quand on consomme (clic sur le bloc), au plus une fois toutes les 10 s :
    /// picto devant le joueur, léger flash rouge, très légère secousse du cerveau
    /// (on secoue le décor, pas la caméra, pour ne pas donner mal au cœur).
    /// </summary>
    public class ThcAlert : MonoBehaviour
    {
        [Header("Références (trouvées automatiquement si vides)")]
        public ThcScenario scenario;
        public BrainEnvironment env;

        [Header("Déclenchement")]
        [Tooltip("Temps minimum entre deux alertes (s)")]
        public float cooldown = 10f;

        [Header("Son (à remplir)")]
        [Tooltip("Son joué à chaque alerte (laisser vide = pas de son)")]
        public AudioClip alertSound;
        public AudioSource audioSource;

        [Header("Picto")]
        public CanvasGroup picto;
        public TMP_Text label;
        public string defaultMessage = "ALERTE";
        public float pictoDuration = 2.2f;
        public int blinkCount = 2;

        [Header("Flash rouge")]
        public Material flashMaterial;
        public Color flashColor = new Color(1f, 0.05f, 0.05f, 1f);
        [Range(0f, 1f)] public float flashMaxAlpha = 0.16f;
        public float flashDuration = 0.6f;

        [Header("Secousse (du décor)")]
        [Tooltip("Amplitude en mètres (0.01 = 1 cm)")]
        public float shakeAmplitude = 0.012f;
        public float shakeDuration = 0.45f;
        public float shakeFrequency = 22f;

        Renderer flashRenderer;
        MaterialPropertyBlock mpb;
        Vector3 baseScale;
        Vector3 envBasePos;
        bool envBaseSet;
        float t = -1f;
        float lastAlert = -999f;
        float currentDuration;
        bool currentFlash = true;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            baseScale = transform.localScale;
            if (picto) picto.alpha = 0f;
            currentDuration = pictoDuration;
            if (label == null)
            {
                var tr = transform.Find("Texte");
                if (tr) label = tr.GetComponent<TMP_Text>();
            }
            if (label != null)
            {
                label.rectTransform.sizeDelta = new Vector2(1100f, 170f);
                label.enableAutoSizing = true;
                label.fontSizeMin = 26f;
                label.fontSizeMax = 60f;
                label.textWrappingMode = TextWrappingModes.Normal;
            }
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
        }

        void Start()
        {
            if (scenario == null) scenario = FindFirstObjectByType<ThcScenario>();
            if (env == null) env = FindFirstObjectByType<BrainEnvironment>();
            CannabisState.Consumed += OnConsumed;
            if (env != null) { envBasePos = env.transform.localPosition; envBaseSet = true; }
        }

        void OnDestroy() => CannabisState.Consumed -= OnConsumed;

        void OnConsumed(CannabisState s)
        {
            if (Time.time - lastAlert < cooldown) return;
            Play();
        }

        void EnsureFlash()
        {
            if (flashRenderer != null || flashMaterial == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Flash alerte";
            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(1.6f, 1.2f, 1f);
            flashRenderer = go.GetComponent<Renderer>();
            flashRenderer.sharedMaterial = flashMaterial;
            flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
            flashRenderer.receiveShadows = false;
            flashRenderer.enabled = false;
            mpb = new MaterialPropertyBlock();
        }

        [ContextMenu("Tester l'alerte")]
        public void Play() => Show(defaultMessage, true, null);

        /// <summary>Affiche une alerte avec son message. flash = flash rouge + secousse. clip vide = son par défaut.</summary>
        public void Show(string message, bool flash, AudioClip clip, float duration = -1f)
        {
            EnsureFlash();
            if (label) label.text = string.IsNullOrEmpty(message) ? defaultMessage : message;
            currentFlash = flash;
            currentDuration = duration > 0f ? duration : pictoDuration;
            var c = clip != null ? clip : alertSound;
            if (c != null && audioSource != null) audioSource.PlayOneShot(c);
            lastAlert = Time.time;
            t = 0f;
        }

        void Update()
        {
#if UNITY_EDITOR
            // Test sans casque : touche X (vue Game active) = alerte
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.xKey.wasPressedThisFrame) Play();
#endif
            if (t < 0f) return;
            t += Time.deltaTime;
            float end = Mathf.Max(currentDuration, Mathf.Max(flashDuration, shakeDuration));

            // Flash rouge
            if (flashRenderer != null && currentFlash)
            {
                float f = Mathf.Clamp01(t / Mathf.Max(0.01f, flashDuration));
                float a = flashMaxAlpha * Mathf.Sin(f * Mathf.PI);
                flashRenderer.enabled = a > 0.001f;
                var c = flashColor;
                c.a = a;
                flashRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, c);
                flashRenderer.SetPropertyBlock(mpb);
            }

            // Secousse du décor
            if (env != null && envBaseSet)
            {
                if (t < shakeDuration && currentFlash)
                {
                    float k = 1f - t / shakeDuration;
                    float s = t * shakeFrequency;
                    var off = new Vector3(Mathf.PerlinNoise(s, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, s) - 0.5f, Mathf.PerlinNoise(s, 3.3f) - 0.5f) * 2f;
                    env.transform.localPosition = envBasePos + off * shakeAmplitude * k * k;
                }
                else env.transform.localPosition = envBasePos;
            }

            // Picto : apparition rapide avec léger rebond, clignotements, puis fondu
            if (picto != null)
            {
                float alpha;
                if (t < 0.15f) alpha = t / 0.15f;
                else if (t > currentDuration - 0.4f) alpha = Mathf.Clamp01((currentDuration - t) / 0.4f);
                else alpha = 1f;
                float blinkEnd = 0.15f + blinkCount * 0.3f;
                if (t > 0.15f && t < blinkEnd) alpha *= 0.55f + 0.45f * Mathf.Cos((t - 0.15f) / 0.3f * Mathf.PI * 2f);
                picto.alpha = alpha;
                float pop = t < 0.25f ? 1f + 0.25f * Mathf.Sin(t / 0.25f * Mathf.PI) : 1f;
                transform.localScale = baseScale * pop;
            }

            if (t >= end)
            {
                t = -1f;
                if (picto) picto.alpha = 0f;
                transform.localScale = baseScale;
                if (flashRenderer) flashRenderer.enabled = false;
                if (env != null && envBaseSet) env.transform.localPosition = envBasePos;
            }
        }
    }
}
