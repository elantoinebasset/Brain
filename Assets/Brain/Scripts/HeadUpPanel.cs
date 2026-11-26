using UnityEngine;

namespace BrainVR
{
    /// <summary>
    /// Garde le panneau en haut du champ de vision, en suivant la tête avec un léger retard
    /// (plus confortable en VR qu'un panneau collé à l'écran).
    /// </summary>
    public class HeadUpPanel : MonoBehaviour
    {
        [Tooltip("Distance du panneau devant les yeux (m)")]
        public float distance = 1.3f;
        [Tooltip("Angle au-dessus du centre du regard (degrés). Plus grand = plus haut")]
        public float angleAboveCenter = 18f;
        [Tooltip("Vitesse à laquelle le panneau rattrape les mouvements de tête")]
        public float followSpeed = 5f;

        void OnEnable()
        {
            if (TryGetTarget(out var pos, out var rot)) transform.SetPositionAndRotation(pos, rot);
        }

        void LateUpdate()
        {
            if (!TryGetTarget(out var pos, out var rot)) return;
            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, pos, k),
                Quaternion.Slerp(transform.rotation, rot, k));
        }

        bool TryGetTarget(out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = default;
            var cam = Camera.main;
            if (cam == null) return false;
            var head = cam.transform;
            Vector3 dir = Quaternion.AngleAxis(-angleAboveCenter, head.right) * head.forward;
            pos = head.position + dir * distance;
            rot = Quaternion.LookRotation(pos - head.position, head.up); // le canvas se lit depuis son côté -Z
            return true;
        }
    }
}
