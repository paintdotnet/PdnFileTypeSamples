using PaintDotNet.FileTypes;
using System;

namespace PdnFileTypeSamples;

internal class MyFileTypeFactory
    : IFileTypeFactory
{
    public IFileType[] CreateFileTypes(IFileTypeHost host)
    {
        return [new MyFileType(host)];
    }
}
