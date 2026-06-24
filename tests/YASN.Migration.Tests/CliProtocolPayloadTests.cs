using YASN.Cli;

namespace YASN.Migration.Tests
{
    /// <summary>
    /// Verifies the Base64 payload codec that lets multi-line content (e.g. <c>note edit</c> input and
    /// <c>list screens</c> output) cross the single-line CLI wire protocol intact.
    /// </summary>
    public sealed class CliProtocolPayloadTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("single line")]
        [InlineData("line one\nline two\r\nline three")]
        [InlineData("unicode: café — 日本語 😀")]
        [InlineData("spaces  and\ttabs")]
        public void EncodeDecodeRoundTrips(string text)
        {
            string encoded = CliProtocol.EncodePayload(text);

            // The encoded token must be free of spaces and newlines so it survives the line protocol.
            Assert.DoesNotContain(' ', encoded);
            Assert.DoesNotContain('\n', encoded);
            Assert.Equal(text, CliProtocol.DecodePayload(encoded));
        }

        [Fact]
        public void EditRequestLineCarriesEncodedContent()
        {
            CliCommand command = CliCommand.Parse(["note", "edit", "--note-id", "abc", "--text", "x"]) with
            {
                Payload = "first line\nsecond line"
            };

            string? requestLine = CliProtocol.ToRequestLine(command);

            Assert.NotNull(requestLine);
            string[] parts = requestLine!.Split(' ');
            Assert.Equal("note-edit", parts[0]);
            Assert.Equal("abc", parts[1]);
            Assert.Equal("replace", parts[2]);
            Assert.Equal("first line\nsecond line", CliProtocol.DecodePayload(parts[3]));
        }

        [Fact]
        public void LayoutRequestLineUsesPlaceholdersWhenNoRectangle()
        {
            CliCommand command = CliCommand.Parse(["note", "layout", "--note-id", "abc"]);

            string? requestLine = CliProtocol.ToRequestLine(command);

            Assert.Equal("note-layout abc 0 - - - -", requestLine);
        }

        [Fact]
        public void LayoutRequestLineSplitsCorners()
        {
            CliCommand command = CliCommand.Parse(
                ["note", "layout", "--note-id", "abc", "--screen", "2", "--lt", "10,20", "--rb", "30,40"]);

            string? requestLine = CliProtocol.ToRequestLine(command);

            Assert.Equal("note-layout abc 2 10 20 30 40", requestLine);
        }
    }
}
