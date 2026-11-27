using UnityEngine;

namespace BrainVR
{

    public class HeadUpPanel : MonoBehaviour
    {
        [Tooltip("Distance du panneau devant les yeux (m)")]
        public float distance = 1.3f;
        [Tooltip("Angle au-dessus du centre du regard (degrés). Plus grand = plus haut")]
        public float angleAboveCenter = 18f;
        [Tooltip("Angle à gauche du centre du regard (degrés). Négatif = à droite")]
        public float angleLeftOfCenter = 0f;
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

        public void SetFromWorldPosition(Vector3 worldPos)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var head = cam.transform;
            Vector3 v = worldPos - head.position;
            if (v.sqrMagnitude < 1e-4f) return;
            distance = Mathf.Max(0.3f, v.magnitude);
            Vector3 l = head.InverseTransformDirection(v.normalized);
            angleAboveCenter = Mathf.Asin(Mathf.Clamp(l.y, -1f, 1f)) * Mathf.Rad2Deg;
            angleLeftOfCenter = Mathf.Atan2(-l.x, l.z) * Mathf.Rad2Deg;
        }

        bool TryGetTarget(out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = default;
            var cam = Camera.main;
            if (cam == null) return false;
            var head = cam.transform;
            Vector3 dir = Quaternion.AngleAxis(-angleLeftOfCenter, head.up)
                        * Quaternion.AngleAxis(-angleAboveCenter, head.right) * head.forward;
            pos = head.position + dir * distance;
            rot = Quaternion.LookRotation(pos - head.position, head.up);
            return true;
        }
    }
}
