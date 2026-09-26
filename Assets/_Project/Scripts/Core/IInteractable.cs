namespace TheDeep.Core
{
    /// <summary>Anything the player can look at and press E on.</summary>
    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract(Player.PlayerInteractor interactor);
        void Interact(Player.PlayerInteractor interactor);
    }

    /// <summary>
    /// An interactable that also works just by being close to it (its trigger around your head),
    /// for things that are awkward to aim at, like the hatch above a diver.
    /// </summary>
    public interface IProximityInteractable : IInteractable { }
}
