using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BrainVR
{
    /// <summary>
    /// Bloc « Consommation » : chaque sélection (gâchette de saisie au rayon, ou toucher avec la main)
    /// ajoute une dose dans <see cref="CannabisState"/>. Il flotte doucement et réagit au clic.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class ConsumeBlock : MonoBehaviour
    {
        public CannabisState state;
        public TMP_Text label;
        public Renderer blockRenderer;
        [Tooltip("Couleur quand la dose max est atteinte")]
        public Color disabledColor = new Color(0.3f, 0.3f, 0.3f);
        public float bobAmplitude = 0.015f;
        public float spinSpeed = 20f;
        [Tooltip("Délai minimum entre deux prises (s), évite les doubles clics")]
        public float cooldown = 0.35f;
        [Tooltip("Scripts réactivés à chaque clic (vide = le HeadUpPanel de « Panneau - Textes », trouvé automatiquement)")]
        public Behaviour[] enableOnClick;
        [Tooltip("Durée pendant laquelle ils restent actifs avant d'être désactivés (s). 0 = restent actifs")]
        public float enableDuration = 1f;
        Coroutine enableRoutine;

        XRSimpleInteractable interactable;
        Vector3 basePos;
        float punch;
        float lastClick = -10f;
        Color baseColor;
        MaterialPropertyBlock mpb;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(_ => TryConsume());
            basePos = transform.localPosition;
            if (blockRenderer == null) blockRenderer = GetComponent<Renderer>();
            if (blockRenderer && blockRenderer.sharedMaterial && blockRenderer.sharedMaterial.HasProperty("_BaseColor"))
                baseColor = blockRenderer.sharedMaterial.GetColor("_BaseColor");
            mpb = new MaterialPropertyBlock();
        }

        void Start()
        {
            if (state == null) state = CannabisState.Instance ?? FindFirstObjectByType<CannabisState>();
            if (enableOnClick == null || enableOnClick.Length == 0)
            {
                foreach (var h in FindObjectsByType<HeadUpPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (h.name == "Panneau - Textes") { enableOnClick = new Behaviour[] { h }; break; }
            }
            UpdateVisual();
        }

        [ContextMenu("Cliquer (test)")]
        public void TryConsume()
        {
            if (state == null || state.IsMaxed || state.Locked || Time.time - lastClick < cooldown) return;
            lastClick = Time.time;
            state.Consume();
            if (enableOnClick != null && enableOnClick.Length > 0)
            {
                if (enableRoutine != null) StopCoroutine(enableRoutine);
                enableRoutine = StartCoroutine(EnableBriefly());
            }
            punch = 1f;
            UpdateVisual();
        }

        System.Collections.IEnumerator EnableBriefly()
        {
            foreach (var b in enableOnClick) if (b != null) b.enabled = true;
            if (enableDuration <= 0f) yield break;
            yield return new WaitForSeconds(enableDuration);
            foreach (var b in enableOnClick) if (b != null) b.enabled = false;
            enableRoutine = null;
        }

        // Test à la souris dans la vue Game (sans casque)
        void OnMouseDown() => TryConsume();

        bool shownLocked;

        void Update()
        {
            if (state != null && state.Locked != shownLocked)
            {
                shownLocked = state.Locked;
                UpdateVisual();
            }
            float t = Time.time;
            transform.localPosition = basePos + Vector3.up * Mathf.Sin(t * 1.6f) * bobAmplitude;
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
            punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 4f);
            float s = 1f - 0.18f * Mathf.Sin(punch * Mathf.PI);
            transform.localScale = Vector3.one * s * baseScale;
        }

        float baseScale = 1f;
        void OnEnable() => baseScale = transform.localScale.x;

        void UpdateVisual()
        {
            bool maxed = state != null && state.IsMaxed;
            bool locked = state != null && state.Locked;
            if (blockRenderer)
            {
                blockRenderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", maxed || locked ? disabledColor : baseColor);
                blockRenderer.SetPropertyBlock(mpb);
            }
            if (label)
            {
                if (state == null) label.text = "Consommer";
                else if (maxed) label.text = "Dose max atteinte";
                else if (locked) label.text = "Patientez…";
                else label.text = "Consommer\n<size=70%>+" + state.doseStepMg.ToString("0") + " mg</size>";
            }
            interactable.enabled = !maxed && !locked;
        }
    }
}
