using System.Collections.Generic;
using UnityEngine;

namespace BrainVR
{

    public class HallucinationSpawner : MonoBehaviour
    {
        public CannabisState state;

        [Header("Modèles 3D (à remplir)")]
        [Tooltip("Glisse ici tes modèles / prefabs d'hallucination")]
        public List<GameObject> models = new List<GameObject>();
        [Tooltip("Échelle appliquée aux modèles")]
        public float scale = 1f;
        [Tooltip("Correction de rotation (degrés) si le modèle n'est pas dans le bon sens. Ex. Y = 180 s'il tourne le dos, X = -90 s'il est couché")]
        public Vector3 rotationOffset = Vector3.zero;

        [Header("Déclenchement")]
        [Tooltip("Nombre de prises (joints) à partir duquel les hallucinations commencent")]
        public int startAtJoints = 5;
        [Tooltip("Temps entre deux apparitions au début (s, min-max)")]
        public Vector2 intervalAtStart = new Vector2(14f, 22f);
        [Tooltip("Temps entre deux apparitions à la dose max (s, min-max)")]
        public Vector2 intervalAtMax = new Vector2(6f, 11f);

        [Header("Apparition")]
        [Tooltip("Temps pendant lequel elle reste visible (s, min-max)")]
        public Vector2 visibleDuration = new Vector2(2f, 3.5f);
        [Tooltip("Durée du fondu d'apparition / disparition (s)")]
        public float appearTime = 0.6f;
        [Tooltip("Distance au joueur (m, min-max)")]
        public Vector2 distanceRange = new Vector2(3f, 6f);
        [Tooltip("Écart max par rapport à la direction du regard (degrés)")]
        [Range(0f, 90f)] public float maxAngleFromView = 40f;
        [Tooltip("Hauteur par rapport aux yeux (m, min-max)")]
        public Vector2 heightRange = new Vector2(-0.8f, 0.4f);

        GameObject current;
        Vector3 currentScale;
        float timer = 3f, age, life;
        bool stopped;

        void Start()
        {
            if (state == null) state = CannabisState.Instance ?? FindFirstObjectByType<CannabisState>();
        }

        /// <summary>Arrête les hallucinations (ex. fin de simulation).</summary>
        public void Stop()
        {
            stopped = true;
            if (current) Destroy(current);
        }

        void Update()
        {
            if (stopped || state == null) return;
            if (current != null)
            {
                Animate();
                return;
            }
            float step = state.doseStepMg > 0f ? state.doseStepMg : 49f;
            float joints = state.DoseMg / step;
            if (joints < startAtJoints - 0.01f) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            Spawn();
            float maxJoints = state.maxDoseMg / step;
            float k = Mathf.InverseLerp(startAtJoints, maxJoints, joints);
            Vector2 range = Vector2.Lerp(intervalAtStart, intervalAtMax, k);
            timer = Random.Range(range.x, range.y);
        }

        [ContextMenu("Faire apparaître (test)")]
        public void Spawn()
        {
            var candidates = models.FindAll(m => m != null);
            if (candidates.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;

            var prefab = candidates[Random.Range(0, candidates.Count)];
            Vector3 eye = cam.transform.position;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(Random.Range(-maxAngleFromView, maxAngleFromView), Vector3.up) * fwd;
            Vector3 pos = eye + dir * Random.Range(distanceRange.x, distanceRange.y)
                        + Vector3.up * Random.Range(heightRange.x, heightRange.y);
            Vector3 toPlayer = Vector3.ProjectOnPlane(eye - pos, Vector3.up);
            var facing = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer.normalized) : Quaternion.identity;
            // Face au joueur + correction réglable + rotation propre du modèle (celle que tu lui donnes dans le prefab)
            var rot = facing * Quaternion.Euler(rotationOffset) * prefab.transform.localRotation;

            current = Instantiate(prefab, pos, rot, transform);
            current.name = "Hallucination";
            foreach (var c in current.GetComponentsInChildren<Collider>()) c.enabled = false; // ne gêne pas les interactions
            currentScale = prefab.transform.localScale * scale;
            current.transform.localScale = Vector3.zero;
            age = 0f;
            life = Random.Range(visibleDuration.x, visibleDuration.y);
        }

        void Animate()
        {
            age += Time.deltaTime;
            float total = life + appearTime * 2f;
            float k;
            if (age < appearTime) k = age / appearTime;
            else if (age > total - appearTime) k = Mathf.Clamp01((total - age) / appearTime);
            else k = 1f;
            k = k * k * (3f - 2f * k);
            // Léger tremblement de taille et flottement, pour un côté irréel
            float wobble = 1f + 0.04f * Mathf.Sin(age * 9f);
            current.transform.localScale = currentScale * k * wobble;
            current.transform.position += Vector3.up * Mathf.Sin(age * 1.7f) * 0.002f;
            if (age >= total)
            {
                Destroy(current);
                current = null;
            }
        }
    }
}
