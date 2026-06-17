using YASN.App.Settings;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the numeric settings field parses, clamps, and round-trips through its string value.
    /// </summary>
    public sealed class SettingFieldNumberTests
    {
        /// <summary>
        /// NumberValue reads the parsed canonical string value.
        /// </summary>
        [Fact]
        public void NumberValueParsesStringValue()
        {
            SettingField field = new SettingField { FieldType = SettingFieldType.Number, Value = "42" };

            Assert.Equal(42m, field.NumberValue);
        }

        /// <summary>
        /// Setting NumberValue writes the canonical string into Value.
        /// </summary>
        [Fact]
        public void NumberValueWritesStringValue()
        {
            SettingField field = new SettingField { FieldType = SettingFieldType.Number };

            field.NumberValue = 7m;

            Assert.Equal("7", field.Value);
        }

        /// <summary>
        /// Setting NumberValue clamps to the configured minimum and maximum.
        /// </summary>
        [Fact]
        public void NumberValueClampsToBounds()
        {
            SettingField field = new SettingField
            {
                FieldType = SettingFieldType.Number,
                Minimum = 1,
                Maximum = 1024
            };

            field.NumberValue = 0m;
            Assert.Equal(1m, field.NumberValue);

            field.NumberValue = 5000m;
            Assert.Equal(1024m, field.NumberValue);
        }

        /// <summary>
        /// A non-numeric or empty value reads as zero.
        /// </summary>
        [Fact]
        public void NumberValueDefaultsToZeroWhenUnparseable()
        {
            SettingField field = new SettingField { FieldType = SettingFieldType.Number, Value = "abc" };

            Assert.Equal(0m, field.NumberValue);
        }
    }
}
