using UnityEngine;

namespace BrainVR
{
    /// <summary>
    /// Fait suivre le panneau à la manette gauche quand il est affiché comme infobulle
    /// (le Callout du template l'active quand on regarde la manette, et le désactive sinon).
    /// </summary>
    public class CalloutPanelFollow : MonoBehaviour
    {
        [Tooltip("Point d'ancrage (le Callout placé sur la manette gauche)")]
        public Transform target;
        [Tooltip("Hauteur du panneau au-dessus de la manette (m)")]
        public float height = 0.15f;
        [Tooltip("Décalage vers la gauche, vu du joueur (m)")]
        public float side = 0.02f;
        [Tooltip("Décalage vers l'avant, vu du joueur (m)")]
        public float forward = 0f;
        public float followSmoothing = 14f;

        void OnEnable()
        {
            if (TryGetTargetPose(out var pos, out var rot)) transform.SetPositionAndRotation(pos, rot);
        }

        void LateUpdate()
        {
            // Si la manette disparaît (passage au hand tracking...), on cache le panneau
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                gameObject.SetActive(false);
                return;
            }
            if (!TryGetTargetPose(out var pos, out var rot)) return;
            float k = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, pos, k),
                Quaternion.Slerp(transform.rotation, rot, k));
        }

        /// <summary>Recalcule le décalage par rapport à la manette pour garder cette position.</summary>
        public void SetFromWorldPosition(Vector3 worldPos)
        {
            var cam = Camera.main;
            if (cam == null || target == null) return;
            Vector3 d = worldPos - target.position;
            Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            height = d.y;
            side = -Vector3.Dot(d, right);
            forward = Vector3.Dot(d, fwd);
        }

        bool TryGetTargetPose(out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = default;
            var cam = Camera.main;
            if (target == null || cam == null) return false;
            Vector3 camRight = cam.transform.right;
            Vector3 camFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            pos = target.position + Vector3.up * height - camRight * side + camFwd * forward;
            Vector3 look = pos - cam.transform.position;
            look.y *= 0.5f;
            rot = Quaternion.LookRotation(look.normalized, Vector3.up); // le canvas se lit depuis son côté -Z
            return true;
        }
    }
}
