using TheDeep.Core;
using TheDeep.Player;
using UnityEngine;

namespace TheDeep.Footage
{
    /// <summary>
    /// The slot next to the terminal: put camera chips in here (your own helmet camera's, or ones
    /// recovered from bodies) to watch them in the Footage app.
    /// </summary>
    public class ChipReader : MonoBehaviour, IInteractable
    {
        [SerializeField] Renderer busyLight;
        float busyUntil;

        public string Prompt => "Insert camera chip(s)";

        public bool CanInteract(PlayerInteractor interactor)
        {
            var camera = interactor.GetComponent<HelmetCamera>();
            return (camera != null && camera.HasFootage) || CarriedCount(interactor) > 0;
        }

        public void Interact(PlayerInteractor interactor)
        {
            var archive = FootageArchive.Instance;
            if (archive == null) return;
            var clip = interactor.GetComponent<HelmetCamera>()?.TakeChip(endsInDeath: false);
            if (clip != null) archive.Submit(archive.NewChipId(), clip, ChipStatus.Inserted);
            archive.InsertCarriedRpc();
            busyUntil = Time.time + 2f;
        }

        static int CarriedCount(PlayerInteractor interactor)
        {
            var archive = FootageArchive.Instance;
            if (archive == null || !archive.IsSpawned) return 0;
            int count = 0;
            ulong me = interactor.GetComponent<Unity.Netcode.NetworkObject>().OwnerClientId;
            foreach (var c in archive.Chips)
                if (c.Status == ChipStatus.Carried && c.Carrier == me) count++;
            return count;
        }

        void Update()
        {
            if (busyLight != null) busyLight.enabled = Time.time < busyUntil ? Mathf.Repeat(Time.time * 8f, 1f) < 0.5f : true;
        }
    }
}
