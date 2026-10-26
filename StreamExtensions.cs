using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PaintDotNet.FileTypes.Samples;

internal static class StreamExtensions
{
    public static T ReadValue<T>(this Stream stream)
        where T : unmanaged
    {
        T value = default;
        stream.ReadExactly(MemoryMarshal.CreateSpan(ref value, 1).AsBytes());
        return value;
    }

    public static void WriteValue<T>(this Stream stream, in T value)
        where T : unmanaged
    {
        stream.Write(MemoryMarshal.CreateReadOnlySpan(in value, 1).AsBytes());
    }
}
