namespace YASN.Infrastructure.Sync.WebDav
{
    public class WebDavOptions
    {
        public string ServerUrl { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public bool AllowInvalidCertificates { get; init; } = true;
    }
}
