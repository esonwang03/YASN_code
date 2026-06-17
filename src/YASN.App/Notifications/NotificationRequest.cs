namespace YASN.Notifications
{
    /// <summary>
    /// Describes a desktop notification request.
    /// </summary>
    public sealed record NotificationRequest(string Title, string Body, string ActivationArgument);
}
