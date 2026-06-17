namespace YASN.SingleNote
{
    /// <summary>
    /// Represents the one persisted note used by the phase-two tracer bullet.
    /// </summary>
    public sealed record SingleNoteDocument(int Id, string Content);
}
