using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BrainVR
{
    /// <summary>
    /// Poignée sous un panneau : on l'attrape (grip, main ou rayon) pour déplacer le panneau.
    /// Le panneau reste tourné vers le joueur. Au lâcher :
    /// - HeadUpPanel : garde la nouvelle place dans le champ de vision ;
    /// - CalloutPanelFollow : garde le nouveau décalage par rapport à la manette ;
    /// - sinon : reste là où on l'a posé.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class PanelGrabHandle : MonoBehaviour
    {
        public Transform panel;
        public Image visual;
        public Color normalColor = new Color(1f, 1f, 1f, 0.55f);
        public Color hoverColor = new Color(1f, 1f, 1f, 0.95f);
        public Color grabColor = new Color(0f, 0.83f, 1f, 1f);

        XRSimpleInteractable interactable;
        IXRSelectInteractor grabber;
        Vector3 localOffset;
        HeadUpPanel headUp;
        CalloutPanelFollow callout;
        bool headUpWasEnabled, calloutWasEnabled;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (panel == null) panel = transform.parent;
            headUp = panel.GetComponent<HeadUpPanel>();
            callout = panel.GetComponent<CalloutPanelFollow>();
            interactable.hoverEntered.AddListener(_ => Refresh());
            interactable.hoverExited.AddListener(_ => Refresh());
            interactable.selectEntered.AddListener(OnGrab);
            interactable.selectExited.AddListener(OnRelease);
            Refresh();
            // La poignée ne doit jamais bloquer le déplacement du joueur
            var col = GetComponent<Collider>();
            if (col != null)
                foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
                    Physics.IgnoreCollision(col, cc, true);
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            grabber = args.interactorObject;
            var attach = grabber.GetAttachTransform(interactable);
            localOffset = attach.InverseTransformPoint(panel.position);
            if (headUp) { headUpWasEnabled = headUp.enabled; headUp.enabled = false; }
            if (callout) { calloutWasEnabled = callout.enabled; callout.enabled = false; }
            Refresh();
        }

        void OnRelease(SelectExitEventArgs args)
        {
            if (args.interactorObject != grabber) return;
            grabber = null;
            if (headUp)
            {
                headUp.SetFromWorldPosition(panel.position);
                headUp.enabled = headUpWasEnabled;
            }
            if (callout)
            {
                callout.SetFromWorldPosition(panel.position);
                callout.enabled = calloutWasEnabled;
            }
            Refresh();
        }

        void LateUpdate()
        {
            if (grabber == null) return;
            var attach = grabber.GetAttachTransform(interactable);
            if (attach == null) return;
            Vector3 pos = attach.TransformPoint(localOffset);
            Quaternion rot = panel.rotation;
            var cam = Camera.main;
            if (cam)
            {
                Vector3 look = pos - cam.transform.position;
                if (look.sqrMagnitude > 1e-4f) rot = Quaternion.LookRotation(look.normalized, Vector3.up); // canvas lisible depuis -Z
            }
            panel.SetPositionAndRotation(pos, rot);
        }

        void Refresh()
        {
            if (!visual || interactable == null) return;
            visual.color = grabber != null ? grabColor : interactable.isHovered ? hoverColor : normalColor;
        }
    }
}
