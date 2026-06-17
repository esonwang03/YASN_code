using YASN.Localization;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies runtime-switchable localized strings.
    /// </summary>
    public sealed class LocalizationServiceTests
    {
        /// <summary>
        /// Returns English strings and raises change notifications when culture changes.
        /// </summary>
        [Fact]
        public void SetCultureUpdatesCurrentCultureAndRaisesEvent()
        {
            LocalizationService service = new LocalizationService();
            int changes = 0;
            service.CultureChanged += (_, _) => changes += 1;

            service.SetCulture("en");

            Assert.Equal("en", service.CurrentCulture);
            Assert.Equal("Open note", service["Menu.OpenNote"]);
            Assert.Equal(1, changes);
        }

        /// <summary>
        /// Raises an indexer change notification so {l:Tr} bindings relocalize live.
        /// </summary>
        [Fact]
        public void StringsIndexerNotifiesOnCultureChange()
        {
            LocalizationService service = new LocalizationService();
            int indexerChanges = 0;
            service.Strings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == "Item[]")
                {
                    indexerChanges += 1;
                }
            };

            service.SetCulture("en");

            Assert.Equal("Exit", service.Strings["Menu.Exit"]);
            Assert.Equal(1, indexerChanges);
        }
    }
}
