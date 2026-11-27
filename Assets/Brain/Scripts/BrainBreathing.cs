using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BrainVR
{
    /// <summary>
    /// Ambiance « vivante » : la paroi du cerveau et les lumières pulsent lentement comme une respiration / un cœur,
    /// le rythme s'accélère avec la dose, et de temps en temps une vague d'activité traverse la cavité.
    /// </summary>
    public class BrainBreathing : MonoBehaviour
    {
        public BrainEnvironment env;
        public CannabisState state;
        [Tooltip("Paroi du cerveau (trouvée automatiquement : « Paroi cerebrale »)")]
        public Transform wall;

        [Header("Respiration")]
        [Tooltip("Amplitude de la pulsation de la paroi (0.012 = 1,2 %)")]
        public float wallAmplitude = 0.012f;
        [Tooltip("Amplitude de la pulsation des lumières (0.12 = ±12 %)")]
        public float lightAmplitude = 0.12f;
        [Tooltip("Durée d'un cycle au calme (s)")]
        public float periodCalm = 4.5f;
        [Tooltip("Durée d'un cycle à la dose max (s) : le rythme s'accélère avec la consommation")]
        public float periodMaxDose = 2.2f;

        [Header("Vagues d'activité")]
        [Tooltip("Temps entre deux vagues (s, min-max)")]
        public Vector2 waveInterval = new Vector2(12f, 25f);
        [Tooltip("Durée de la traversée d'une vague (s)")]
        public float waveDuration = 2.5f;

        Light[] lights = new Light[0];
        float[] baseIntensity = new float[0];
        Vector3 wallBase = Vector3.one;
        float phase, waveTimer;

        void Start()
        {
            if (env == null) env = FindFirstObjectByType<BrainEnvironment>();
            if (state == null) state = CannabisState.Instance ?? FindFirstObjectByType<CannabisState>();
            if (env != null)
            {
                if (wall == null) wall = env.transform.Find("Paroi cerebrale");
                lights = env.GetComponentsInChildren<Light>();
                baseIntensity = new float[lights.Length];
                for (int i = 0; i < lights.Length; i++) baseIntensity[i] = lights[i].intensity;
            }
            if (wall != null) wallBase = wall.localScale;
            waveTimer = Random.Range(waveInterval.x * 0.5f, waveInterval.y * 0.5f);
        }

        void OnDisable()
        {
            // Remet tout à l'état de base (ex. avant l'obscurité finale)
            if (wall != null) wall.localScale = wallBase;
            for (int i = 0; i < lights.Length; i++) if (lights[i]) lights[i].intensity = baseIntensity[i];
            StopAllCoroutines();
        }

        void Update()
        {
            float dose = state != null ? state.DoseRatio : 0f;
            float period = Mathf.Lerp(periodCalm, periodMaxDose, dose);
            phase += Time.deltaTime / Mathf.Max(0.2f, period) * Mathf.PI * 2f;
            float b = Mathf.Sin(phase) * 0.5f + 0.5f; // 0 → 1

            if (wall != null) wall.localScale = wallBase * (1f + wallAmplitude * b);
            for (int i = 0; i < lights.Length; i++)
                if (lights[i]) lights[i].intensity = baseIntensity[i] * (1f + lightAmplitude * (b * 2f - 1f));

            waveTimer -= Time.deltaTime;
            if (waveTimer <= 0f && env != null)
            {
                StartCoroutine(Wave());
                waveTimer = Random.Range(waveInterval.x, waveInterval.y);
            }
        }

        [ContextMenu("Lancer une vague (test)")]
        public void TriggerWave()
        {
            if (env != null) StartCoroutine(Wave());
        }

        IEnumerator Wave()
        {
            // Une vague d'activations qui traverse la cavité dans une direction au hasard
            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            var order = new List<KeyValuePair<float, int>>();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < env.neurons.Count; i++)
            {
                if (!env.IsAlive(i)) continue;
                float p = Vector3.Dot(env.NeuronWorldPosition(i), dir);
                order.Add(new KeyValuePair<float, int>(p, i));
                min = Mathf.Min(min, p);
                max = Mathf.Max(max, p);
            }
            if (order.Count == 0) yield break;
            order.Sort((a, c) => a.Key.CompareTo(c.Key));
            float span = Mathf.Max(0.01f, max - min);
            float t = 0f;
            int k = 0;
            while (k < order.Count)
            {
                t += Time.deltaTime;
                while (k < order.Count && (order[k].Key - min) / span * waveDuration <= t)
                {
                    env.Fire(order[k].Value);
                    k++;
                }
                yield return null;
            }
        }
    }
}
