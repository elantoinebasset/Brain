using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace BrainVR.Intro
{
    /// <summary>Une phrase de la voix off : texte affiché (sous-titre), clip audio facultatif, événement facultatif.</summary>
    [Serializable]
    public class IntroLine
    {
        [Tooltip("Titre facultatif affiché au-dessus du texte")]
        public string title;
        [TextArea(2, 8)] public string text;
        [Tooltip("Voix off de cette phrase. Vide = durée calculée d'après le nombre de mots")]
        public AudioClip voice;
        [Tooltip("Nom d'événement envoyé au début de la phrase (ex. « regles », « choix_dose »). Vide = aucun")]
        public string cue;
        [Tooltip("Pause après la phrase (s)")]
        [Min(0f)] public float pauseAfter = 1f;
    }

    /// <summary>
    /// Séquenceur de la voix off : lit les phrases une par une, affiche les sous-titres,
    /// joue le clip audio s'il existe et déclenche les événements (cue).
    /// Bouton A (main droite) ou Espace : phrase suivante.
    /// </summary>
    public class IntroSequencer : MonoBehaviour
    {
        [Header("Références")]
        public TMP_Text titleField;
        public TMP_Text textField;
        [Tooltip("Groupe à afficher / cacher (fondu)")]
        public CanvasGroup group;
        public AudioSource voiceSource;

        [Header("Rythme sans voix off")]
        [Tooltip("Temps de lecture par mot quand il n'y a pas de clip audio (s)")]
        public float secondsPerWord = 0.32f;
        public float minDuration = 2.5f;
        public float fadeDuration = 0.35f;

        [Header("Événements")]
        public UnityEvent<int> onLineStarted = new UnityEvent<int>();
        public UnityEvent<string> onCue = new UnityEvent<string>();
        public UnityEvent onFinished = new UnityEvent();

        /// <summary>Événement global (pour d'autres scripts / scènes) : nom de la cue.</summary>
        public static event Action<string> Cue;

        public bool IsPlaying { get; private set; }
        public int CurrentLine { get; private set; } = -1;

        InputAction skipAction;
        bool skipRequested;
        Coroutine routine;
        static readonly Regex BoldMarkdown = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Singleline);

        void Awake()
        {
            skipAction = new InputAction("Phrase suivante", InputActionType.Button);
            skipAction.AddBinding("<XRController>{RightHand}/primaryButton");
            skipAction.AddBinding("<Keyboard>/space");
            if (group) group.alpha = 0f;
        }

        void OnEnable() => skipAction?.Enable();
        void OnDisable() => skipAction?.Disable();
        void OnDestroy() => skipAction?.Dispose();

        void Update()
        {
            if (IsPlaying && skipAction != null && skipAction.WasPressedThisFrame()) skipRequested = true;
        }

        [ContextMenu("Phrase suivante")]
        public void Skip() { if (IsPlaying) skipRequested = true; }

        /// <summary>Lit les phrases ; <paramref name="done"/> est appelé à la fin.</summary>
        public void Play(IList<IntroLine> lines, Action done = null)
        {
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Run(lines, done));
        }

        public void Stop()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            IsPlaying = false;
            if (voiceSource) voiceSource.Stop();
            if (group) group.alpha = 0f;
        }

        IEnumerator Run(IList<IntroLine> lines, Action done)
        {
            IsPlaying = true;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                CurrentLine = i;
                skipRequested = false;

                if (titleField)
                {
                    titleField.text = Format(line.title);
                    titleField.gameObject.SetActive(!string.IsNullOrWhiteSpace(line.title));
                }
                if (textField) textField.text = Format(line.text);

                onLineStarted.Invoke(i);
                if (!string.IsNullOrEmpty(line.cue))
                {
                    onCue.Invoke(line.cue);
                    Cue?.Invoke(line.cue);
                }

                yield return Fade(1f);

                float duration;
                if (line.voice != null && voiceSource != null)
                {
                    voiceSource.Stop();
                    voiceSource.clip = line.voice;
                    voiceSource.Play();
                    duration = line.voice.length;
                }
                else duration = Mathf.Max(minDuration, WordCount(line.text) * secondsPerWord);
                duration += line.pauseAfter;

                float t = 0f;
                while (t < duration && !skipRequested)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                if (voiceSource) voiceSource.Stop();

                bool last = i == lines.Count - 1;
                yield return Fade(last ? 0f : 0.15f);
            }
            if (group) group.alpha = 0f;
            CurrentLine = -1;
            IsPlaying = false;
            routine = null;
            onFinished.Invoke();
            done?.Invoke();
        }

        IEnumerator Fade(float target)
        {
            if (group == null) yield break;
            float from = group.alpha, t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                group.alpha = Mathf.Lerp(from, target, t / Mathf.Max(0.01f, fadeDuration));
                yield return null;
            }
            group.alpha = target;
        }

        static string Format(string s) => BoldMarkdown.Replace((s ?? "").Replace("\r", ""), "<b>$1</b>");

        static int WordCount(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return s.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
