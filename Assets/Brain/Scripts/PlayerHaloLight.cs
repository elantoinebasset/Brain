using UnityEngine;

namespace BrainVR
{

    [RequireComponent(typeof(Light))]
    public class PlayerHaloLight : MonoBehaviour
    {
        public float heightAbove = 0.8f;
        public float targetIntensity = 2.5f;
        public float fadeDuration = 4f;

        Light lightComp;
        float t = -1f;

        void Awake()
        {
            lightComp = GetComponent<Light>();
            lightComp.enabled = false;
        }

        public void TurnOn()
        {
            lightComp.enabled = true;
            lightComp.intensity = 0f;
            t = 0f;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam) transform.position = cam.transform.position + Vector3.up * heightAbove;
            if (t < 0f) return;
            t += Time.deltaTime;
            lightComp.intensity = targetIntensity * Mathf.Clamp01(t / Mathf.Max(0.01f, fadeDuration));
            if (t >= fadeDuration) t = -1f;
        }
    }
}
