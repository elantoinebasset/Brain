using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrainVR.Intro
{
    public enum DoseChoice
    {
        /// <summary>1 joint (49 mg) par prise : séquence commentée 49 mg, puis on peut monter jusqu'à 490 mg.</summary>
        UnJoint,
        /// <summary>10 joints (490 mg) d'un coup : séquence ALERTE 490 mg directement.</summary>
        DixJoints
    }

    /// <summary>
    /// Choix fait dans la scène Intro, gardé en mémoire pendant le chargement de la scène du cerveau.
    /// Quand la scène du cerveau se charge, la dose par prise du <see cref="CannabisState"/> est réglée
    /// selon le choix — sans rien modifier dans la scène elle-même.
    /// </summary>
    public static class SimulationSettings
    {
        public const float MgParJoint = 49f;

        public static bool HasChoice { get; private set; }
        public static DoseChoice Choice { get; private set; } = DoseChoice.UnJoint;

        public static float DoseStepMg => Choice == DoseChoice.DixJoints ? MgParJoint * 10f : MgParJoint;
        public static int Joints => Choice == DoseChoice.DixJoints ? 10 : 1;

        public static void SetChoice(DoseChoice choice)
        {
            Choice = choice;
            HasChoice = true;
            Debug.Log($"[Intro] Dose choisie : {DoseStepMg:0} mg ({Joints} joint{(Joints > 1 ? "s" : "")})");
        }

        public static void Clear() => HasChoice = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            HasChoice = false;
            Choice = DoseChoice.UnJoint;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!HasChoice) return;
            // Appelé après Awake/OnEnable des objets de la scène, avant leurs Start.
            var state = Object.FindFirstObjectByType<CannabisState>(FindObjectsInactive.Include);
            if (state == null) return;
            state.doseStepMg = Mathf.Min(DoseStepMg, state.maxDoseMg);
            Debug.Log($"[Intro] Scène « {scene.name} » : dose par prise réglée à {state.doseStepMg:0} mg.");
        }
    }
}
