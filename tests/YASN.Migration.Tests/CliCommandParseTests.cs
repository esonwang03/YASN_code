using YASN.Cli;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies that <see cref="CliCommand.Parse"/> maps raw arguments to the right verb, note id, and
    /// options for the new CLI verbs, and reports usage errors. The IPC layer depends on this grammar,
    /// so these tests pin the contract rather than incidental behavior.
    /// </summary>
    public sealed class CliCommandParseTests
    {
        [Fact]
        public void GlanceParsesNoteIdAndLineRange()
        {
            CliCommand command = CliCommand.Parse(["note", "glance", "--note-id", "abc", "--lines", "2-4"]);

            Assert.Equal(CliVerb.NoteGlance, command.Verb);
            Assert.Equal("abc", command.NoteId);
            Assert.NotNull(command.Options);
            Assert.Equal("2-4", command.Options!["lines"]);
        }

        [Fact]
        public void EditParsesAppendFlagAndText()
        {
            CliCommand command = CliCommand.Parse(["note", "edit", "--note-id", "abc", "--append", "--text", "hi"]);

            Assert.Equal(CliVerb.NoteEdit, command.Verb);
            Assert.True(command.Options!.ContainsKey("append"));
            Assert.Equal("hi", command.Options!["text"]);
        }

        [Fact]
        public void LayoutParsesScreenAndCorners()
        {
            CliCommand command = CliCommand.Parse(
                ["note", "layout", "--note-id", "abc", "--screen", "1", "--lt", "10,20", "--rb", "300,400"]);

            Assert.Equal(CliVerb.NoteLayout, command.Verb);
            Assert.Equal("1", command.Options!["screen"]);
            Assert.Equal("10,20", command.Options!["lt"]);
            Assert.Equal("300,400", command.Options!["rb"]);
        }

        [Fact]
        public void ListScreensParses()
        {
            CliCommand command = CliCommand.Parse(["list", "screens"]);

            Assert.Equal(CliVerb.ListScreens, command.Verb);
        }

        [Fact]
        public void UnknownListSubcommandIsUsageError()
        {
            CliCommand command = CliCommand.Parse(["list", "bogus"]);

            Assert.Equal(CliVerb.None, command.Verb);
            Assert.NotNull(command.Error);
        }

        [Fact]
        public void NoteEditWithoutNoteIdIsUsageError()
        {
            CliCommand command = CliCommand.Parse(["note", "edit", "--append"]);

            Assert.Equal(CliVerb.None, command.Verb);
            Assert.NotNull(command.Error);
        }

        [Fact]
        public void LegacyOpenStillParses()
        {
            CliCommand command = CliCommand.Parse(["note", "open", "--note-id", "abc"]);

            Assert.Equal(CliVerb.NoteOpen, command.Verb);
            Assert.Equal("abc", command.NoteId);
        }
    }
}
