using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BrainVR.Intro
{
    /// <summary>
    /// Déroulé de la scène Intro :
    /// 1. Écran titre « Entrez dans la simulation »
    /// 2. Voix off d'introduction (le cerveau, le THC)
    /// 3. Explication des règles
    /// 4. Choix de la quantité (1 joint = 49 mg / 10 joints = 490 mg)
    /// 5. « Plongez au cœur de notre centre de contrôle… » puis transition (fondu + accélération des particules)
    ///    et chargement de la scène du cerveau.
    /// Test sans casque (vue Game) : Entrée = bouton du panneau affiché, 1 / 2 = choix de dose, Espace = phrase suivante.
    /// </summary>
    public class IntroFlow : MonoBehaviour
    {
        enum Step { Titre, Narration, Regles, Choix, Transition }

        [Header("Scène suivante")]
        [Tooltip("Nom de la scène du cerveau (doit être dans File > Build Profiles > Scene List)")]
        public string brainSceneName = "SampleScene";

        [Header("Panneaux")]
        public GameObject titlePanel;
        public GameObject narrationPanel;
        public GameObject rulesPanel;
        public GameObject dosePanel;

        [Header("Boutons")]
        public Button startButton;
        public Button rulesContinueButton;
        public Button dose49Button;
        public Button dose490Button;

        [Header("Textes")]
        public TMP_Text rulesText;
        [TextArea(6, 14)]
        public string rules =
            "<b>Comment ça marche ?</b>\n\n" +
            "• <b>Gâchette</b> (index) : appuyer sur les boutons et sur le bloc « Consommer ».\n" +
            "• <b>Joystick gauche</b> : se déplacer.  <b>Joystick droit</b> : tourner.\n" +
            "• <b>Bouton A</b> : passer au texte suivant.  <b>Bouton B</b> : cacher les textes.\n" +
            "• Regardez autour de vous : les neurones, les connexions et les signaux réagissent au THC.\n\n" +
            "Vous pouvez retirer le casque à tout moment si vous ne vous sentez pas bien.";

        [Header("Voix off")]
        public IntroSequencer sequencer;
        [Tooltip("Lu après l'écran titre")]
        public List<IntroLine> introLines = DefaultIntroLines();
        [Tooltip("Lu après le choix de dose, juste avant la transition. {dose} = mg, {joints} = « 1 joint » / « 10 joints »")]
        public List<IntroLine> transitionLines = DefaultTransitionLines();

        [Header("Transition")]
        [Tooltip("Fondu au noir (script ScreenFader d'Antoine). Vide = recherché dans la scène")]
        public ScreenFader fader;
        public float fadeOutDuration = 2f;
        [Tooltip("Particules qui accélèrent pendant la transition (effet « plongée »)")]
        public ParticleSystem tunnel;
        public float tunnelMaxSpeed = 8f;
        public AudioSource transitionSound;

        Step step;
        bool loading;

        void Start()
        {
            if (fader == null) fader = FindFirstObjectByType<ScreenFader>();
            if (startButton) startButton.onClick.AddListener(OnStart);
            if (rulesContinueButton) rulesContinueButton.onClick.AddListener(OnRulesContinue);
            if (dose49Button) dose49Button.onClick.AddListener(() => OnDose(DoseChoice.UnJoint));
            if (dose490Button) dose490Button.onClick.AddListener(() => OnDose(DoseChoice.DixJoints));
            if (rulesText) rulesText.text = rules;
            SimulationSettings.Clear();
            Show(Step.Titre);
        }

        void Update()
        {
#if UNITY_EDITOR
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.enterKey.wasPressedThisFrame)
            {
                if (step == Step.Titre) OnStart();
                else if (step == Step.Regles) OnRulesContinue();
            }
            if (step == Step.Choix)
            {
                if (kb.digit1Key.wasPressedThisFrame) OnDose(DoseChoice.UnJoint);
                if (kb.digit2Key.wasPressedThisFrame) OnDose(DoseChoice.DixJoints);
            }
#endif
        }

        void Show(Step s)
        {
            step = s;
            if (titlePanel) titlePanel.SetActive(s == Step.Titre);
            if (narrationPanel) narrationPanel.SetActive(s == Step.Narration || s == Step.Transition);
            if (rulesPanel) rulesPanel.SetActive(s == Step.Regles);
            if (dosePanel) dosePanel.SetActive(s == Step.Choix);
        }

        // ---------------------------------------------------------------------
        [ContextMenu("1 · Entrer dans la simulation")]
        public void OnStart()
        {
            if (step != Step.Titre) return;
            Show(Step.Narration);
            if (sequencer != null && introLines.Count > 0) sequencer.Play(introLines, () => Show(Step.Regles));
            else Show(Step.Regles);
        }

        [ContextMenu("2 · Règles lues")]
        public void OnRulesContinue()
        {
            if (step != Step.Regles) return;
            Show(Step.Choix);
        }

        public void OnDose(DoseChoice choice)
        {
            if (step != Step.Choix) return;
            SimulationSettings.SetChoice(choice);
            Show(Step.Transition);
            var lines = Formatted(transitionLines);
            if (sequencer != null && lines.Count > 0) sequencer.Play(lines, () => StartCoroutine(LoadBrain()));
            else StartCoroutine(LoadBrain());
        }

        [ContextMenu("3 · Choisir 1 joint (49 mg)")] void Choose49() => OnDose(DoseChoice.UnJoint);
        [ContextMenu("3 · Choisir 10 joints (490 mg)")] void Choose490() => OnDose(DoseChoice.DixJoints);

        // ---------------------------------------------------------------------
        IEnumerator LoadBrain()
        {
            if (loading) yield break;
            loading = true;

            var op = SceneManager.LoadSceneAsync(brainSceneName);
            if (op == null)
            {
                Debug.LogError($"[Intro] Scène « {brainSceneName} » introuvable : ajoute-la dans File > Build Profiles > Scene List.");
                loading = false;
                yield break;
            }
            op.allowSceneActivation = false;

            if (transitionSound) transitionSound.Play();
            if (fader) fader.FadeOut(fadeOutDuration);

            float t = 0f;
            float baseSpeed = tunnel ? tunnel.main.simulationSpeed : 1f;
            while (t < fadeOutDuration)
            {
                t += Time.deltaTime;
                if (tunnel)
                {
                    var main = tunnel.main;
                    main.simulationSpeed = Mathf.Lerp(baseSpeed, tunnelMaxSpeed, t / fadeOutDuration);
                }
                yield return null;
            }

            // Attend la fin du chargement (90 %), puis active la scène. Le ScreenFader du cerveau rouvre au noir.
            while (op.progress < 0.9f) yield return null;
            op.allowSceneActivation = true;
        }

        static List<IntroLine> Formatted(List<IntroLine> src)
        {
            string dose = SimulationSettings.DoseStepMg.ToString("0");
            int j = SimulationSettings.Joints;
            string joints = j + (j > 1 ? " joints" : " joint");
            var list = new List<IntroLine>();
            foreach (var l in src)
                list.Add(new IntroLine
                {
                    title = (l.title ?? "").Replace("{dose}", dose).Replace("{joints}", joints),
                    text = (l.text ?? "").Replace("{dose}", dose).Replace("{joints}", joints),
                    voice = l.voice,
                    cue = l.cue,
                    pauseAfter = l.pauseAfter
                });
            return list;
        }

        // ---------------------------------------------------------------------
        public static List<IntroLine> DefaultIntroLines() => new List<IntroLine>
        {
            new IntroLine
            {
                title = "Le cerveau",
                text = "Le cerveau est un organe essentiel du **système nerveux central**, situé dans la boîte crânienne. " +
                       "Il est composé de milliards de **neurones** qui communiquent entre eux grâce à des signaux électriques et chimiques.",
                cue = "intro_cerveau"
            },
            new IntroLine
            {
                text = "Divisé en plusieurs parties, dont les principales sont le **cerveau**, le **cervelet** et le **tronc cérébral**, " +
                       "il assure le contrôle des mouvements volontaires, la mémoire, le langage, les émotions, les sensations " +
                       "et les fonctions vitales comme la respiration et le rythme cardiaque."
            },
            new IntroLine
            {
                text = "Il reçoit, analyse et transmet les informations provenant du corps et de l'environnement."
            },
            new IntroLine
            {
                title = "Le THC",
                text = "Vous allez désormais consommer une certaine quantité de **cannabis**. " +
                       "Celui-ci contient une molécule psychoactive appelée **THC**, ou tétrahydrocannabinol.",
                cue = "intro_thc"
            },
            new IntroLine
            {
                text = "**49 mg** de THC correspondent à environ **un joint**. **490 mg** correspondent à **10 joints**.",
                pauseAfter = 1.5f
            },
        };

        public static List<IntroLine> DefaultTransitionLines() => new List<IntroLine>
        {
            new IntroLine
            {
                text = "Dose choisie : **{dose} mg de THC** ({joints}).\n" +
                       "Dans le cerveau, appuyez sur le bloc **« Consommer »** pour commencer.",
                cue = "dose_choisie"
            },
            new IntroLine
            {
                title = "Plongez au cœur de notre centre de contrôle…",
                text = "",
                cue = "transition",
                pauseAfter = 0.5f
            },
        };
    }
}
