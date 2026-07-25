using System.Text;

namespace Echora.Api.ModelRuntime;

/// <summary>按完整 SSE 行移除 MiMo 后续 Tool 增量中的空身份字段。</summary>
internal sealed class MimoSseNullFixStream(Stream inner) : Stream
{
    private readonly byte[] _source = new byte[8192];
    private readonly MemoryStream _line = new();
    private byte[]? _pending;
    private int _sourceOffset;
    private int _sourceCount;
    private int _pendingOffset;

    /// <inheritdoc />
    public override bool CanRead => true;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!EnsurePending()) return 0;
        return Drain(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadIntoAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ReadIntoAsync(buffer, cancellationToken);

    /// <summary>同步读取到至少一个完整 SSE 行。</summary>
    private bool EnsurePending()
    {
        if (HasPending) return true;
        while (true)
        {
            if (TryCompleteLine()) return true;
            _sourceCount = inner.Read(_source, 0, _source.Length);
            _sourceOffset = 0;
            if (_sourceCount != 0) continue;
            return CompleteLastLine();
        }
    }

    /// <summary>异步读取到至少一个完整 SSE 行。</summary>
    private async ValueTask<bool> EnsurePendingAsync(CancellationToken cancellationToken)
    {
        if (HasPending) return true;
        while (true)
        {
            if (TryCompleteLine()) return true;
            _sourceCount = await inner.ReadAsync(_source.AsMemory(), cancellationToken);
            _sourceOffset = 0;
            if (_sourceCount != 0) continue;
            return CompleteLastLine();
        }
    }

    /// <summary>从当前网络块读取一个完整行；不完整内容留到下一块。</summary>
    private bool TryCompleteLine()
    {
        if (_sourceOffset >= _sourceCount) return false;
        var remaining = _source.AsSpan(_sourceOffset, _sourceCount - _sourceOffset);
        var newline = remaining.IndexOf((byte)'\n');
        if (newline < 0)
        {
            _line.Write(remaining);
            _sourceOffset = _sourceCount;
            return false;
        }

        _line.Write(remaining[..(newline + 1)]);
        _sourceOffset += newline + 1;
        return NormalizeLine();
    }

    /// <summary>流结束时交付最后一行。</summary>
    private bool CompleteLastLine()
    {
        if (_line.Length == 0) return false;
        return NormalizeLine();
    }

    /// <summary>保留参数增量，只把 OpenAI 流式协议中本应省略的 null 字段移除。</summary>
    private bool NormalizeLine()
    {
        var text = Encoding.UTF8.GetString(_line.GetBuffer(), 0, checked((int)_line.Length));
        _line.SetLength(0);
        text = text
            .Replace("\"index\":null", "\"index\":0", StringComparison.Ordinal)
            .Replace("\"id\":null,", string.Empty, StringComparison.Ordinal)
            .Replace(",\"name\":null", string.Empty, StringComparison.Ordinal);
        _pending = Encoding.UTF8.GetBytes(text);
        _pendingOffset = 0;
        return true;
    }

    /// <summary>异步读取修正后的内容。</summary>
    private async ValueTask<int> ReadIntoAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!await EnsurePendingAsync(cancellationToken)) return 0;
        return Drain(buffer.Span);
    }

    /// <summary>复制一段待交付内容。</summary>
    private int Drain(Span<byte> destination)
    {
        var count = Math.Min(destination.Length, _pending!.Length - _pendingOffset);
        _pending.AsSpan(_pendingOffset, count).CopyTo(destination);
        _pendingOffset += count;
        if (_pendingOffset == _pending.Length)
        {
            _pending = null;
            _pendingOffset = 0;
        }
        return count;
    }

    private bool HasPending => _pending is not null && _pendingOffset < _pending.Length;

    /// <inheritdoc />
    public override void Flush() { }
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _line.Dispose();
            inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
