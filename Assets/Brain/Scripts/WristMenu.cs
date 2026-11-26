using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Hands;

namespace BrainVR
{
    /// <summary>
    /// Menu au poignet : le panneau apparaît au-dessus de la main quand on tourne
    /// la paume vers son visage, et se replie quand on baisse la main.
    /// Marche en hand tracking (XR Hands) et, à défaut, avec la manette.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class WristMenu : MonoBehaviour
    {
        public Handedness hand = Handedness.Left;
        [Tooltip("Utilisé quand les mains ne sont pas suivies (manettes)")]
        public Transform controllerFallback;

        [Header("Placement")]
        [Tooltip("Échelle du canvas quand il est affiché (0.00035 = panneau de 21 cm)")]
        public float shownScale = 0.00035f;
        public float heightAbovePalm = 0.14f;
        public float distanceFromPalm = 0.04f;
        public float followSmoothing = 18f;

        [Header("Détection de la paume")]
        [Range(0f, 1f)] public float showThreshold = 0.6f;
        [Range(0f, 1f)] public float hideThreshold = 0.4f;
        public float animSpeed = 8f;

        Canvas canvas;
        BaseRaycaster[] raycasters;
        XROrigin origin;
        XRHandSubsystem handSubsystem;
        static readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        bool wantVisible;
        float visibility;
        bool placedOnce;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            raycasters = GetComponents<BaseRaycaster>();
            origin = FindFirstObjectByType<XROrigin>();
            SetVisibility(0f);
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;

            bool tracked = TryGetPalm(out Vector3 palmPos, out Vector3 palmNormal);
            if (tracked)
            {
                Vector3 toHead = (cam.transform.position - palmPos).normalized;
                float facing = Vector3.Dot(palmNormal, toHead);
                if (facing > showThreshold) wantVisible = true;
                else if (facing < hideThreshold) wantVisible = false;

                Vector3 targetPos = palmPos + palmNormal * distanceFromPalm + Vector3.up * heightAbovePalm;
                Vector3 look = targetPos - cam.transform.position;
                look.y *= 0.5f; // garde le panneau assez droit
                Quaternion targetRot = Quaternion.LookRotation(look.normalized, Vector3.up);

                if (!placedOnce || visibility <= 0.01f)
                {
                    transform.SetPositionAndRotation(targetPos, targetRot);
                    placedOnce = true;
                }
                else
                {
                    float k = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
                    transform.SetPositionAndRotation(
                        Vector3.Lerp(transform.position, targetPos, k),
                        Quaternion.Slerp(transform.rotation, targetRot, k));
                }
            }
            else
            {
                wantVisible = false;
            }

            float target = wantVisible ? 1f : 0f;
            if (!Mathf.Approximately(visibility, target))
                SetVisibility(Mathf.MoveTowards(visibility, target, animSpeed * Time.deltaTime));
        }

        void SetVisibility(float v)
        {
            visibility = v;
            float eased = v * v * (3f - 2f * v);
            transform.localScale = Vector3.one * shownScale * Mathf.Max(0.0001f, eased);
            bool on = v > 0.01f;
            if (canvas.enabled != on) canvas.enabled = on;
            foreach (var r in raycasters) r.enabled = v > 0.9f; // cliquable seulement une fois bien ouvert
        }

        bool TryGetPalm(out Vector3 pos, out Vector3 normal)
        {
            pos = default;
            normal = default;

            // 1) Hand tracking
            if (handSubsystem == null || !handSubsystem.running)
            {
                handSubsystem = null;
                SubsystemManager.GetSubsystems(subsystems);
                foreach (var s in subsystems)
                    if (s.running) { handSubsystem = s; break; }
            }
            if (handSubsystem != null)
            {
                var h = hand == Handedness.Left ? handSubsystem.leftHand : handSubsystem.rightHand;
                if (h.isTracked && h.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose pose))
                {
                    Transform space = origin != null && origin.CameraFloorOffsetObject != null
                        ? origin.CameraFloorOffsetObject.transform
                        : (origin != null ? origin.transform : null);
                    if (space != null) pose = pose.GetTransformedBy(space);
                    pos = pose.position;
                    normal = -pose.up; // le dos de la main est +up
                    return true;
                }
            }

            // 2) Manette : la paume regarde vers +X (gauche) / -X (droite) de la manette
            if (controllerFallback != null && controllerFallback.gameObject.activeInHierarchy)
            {
                pos = controllerFallback.position;
                normal = hand == Handedness.Left ? controllerFallback.right : -controllerFallback.right;
                return true;
            }
            return false;
        }
    }
}
