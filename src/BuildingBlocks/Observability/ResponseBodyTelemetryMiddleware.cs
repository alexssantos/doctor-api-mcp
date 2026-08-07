using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace McpApis.BuildingBlocks.Observability;

public class ResponseBodyTelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IBodyCaptureOptions _options;

    public ResponseBodyTelemetryMiddleware(RequestDelegate next, IBodyCaptureOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalBody = context.Response.Body;

        await using var capture = new LimitedCaptureStream(originalBody, _options.MaxBodyBytes);
        context.Response.Body = capture;
        try
        {
            await _next(context);

            var mediaType = context.Response.ContentType?.Split(';', 2)[0].Trim();
            var activity = Activity.Current;
            if (activity is not null && mediaType is not null &&
                _options.AllowedContentTypes.Contains(mediaType) && capture.CapturedLength > 0)
            {
                var body = capture.GetCapturedText();
                activity.SetTag("http.response.body",
                    SensitiveDataRedactor.Redact(body, _options.SensitiveFields));
                if (capture.Truncated)
                    activity.SetTag("http.response.body.capture", "truncated");
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private sealed class LimitedCaptureStream(Stream inner, int limit) : Stream
    {
        private readonly MemoryStream _capture = new(Math.Min(limit, 16_384));
        public int CapturedLength => (int)_capture.Length;
        public bool Truncated { get; private set; }

        public string GetCapturedText() => System.Text.Encoding.UTF8.GetString(_capture.ToArray());

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            var remaining = limit - (int)_capture.Length;
            if (remaining > 0)
            {
                var count = Math.Min(remaining, buffer.Length);
                await _capture.WriteAsync(buffer[..count], cancellationToken);
                Truncated |= count < buffer.Length;
            }
            else
            {
                Truncated = true;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            var remaining = limit - (int)_capture.Length;
            if (remaining > 0)
            {
                var captured = Math.Min(remaining, count);
                _capture.Write(buffer, offset, captured);
                Truncated |= captured < count;
            }
            else
            {
                Truncated = true;
            }
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
