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

        bool TryGetTargetPose(out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = default;
            var cam = Camera.main;
            if (target == null || cam == null) return false;
            Vector3 camRight = cam.transform.right;
            pos = target.position + Vector3.up * height - camRight * side;
            Vector3 look = pos - cam.transform.position;
            look.y *= 0.5f;
            rot = Quaternion.LookRotation(look.normalized, Vector3.up); // le canvas se lit depuis son côté -Z
            return true;
        }
    }
}
