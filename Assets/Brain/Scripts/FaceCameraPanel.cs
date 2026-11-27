using UnityEngine;

namespace BrainVR
{
    /// <summary>
    /// Le panneau reste à sa place mais se tourne toujours vers le joueur.
    /// À mettre sur l'objet racine du panneau (Canvas World Space ou objet 3D).
    /// </summary>
    public class FaceCameraPanel : MonoBehaviour
    {
        [Tooltip("Vrai = ne tourne que sur l'axe vertical (reste droit). Faux = s'incline aussi vers le haut / le bas")]
        public bool onlyVertical = true;
        [Tooltip("Vitesse de rotation (0 = instantané)")]
        public float smoothing = 8f;
        [Tooltip("Coché pour un Canvas (lisible depuis son côté -Z). Décoché pour un objet 3D dont l'avant est +Z")]
        public bool isCanvas = true;
        [Tooltip("Correction si le panneau est dans le mauvais sens (degrés, ex. Y = 180)")]
        public Vector3 rotationOffset = Vector3.zero;

        Transform cam;

        void OnEnable() => Turn(true);

        void LateUpdate() => Turn(false);

        void Turn(bool instant)
        {
            if (cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                cam = c.transform;
            }

            Vector3 dir = isCanvas ? transform.position - cam.position : cam.position - transform.position;
            if (onlyVertical) dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;

            Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(rotationOffset);
            if (instant || smoothing <= 0f) transform.rotation = target;
            else transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        }
    }
}
