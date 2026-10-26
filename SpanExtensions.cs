using System;
using System.Runtime.InteropServices;

namespace PaintDotNet.FileTypes.Samples;

internal static class SpanExtensions
{
    public static Span<byte> AsBytes<T>(this Span<T> span)
        where T : unmanaged
    {
        return MemoryMarshal.AsBytes(span);
    }

    public static ReadOnlySpan<byte> AsBytes<T>(this ReadOnlySpan<T> span)
        where T : unmanaged
    {
        return MemoryMarshal.AsBytes(span);
    }
}
