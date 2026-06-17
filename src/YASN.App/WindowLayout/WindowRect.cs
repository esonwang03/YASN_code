namespace YASN.WindowLayout
{
    /// <summary>
    /// Describes a platform-independent window rectangle.
    /// </summary>
    /// <param name="Left">The left coordinate.</param>
    /// <param name="Top">The top coordinate.</param>
    /// <param name="Width">The rectangle width.</param>
    /// <param name="Height">The rectangle height.</param>
    public sealed record WindowRect(double Left, double Top, double Width, double Height);
}
