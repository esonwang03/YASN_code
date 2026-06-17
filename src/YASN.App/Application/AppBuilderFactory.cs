using Avalonia;

namespace YASN.Application
{
    /// <summary>
    /// Creates configured Avalonia app builders for the desktop shell.
    /// </summary>
    public static class AppBuilderFactory
    {
        /// <summary>
        /// Creates the standard app builder used by production startup and tests.
        /// </summary>
        public static AppBuilder Create()
        {
            return AppBuilder.Configure<YasnApplication>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}
