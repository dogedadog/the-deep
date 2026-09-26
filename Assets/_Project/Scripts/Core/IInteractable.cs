namespace TheDeep.Core
{
    /// <summary>Anything the player can look at and press E on.</summary>
    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract(Player.PlayerInteractor interactor);
        void Interact(Player.PlayerInteractor interactor);
    }
}
