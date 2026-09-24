using System;
using System.IO;
using System.Text;

namespace PyNavis.Runtime.Output
{
    /// <summary>
    /// Write-only Stream that decodes into a TextWriter. IronPython's sys.stdout writes
    /// BYTES to the stream half of ScriptIO.SetOutput - a plain TextWriter never sees
    /// them - so script output capture must go through a stream like this one.
    /// Uses a stateful Decoder so multi-byte characters split across writes survive.
    /// </summary>
    public sealed class TextWriterStream : Stream
    {
        private readonly TextWriter _writer;
        private readonly Decoder _decoder;

        public TextWriterStream(TextWriter writer, Encoding encoding)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _decoder = (encoding ?? Encoding.UTF8).GetDecoder();
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override void Write(byte[] buffer, int offset, int count)
        {
            var chars = new char[_decoder.GetCharCount(buffer, offset, count)];
            var n = _decoder.GetChars(buffer, offset, count, chars, 0);
            if (n > 0) _writer.Write(chars, 0, n);
        }

        public override void Flush() => _writer.Flush();

        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
