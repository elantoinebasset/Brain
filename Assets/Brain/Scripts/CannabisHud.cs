using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BrainVR
{
    /// <summary>
    /// Jauge de consommation en haut à gauche du champ de vision.
    /// La barre se remplit en modifiant l'ancre droite de l'image « Remplissage ».
    /// </summary>
    public class CannabisHud : MonoBehaviour
    {
        public CannabisState state;

        [Header("Dose")]
        public RectTransform doseFill;
        public Image doseFillImage;
        public TMP_Text doseValue;
        public Gradient doseColors = DefaultGradient();

        float shownDose;

        static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.45f, 0.9f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.75f, 0.2f), 0.5f), new GradientColorKey(new Color(1f, 0.3f, 0.3f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        void Start()
        {
            if (state == null) state = CannabisState.Instance ?? FindFirstObjectByType<CannabisState>();
        }

        void Update()
        {
            if (state == null) return;

            // Dose (animation douce de la barre)
            shownDose = Mathf.MoveTowards(shownDose, state.DoseRatio, Time.deltaTime * 1.5f);
            SetFill(doseFill, shownDose);
            if (doseFillImage) doseFillImage.color = doseColors.Evaluate(state.DoseRatio);
            if (doseValue) doseValue.text = $"{state.DoseMg:0} / {state.maxDoseMg:0} mg";
        }

        static void SetFill(RectTransform fill, float v)
        {
            if (fill == null) return;
            var max = fill.anchorMax;
            max.x = Mathf.Clamp01(v);
            fill.anchorMax = max;
        }
    }
}
