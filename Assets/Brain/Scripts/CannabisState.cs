using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace BrainVR
{
    public enum CannabisPhase { Aucune, Montee, Pic, Descente, Terminee }

    /// <summary>
    /// État central de la consommation : dose cumulée, temps écoulé (accéléré) et intensité de l'effet.
    /// Les autres systèmes (molécules THC, alerte, activité neuronale, post-process…) lisent
    /// <see cref="Intensity"/> (0 → 1) et <see cref="DoseMg"/>.
    /// </summary>
    public class CannabisState : MonoBehaviour
    {
        public static CannabisState Instance { get; private set; }

        [Header("Dose")]
        [Tooltip("Quantité ajoutée à chaque clic sur le bloc (mg)")]
        public float doseStepMg = 49f;
        public float maxDoseMg = 490f;

        [Header("Timeline (temps de jeu accéléré)")]
        [Tooltip("Durée totale d'un effet, en minutes « réelles » simulées")]
        public float effectDurationMinutes = 180f;
        [Tooltip("Combien de minutes simulées passent par seconde de jeu")]
        public float minutesPerSecond = 2f;
        [Tooltip("Fin de la montée (min après la prise)")]
        public float onsetEndMinutes = 10f;
        [Tooltip("Fin du pic (min après la prise)")]
        public float peakEndMinutes = 60f;
        [Tooltip("Forme de l'effet d'une prise : X = temps normalisé (0 → durée), Y = effet (0 → 1)")]
        public AnimationCurve effectCurve = DefaultCurve();

        [Header("Événements")]
        public UnityEvent<float> onConsumed = new UnityEvent<float>();

        /// <summary>Appelé à chaque prise (dose cumulée en mg).</summary>
        public static event Action<CannabisState> Consumed;

        struct Intake { public float minute; public float mg; }
        readonly List<Intake> intakes = new List<Intake>();

        public float DoseMg { get; private set; }
        /// <summary>Quand vrai, on ne peut pas consommer (ex. pendant la séquence commentée).</summary>
        public bool Locked { get; set; }
        public float DoseRatio => maxDoseMg > 0f ? Mathf.Clamp01(DoseMg / maxDoseMg) : 0f;
        public bool HasConsumed => intakes.Count > 0;
        public bool IsMaxed => DoseMg >= maxDoseMg - 0.01f;
        /// <summary>Minutes simulées depuis la première prise.</summary>
        public float ElapsedMinutes { get; private set; }
        /// <summary>Minutes simulées depuis la dernière prise.</summary>
        public float MinutesSinceLast => HasConsumed ? ElapsedMinutes - intakes[intakes.Count - 1].minute : 0f;
        /// <summary>Intensité globale de l'effet, 0 → 1 (1 = dose max au pic).</summary>
        public float Intensity { get; private set; }
        /// <summary>Quantité « active » en ce moment (mg pondérés par la courbe).</summary>
        public float ActiveMg { get; private set; }
        public CannabisPhase Phase { get; private set; }
        /// <summary>Durée totale affichée sur la timeline (s'allonge avec les prises tardives).</summary>
        public float TimelineEndMinutes => HasConsumed ? intakes[intakes.Count - 1].minute + effectDurationMinutes : effectDurationMinutes;

        void Reset() => effectCurve = DefaultCurve();

        void Awake()
        {
            Instance = this;
            if (effectCurve == null || effectCurve.length == 0) effectCurve = DefaultCurve();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static AnimationCurve DefaultCurve()
        {
            // Inhalation simplifiée : montée rapide (~10 min), pic, puis lente descente jusqu'à ~3 h.
            var c = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 8f),
                new Keyframe(0.055f, 0.85f),
                new Keyframe(0.12f, 1f, 0f, 0f),
                new Keyframe(0.33f, 0.8f),
                new Keyframe(0.65f, 0.3f),
                new Keyframe(1f, 0f, -0.4f, 0f));
            return c;
        }

        [ContextMenu("Consommer (test)")]
        public void Consume()
        {
            if (IsMaxed || Locked) return;
            float mg = Mathf.Min(doseStepMg, maxDoseMg - DoseMg);
            if (!HasConsumed) ElapsedMinutes = 0f;
            intakes.Add(new Intake { minute = ElapsedMinutes, mg = mg });
            DoseMg += mg;
            Recompute();
            Debug.Log($"[THC] Prise de {mg:0} mg — total {DoseMg:0} / {maxDoseMg:0} mg");
            onConsumed.Invoke(DoseMg);
            Consumed?.Invoke(this);
        }

        void Update()
        {
#if UNITY_EDITOR
            // Test sans casque : touche C (vue Game active) = une prise
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) Consume();
#endif
            if (!HasConsumed) return;
            ElapsedMinutes += Time.deltaTime * minutesPerSecond;
            Recompute();
        }

        void Recompute()
        {
            float active = 0f;
            foreach (var i in intakes)
            {
                float t = (ElapsedMinutes - i.minute) / Mathf.Max(1f, effectDurationMinutes);
                if (t < 0f || t > 1f) continue;
                active += i.mg * Mathf.Clamp01(effectCurve.Evaluate(t));
            }
            ActiveMg = active;
            Intensity = maxDoseMg > 0f ? Mathf.Clamp01(active / maxDoseMg) : 0f;

            if (!HasConsumed) Phase = CannabisPhase.Aucune;
            else
            {
                float m = MinutesSinceLast;
                if (m < onsetEndMinutes) Phase = CannabisPhase.Montee;
                else if (m < peakEndMinutes) Phase = CannabisPhase.Pic;
                else if (m < effectDurationMinutes) Phase = CannabisPhase.Descente;
                else Phase = CannabisPhase.Terminee;
            }
        }

        public static string PhaseLabel(CannabisPhase p)
        {
            switch (p)
            {
                case CannabisPhase.Montee: return "Montée";
                case CannabisPhase.Pic: return "Pic";
                case CannabisPhase.Descente: return "Descente";
                case CannabisPhase.Terminee: return "Effets dissipés";
                default: return "Aucune consommation";
            }
        }
    }
}
