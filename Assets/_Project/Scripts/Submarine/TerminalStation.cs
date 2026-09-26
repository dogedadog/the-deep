using System;
using System.Collections;
using TheDeep.Core;
using TheDeep.Player;
using TheDeep.UI.Terminal;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDeep.Submarine
{
    /// <summary>
    /// The physical computer desk in the sub. Pressing E moves the player's camera in front of
    /// the screen and frees the mouse so they can click the in-world desktop. Esc leaves.
    /// </summary>
    public class TerminalStation : MonoBehaviour, IInteractable
    {
        [SerializeField] Transform viewPoint;
        [SerializeField] TerminalOS os;
        [SerializeField] float transitionTime = 0.35f;

        PlayerInteractor user;
        bool transitioning;

        public string Prompt => "Use Terminal";
        public bool CanInteract(PlayerInteractor interactor) => user == null && !transitioning;

        void OnEnable() => os.LogOffRequested += Exit;
        void OnDisable() => os.LogOffRequested -= Exit;

        public void Interact(PlayerInteractor interactor)
        {
            user = interactor;
            interactor.Controller.InputLocked = true;
            os.SetEventCamera(interactor.Camera);
            Transform cam = interactor.Camera.transform;
            StartCoroutine(MoveCamera(cam, () => new Pose(viewPoint.position, viewPoint.rotation), () =>
            {
                FirstPersonController.SetCursorLocked(false);
                os.SetInteractive(true);
            }));
        }

        void Update()
        {
            if (user != null && os.IsInteractive && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Exit();
        }

        public void Exit()
        {
            if (user == null || transitioning) return;
            PlayerInteractor leaving = user;
            os.SetInteractive(false);
            FirstPersonController.SetCursorLocked(true);
            Transform cam = leaving.Camera.transform;
            StartCoroutine(MoveCamera(cam, () => new Pose(cam.parent.position, cam.parent.rotation), () =>
            {
                cam.localPosition = Vector3.zero;
                cam.localRotation = Quaternion.identity;
                leaving.Controller.InputLocked = false;
                user = null;
            }));
        }

        IEnumerator MoveCamera(Transform cam, Func<Pose> target, Action done)
        {
            transitioning = true;
            Vector3 startPos = cam.position;
            Quaternion startRot = cam.rotation;
            for (float t = 0f; t < 1f; t += Time.deltaTime / transitionTime)
            {
                Pose goal = target();
                float s = Mathf.SmoothStep(0f, 1f, t);
                cam.SetPositionAndRotation(Vector3.Lerp(startPos, goal.position, s), Quaternion.Slerp(startRot, goal.rotation, s));
                yield return null;
            }
            Pose end = target();
            cam.SetPositionAndRotation(end.position, end.rotation);
            transitioning = false;
            done();
        }
    }
}
