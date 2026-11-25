using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BrainVR
{
    /// <summary>
    /// Orbe flottante (sans gravité) qu'on peut attraper en VR.
    /// Quand on la relâche, elle se transforme en neurone.
    /// </summary>
    public class NeuronOrb : MonoBehaviour
    {
        public BrainEnvironment env;
        public NeuronSpawnerPanel panel;
        public float transformDuration = 1.3f;

        XRGrabInteractable grab;
        float baseScale;
        bool transforming;

        public static NeuronOrb Create(BrainEnvironment env, NeuronSpawnerPanel panel, Vector3 pos, float radius, Material mat)
        {
            var go = new GameObject("Orbe neuronale");
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * radius * 2f;

            go.AddComponent<MeshFilter>().sharedMesh = env.pulseMesh; // sphère de rayon 0.5
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            NeuronMeshFactory.ConfigureRenderer(mr);

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.5f;

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true; // flotte exactement où on la lâche
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var g = go.AddComponent<XRGrabInteractable>();
            g.movementType = XRBaseInteractable.MovementType.Instantaneous;
            g.throwOnDetach = false;
            g.useDynamicAttach = true;

            // Petite lueur
            var lightGo = new GameObject("Lueur");
            lightGo.transform.SetParent(go.transform, false);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.6f, 0.9f, 1f);
            l.intensity = 2f;
            l.range = 1.5f;
            l.shadows = LightShadows.None;

            var orb = go.AddComponent<NeuronOrb>();
            orb.env = env;
            orb.panel = panel;
            return orb;
        }

        void Start()
        {
            grab = GetComponent<XRGrabInteractable>();
            baseScale = transform.localScale.x;
            grab.selectExited.AddListener(OnReleased);
        }

        void Update()
        {
            if (transforming) return;
            // Respiration lumineuse
            transform.localScale = Vector3.one * baseScale * (1f + 0.08f * Mathf.Sin(Time.time * 4f));
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (args.isCanceled || transforming) return;
            StartCoroutine(WaitThenTransform());
        }

        public void ForceTransform()
        {
            if (!transforming) StartCoroutine(TransformIntoNeuron());
        }

        IEnumerator WaitThenTransform()
        {
            // Laisse le temps de passer l'orbe d'une main à l'autre
            yield return new WaitForSeconds(0.35f);
            if (transforming || grab == null || grab.isSelected) yield break;
            StartCoroutine(TransformIntoNeuron());
        }

        IEnumerator TransformIntoNeuron()
        {
            transforming = true;
            if (grab == null) grab = GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                grab.selectExited.RemoveListener(OnReleased);
                Destroy(grab);
            }
            yield return null;
            var col = GetComponent<Collider>();
            if (col) Destroy(col);
            var rb = GetComponent<Rigidbody>();
            if (rb) Destroy(rb);

            Vector3 pos = transform.position;
            var neuron = env.CreateNeuronObject(pos);
            float startScale = transform.localScale.x;
            var light = GetComponentInChildren<Light>();

            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / transformDuration);
                neuron.transform.localScale = Vector3.one * Mathf.Max(0f, EaseOutBack(t));
                float shrink = Mathf.Clamp01(1f - t * 1.4f);
                transform.localScale = Vector3.one * startScale * (1f + 0.6f * Mathf.Sin(t * Mathf.PI)) * shrink;
                if (light) light.intensity = 2f + 6f * Mathf.Sin(t * Mathf.PI);
                yield return null;
            }
            neuron.transform.localScale = Vector3.one;

            env.RegisterNeuron(neuron.GetComponent<MeshRenderer>(), pos);
            if (panel) panel.OnNeuronCreated();
            Destroy(gameObject);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }
}
