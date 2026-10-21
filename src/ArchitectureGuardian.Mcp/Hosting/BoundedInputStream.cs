namespace ArchitectureGuardian.Mcp.Hosting;

/// <summary>Bounds each newline-delimited stdio frame before the SDK buffers or parses the whole message.</summary>
internal sealed class BoundedInputStream(Stream input) : Stream
{
    private int _frameBytes;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await input.ReadAsync(buffer, cancellationToken);
        Check(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = input.Read(buffer, offset, count);
        Check(buffer.AsSpan(offset, read));
        return read;
    }

    private void Check(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value == (byte)'\n')
            {
                _frameBytes = 0;
            }
            else if (++_frameBytes > ServerOptions.MaxMessageBytes)
            {
                Console.Error.WriteLine("MCP input frame exceeds the 1 MiB limit; closing the session.");
                throw new IOException("MCP input frame exceeds the 1 MiB limit; closing the session.");
            }
        }
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
