using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrainVR
{
    /// <summary>
    /// Fondu de l'écran (VR) : un voile noir devant les yeux.
    /// Ouverture au noir au lancement, et petit assombrissement entre les grandes étapes (Dip).
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        [Tooltip("Matériau transparent (par défaut : Flash_alerte, coloré en noir ici)")]
        public Material fadeMaterial;
        public Color color = Color.black;

        [Header("Ouverture au lancement")]
        public bool fadeInOnStart = true;
        public float startDelay = 0.3f;
        public float fadeInDuration = 3f;

        Renderer quad;
        MaterialPropertyBlock mpb;
        float alpha = 1f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Start()
        {
            alpha = fadeInOnStart ? 1f : 0f;
            Ensure();
            Apply();
            if (fadeInOnStart) StartCoroutine(FadeTo(0f, fadeInDuration, startDelay));
        }

        void LateUpdate()
        {
            if (quad == null) { Ensure(); Apply(); }
        }

        void Ensure()
        {
            if (quad != null || fadeMaterial == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Fondu ecran";
            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.2f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(1.5f, 1.2f, 1f);
            quad = go.GetComponent<Renderer>();
            quad.sharedMaterial = fadeMaterial;
            quad.shadowCastingMode = ShadowCastingMode.Off;
            quad.receiveShadows = false;
            mpb = new MaterialPropertyBlock();
        }

        void Apply()
        {
            if (quad == null) return;
            quad.enabled = alpha > 0.001f;
            var c = color;
            c.a = alpha;
            quad.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            quad.SetPropertyBlock(mpb);
        }

        public void FadeIn(float duration) { StopAllCoroutines(); StartCoroutine(FadeTo(0f, duration, 0f)); }
        public void FadeOut(float duration) { StopAllCoroutines(); StartCoroutine(FadeTo(1f, duration, 0f)); }

        /// <summary>Petit assombrissement puis retour (transition entre deux grandes étapes).</summary>
        [ContextMenu("Assombrissement (test)")]
        public void Dip() => Dip(0.75f, 0.6f, 0.3f, 0.9f);

        public void Dip(float depth, float down, float hold, float up)
        {
            StopAllCoroutines();
            StartCoroutine(DipRoutine(depth, down, hold, up));
        }

        IEnumerator DipRoutine(float depth, float down, float hold, float up)
        {
            yield return FadeTo(depth, down, 0f);
            yield return new WaitForSeconds(hold);
            yield return FadeTo(0f, up, 0f);
        }

        IEnumerator FadeTo(float target, float duration, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            float from = alpha, t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, duration));
                alpha = Mathf.Lerp(from, target, k);
                Apply();
                yield return null;
            }
            alpha = target;
            Apply();
        }
    }
}
