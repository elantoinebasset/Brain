using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace BrainVR
{
    [Serializable]
    public class TextGroup
    {
        public string title;
        [TextArea(4, 15)]
        public string text;
        [Min(0.01f)]
        public float wordInterval = 0.12f;
        [Min(0f)]
        public float delayAfter = 4f;
    }


    public class TextSequence : MonoBehaviour
    {
        [Header("Références")]
        public GameObject panel;
        public TMP_Text textField;

        [Header("Déroulement")]
        [Min(0f)] public float startDelay = 2f;
        public bool autoAdvance = true;
        public bool hideWhenFinished = true;

        [Header("Groupes de texte")]
        public List<TextGroup> groups = new List<TextGroup>();

        [Header("Événements")]
        public UnityEvent<int> onGroupStarted = new UnityEvent<int>();
        public UnityEvent onSequenceFinished = new UnityEvent();

        enum State { Waiting, Typing, Holding, Finished }
        State state;
        float timer;
        int groupIndex = -1;
        int visibleWords;
        int totalWords;
        readonly List<string> tokens = new List<string>();
        InputAction skipAction;
        InputAction toggleAction;
        bool panelHidden;

        public bool IsFinished => state == State.Finished;
        public int CurrentGroup => groupIndex;

        void Awake()
        {
            skipAction = new InputAction("Passer le texte", InputActionType.Button);
            skipAction.AddBinding("<XRController>{RightHand}/primaryButton");
            skipAction.AddBinding("<Keyboard>/space");
            toggleAction = new InputAction("Cacher le panneau", InputActionType.Button);
            toggleAction.AddBinding("<XRController>{RightHand}/secondaryButton");
            toggleAction.AddBinding("<Keyboard>/b");
        }

        void OnEnable() { skipAction?.Enable(); toggleAction?.Enable(); }
        void OnDisable() { skipAction?.Disable(); toggleAction?.Disable(); }
        void OnDestroy() { skipAction?.Dispose(); toggleAction?.Dispose(); }

        [ContextMenu("Cacher / afficher (bouton B)")]
        public void TogglePanel() => panelHidden = !panelHidden;

        void Start()
        {
            Restart();
        }

        [ContextMenu("Relancer la séquence")]
        public void Restart()
        {
            groupIndex = -1;
            state = State.Waiting;
            timer = startDelay;
            if (textField) textField.text = "";
        }

        /// <summary>Remplace les textes et lance la nouvelle séquence (ex. séquence THC).</summary>
        public void Play(List<TextGroup> newGroups, float delay = 0.3f)
        {
            groups = newGroups;
            groupIndex = -1;
            state = State.Waiting;
            timer = delay;
            panelHidden = false;
            if (textField) textField.text = "";
        }

        [ContextMenu("Texte suivant (bouton A)")]
        public void Skip()
        {
            if (state == State.Waiting) ShowGroup(0);
            else if (state == State.Typing || state == State.Holding) Next();
        }

        TextGroup Current => groups[groupIndex];

        void Update()
        {
            if (toggleAction != null && toggleAction.WasPressedThisFrame()) TogglePanel();
            if (skipAction != null && skipAction.WasPressedThisFrame()) Skip();
            if (state == State.Finished || groups.Count == 0) return;

            float dt = Time.deltaTime;
            switch (state)
            {
                case State.Waiting:
                    timer -= dt;
                    if (timer <= 0f) ShowGroup(0);
                    break;

                case State.Typing:
                    timer -= dt;
                    bool changed = false;
                    while (timer <= 0f && state == State.Typing)
                    {
                        visibleWords++;
                        changed = true;
                        if (visibleWords >= totalWords)
                        {
                            state = State.Holding;
                            timer = Current.delayAfter;
                        }
                        else timer += Current.wordInterval;
                    }
                    if (changed) Render();
                    break;

                case State.Holding:
                    if (!autoAdvance) break;
                    timer -= dt;
                    if (timer <= 0f) Next();
                    break;
            }
        }

        void LateUpdate()
        {
            if (panel == null) return;
            bool show = !panelHidden && state != State.Waiting && !(state == State.Finished && hideWhenFinished);
            if (panel.activeSelf != show) panel.SetActive(show);
        }

        void Next()
        {
            if (groupIndex + 1 < groups.Count) ShowGroup(groupIndex + 1);
            else
            {
                state = State.Finished;
                onSequenceFinished.Invoke();
            }
        }

        void ShowGroup(int i)
        {
            if (i < 0 || i >= groups.Count) { state = State.Finished; return; }
            groupIndex = i;
            Tokenize(Format(groups[i]));
            visibleWords = 0;
            Render();
            onGroupStarted.Invoke(i);
            if (totalWords == 0)
            {
                state = State.Holding;
                timer = groups[i].delayAfter;
            }
            else
            {
                state = State.Typing;
                timer = groups[i].wordInterval;
            }
        }


        static readonly Regex BoldMarkdown = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Singleline);
        static readonly Regex Tags = new Regex(@"<[^>]*>");
        static readonly Regex Spaces = new Regex(@"(\s+)");

        static string Format(TextGroup g)
        {
            string body = BoldMarkdown.Replace((g.text ?? "").Replace("\r", ""), "<b>$1</b>");
            if (string.IsNullOrWhiteSpace(g.title)) return body;
            string title = BoldMarkdown.Replace(g.title, "$1");
            return "<size=125%><b>" + title + "</b></size>\n<size=45%>\n</size>" + body;
        }

        void Tokenize(string text)
        {
            tokens.Clear();
            totalWords = 0;
            foreach (var t in Spaces.Split(text))
            {
                if (t.Length == 0) continue;
                tokens.Add(t);
                if (IsWord(t)) totalWords++;
            }
        }

        static bool IsWord(string token) =>
            !string.IsNullOrWhiteSpace(token) && Tags.Replace(token, "").Trim().Length > 0;

        void Render()
        {
            if (textField == null) return;
            var sb = new StringBuilder();
            int words = 0;
            bool hidden = false;
            foreach (var t in tokens)
            {
                if (!hidden && IsWord(t))
                {
                    if (words >= visibleWords)
                    {
                        sb.Append("<alpha=#00>");
                        hidden = true;
                    }
                    words++;
                }
                sb.Append(t);
            }
            textField.text = sb.ToString();
        }
    }
}
