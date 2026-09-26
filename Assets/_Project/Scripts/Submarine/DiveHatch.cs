using TheDeep.Core;
using TheDeep.Player;
using UnityEngine;

namespace TheDeep.Submarine
{
    /// <summary>
    /// One side of the dive hatch. Inside (<see cref="Side.Cabin"/>): "Dive" drops you into the water.
    /// Outside under the hull (<see cref="Side.Water"/>): "Climb aboard" brings you back in.
    /// </summary>
    public class DiveHatch : MonoBehaviour, IProximityInteractable
    {
        public enum Side { Cabin, Water }

        [SerializeField] Side side;
        [SerializeField, Tooltip("Where you end up on the other side.")] Transform destination;

        public string Prompt => side == Side.Cabin ? "Dive" : "Climb aboard";

        public bool CanInteract(PlayerInteractor interactor)
        {
            var diver = interactor.GetComponent<DiverController>();
            return diver != null && diver.IsDiving == (side == Side.Water);
        }

        public void Interact(PlayerInteractor interactor)
        {
            var diver = interactor.GetComponent<DiverController>();
            if (side == Side.Cabin) diver.EnterWater(destination);
            else diver.ExitWater(destination);
        }
    }
}
