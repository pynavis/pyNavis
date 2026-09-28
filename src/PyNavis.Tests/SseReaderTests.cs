using System.Linq;
using PyNavis.Runtime.Ai;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>The Server-Sent Events framing both providers stream in.</summary>
    public class SseReaderTests
    {
        private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

        [Fact]
        public void BlankLines_DelimitEvents_AndEventNamesRideAlong()
        {
            var events = SseReader.Parse(Lines(
                "event: message_start\ndata: {\"a\":1}\n\nevent: content_block_delta\ndata: {\"b\":2}\n\n")).ToList();

            Assert.Equal(2, events.Count);
            Assert.Equal("message_start", events[0].Event);
            Assert.Equal("{\"a\":1}", events[0].Data);
            Assert.Equal("content_block_delta", events[1].Event);
            Assert.Equal("{\"b\":2}", events[1].Data);
        }

        [Fact]
        public void MultipleDataLines_JoinWithNewlines()
        {
            var only = SseReader.Parse(Lines("data: one\ndata: two\n\n")).Single();

            Assert.Equal("one\ntwo", only.Data);
            Assert.Null(only.Event);
        }

        [Fact]
        public void Comments_AndUnknownFields_AreIgnored_AndTheLastEventNeedsNoTrailingBlank()
        {
            var events = SseReader.Parse(Lines(": keep-alive\nid: 7\nretry: 100\ndata: [DONE]")).ToList();

            var only = Assert.Single(events);
            Assert.Equal("[DONE]", only.Data);
        }

        [Fact]
        public void ADataLineWithoutASpaceAfterTheColon_StillParses()
        {
            var only = SseReader.Parse(Lines("data:{\"x\":1}\n\n")).Single();

            Assert.Equal("{\"x\":1}", only.Data);
        }
    }
}
