namespace SIHKH.Rail
{
    /// <summary>
    /// Something sitting in a cart seat that can move the cart. Lets the rail code
    /// read player input without knowing what a player is.
    /// </summary>
    public interface ICartDriver
    {
        /// <summary>-1 strafe left, 0 hold, +1 strafe right, from the seat's point of view.</summary>
        float Strafe { get; }

        /// <summary>Dug in: the cart refuses to move, so the chain must drag nothing or snap.</summary>
        bool Planted { get; }
    }
}
