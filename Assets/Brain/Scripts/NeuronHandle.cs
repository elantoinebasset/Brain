using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BrainVR
{
    /// <summary>
    /// Rend un neurone attrapable : quand on l'attrape, il redevient une orbe
    /// (directement dans la main) et perd ses connexions.
    /// </summary>
    public class NeuronHandle : MonoBehaviour
    {
        public BrainEnvironment env;
        public int index;

        XRSimpleInteractable simple;
        bool converting;

        public static void Attach(BrainEnvironment env, GameObject go, int index, float radius)
        {
            var existing = go.GetComponent<NeuronHandle>();
            if (existing != null)
            {
                existing.env = env;
                existing.index = index;
                return;
            }
            var col = go.AddComponent<SphereCollider>();
            col.center = Vector3.zero; // sinon Unity le centre sur les dendrites
            col.radius = radius;
            var simple = go.AddComponent<XRSimpleInteractable>();
            var h = go.AddComponent<NeuronHandle>();
            h.env = env;
            h.index = index;
            h.simple = simple;
            simple.selectEntered.AddListener(h.OnGrabbed);
        }

        [ContextMenu("Test : transformer en orbe")]
        void TestConvert()
        {
            if (converting) return;
            converting = true;
            StartCoroutine(ConvertToOrb(null, null));
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (converting) return;
            converting = true;
            StartCoroutine(ConvertToOrb(args.interactorObject, args.manager));
        }

        IEnumerator ConvertToOrb(IXRSelectInteractor interactor, XRInteractionManager manager)
        {
            // On sort du traitement de l'événement XRI avant de modifier les sélections
            yield return null;

            var mr = GetComponent<Renderer>();
            if (mr) mr.enabled = false;
            var orb = env.ConvertNeuronToOrb(index, transform.position);
            yield return null; // laisse l'orbe s'enregistrer auprès du manager

            if (manager != null && interactor != null)
            {
                if (simple != null && simple.isSelected) manager.SelectExit(interactor, simple);
                var grab = orb != null ? orb.GetComponent<XRGrabInteractable>() : null;
                // Si le bouton est encore tenu, l'orbe passe directement dans la main
                if (grab != null && interactor.isSelectActive && !grab.isSelected)
                    manager.SelectEnter(interactor, grab);
            }

            Destroy(gameObject);
        }
    }
}
