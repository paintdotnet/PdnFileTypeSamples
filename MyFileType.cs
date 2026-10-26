using PaintDotNet;
using PaintDotNet.ComponentModel;
using PaintDotNet.FileTypes;
using PaintDotNet.FileTypes.Samples;
using PaintDotNet.Imaging;
using PaintDotNet.IO;
using PaintDotNet.Rendering;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PdnFileTypeSamples;

// An implementation of a simple file type with a .mine extension.

// The format is simple:
// * bytes 0 through 3: 'mine' header to identify the file type
// * bytes 4 through 7: integer width
// * bytes 8 through 11: integer height
// * bytes 12 through 27: pixel format GUID
// * bytes 28 through end: pixel values, starting at [x=0, y=0] through [x=width-1, y=0], then [x=0, y=1] through [x=width-1, y=1], etc.

public sealed class MyFileType
    : FileType
{
    private static ReadOnlySpan<byte> MineFileHeader => "mine"u8;

    public MyFileType(IFileTypeHost host)
        : base(
            host,
            "Mine",
            FileTypeOptions.Create() with
            {
                LoadExtensions = [".mine"],
                SaveExtensions = [".mine"]
            })
    {
    }

    protected override IFileTypeLoader OnCreateLoader()
    {
        return new Loader(this);
    }

    protected override IFileTypeSaver OnCreateSaver()
    {
        return new Saver(this);
    }

    private sealed class Loader
        : FileTypeLoader
    {
        private readonly MyFileType fileType;

        public Loader(MyFileType fileType)
            : base(fileType)
        {
            this.fileType = fileType;
        }

        protected override IFileTypeDocument OnLoad(IFileTypeLoadContext context)
        {
            // First, read 4 bytes that identify the file type as 'mine'
            Span<byte> header = stackalloc byte[4];
            context.Input.ReadExactly(header);
            if (!header.SequenceEqual(MineFileHeader))
            {
                throw new FormatException("Not a valid .mine file");
            }
            
            // Read the width and height
            int width = context.Input.TryReadInt32() ?? throw new FormatException();
            int height = context.Input.TryReadInt32() ?? throw new FormatException();

            // Can't be negative or zero, but also place an arbitrary upper limit on the size
            if (width <= 0 || width > 100_000 || height <= 0 || height > 100_000)
            {
                throw new FormatException();
            }

            // Read the pixel format. We'll use the GUIDs from PixelFormats
            PixelFormat format = default;
            context.Input.ReadExactly(MemoryMarshal.CreateSpan(ref format, 1).AsBytes());

            // We could use one generic method that can handle any pixel format, but I want to show you to deal
            // with pixel formats in a few different ways.

            // First, if the pixel format is BGRA32, work with that pixel format directly
            if (format == PixelFormats.Bgra32)
            {
                return LoadBgra32(context.Factory, context.Input, width, height);
            }

            // Second, utilize generics for a few other pixel formats
            if (format == PixelFormats.Rgba64)
            {
                return Load<ColorRgba64>(context.Factory, context.Input, width, height);
            }
            else if (format == PixelFormats.Bgr24)
            {
                return Load<ColorBgr24>(context.Factory, context.Input, width, height);
            }

            // Lastly, handle any other formats in a generic manner but without using C# generics
            return Load(context.Factory, context.Input, format, width, height);
        }

        // This method shows how to work with the IFileType interfaces for a specific pixel format using its
        // corresponding color struct. Useful when you only care about working with one particular pixel format.
        private IFileTypeDocument<ColorBgra32> LoadBgra32(IFileTypeDocumentFactory factory, Stream input, int width, int height)
        {
            IFileTypeDocument<ColorBgra32> document = factory.CreateDocumentBgra32(width, height);

            IFileTypeBitmapLayer<ColorBgra32> layer = document.CreateBitmapLayer();
            using (IFileTypeBitmapSink<ColorBgra32> layerSink = layer.GetBitmap())
            using (IFileTypeBitmapLock<ColorBgra32> layerBitmapLock = layerSink.Lock())
            {
                RegionPtr<ColorBgra32> region = layerBitmapLock.AsRegionPtr();

                for (int y = 0; y < height; ++y)
                {
                    input.ReadExactly(region.Rows[y].Span.AsBytes());
                }
            }

            document.Layers.Add(layer);
            return document;
        }

        // This method shows you how to work with the IFileType interfaces in a generic manner for pixel formats
        // which have a color struct available. Useful when you want to work with a variety of pixel formats, and
        // have a specific list of which ones you'll support.
        private IFileTypeDocument<TPixel> Load<TPixel>(IFileTypeDocumentFactory factory, Stream input, int width, int height)
            where TPixel : unmanaged, INaturalPixelInfo
        {
            IFileTypeDocument<TPixel> document = factory.CreateDocument<TPixel>(width, height);

            IFileTypeBitmapLayer<TPixel> layer = document.CreateBitmapLayer();
            using (IFileTypeBitmapSink<TPixel> layerSink = layer.GetBitmap())
            using (IFileTypeBitmapLock<TPixel> layerBitmapLock = layerSink.Lock())
            {
                RegionPtr<TPixel> region = layerBitmapLock.AsRegionPtr();

                for (int y = 0; y < height; ++y)
                {
                    input.ReadExactly(region.Rows[y].Span.AsBytes());
                }
            }

            document.Layers.Add(layer);
            return document;
        }

        // This method shows you how to work with the IFileType interfaces in a generic manner for any pixel format, but without
        // using C# generics. Useful when you want to support any pixel format, even if there's no color struct available.
        // Note that this method does not work with pixel formats that are less than 1 byte per pixel (like Indexed4), nor would
        // it work on any pixel format that is not a whole number of bytes (a hypothetical 36-bits per pixel format would not work).
        private unsafe IFileTypeDocument Load(IFileTypeDocumentFactory factory, Stream input, PixelFormat format, int width, int height)
        {
            IPixelFormatInfo formatInfo = format.GetInfo();
            if ((formatInfo.BitsPerPixel % 8) != 0)
            {
                throw new FormatException("Only pixel formats that are comprised of whole bytes are supported");
            }

            int bytesPerPixel = formatInfo.BitsPerPixel / 8;
            int bytesPerRow = checked(bytesPerPixel * width);

            IFileTypeDocument document = factory.CreateDocument(width, height, format);

            IFileTypeBitmapLayer layer = document.CreateBitmapLayer();
            using (IFileTypeBitmapSink layerSink = layer.GetBitmap())
            using (IFileTypeBitmapLock layerBitmapLock = layerSink.Lock())
            {
                for (int y = 0; y < height; ++y)
                {
                    byte* pRow = (byte*)BitmapUtil.GetRowPointer(layerBitmapLock.Buffer, layerBitmapLock.BufferStride, y);
                    Span<byte> rowSpan = new Span<byte>(pRow, bytesPerRow);
                    input.ReadExactly(rowSpan);
                }
            }

            document.Layers.Add(layer);
            return document;
        }
    }

    private sealed class Saver
        : FileTypeSaver
    {
        public Saver(MyFileType fileType)
            : base(fileType)
        {
        }

        protected override void OnSave(IFileTypeSaveContext context)
        {
            IReadOnlyFileTypeDocument document = context.Document;

            context.Output.Write(MineFileHeader);
            context.Output.WriteInt32(document.Size.Width);
            context.Output.WriteInt32(document.Size.Height);
            context.Output.WriteValue(document.PixelFormat);

            // Once again, we'll write out the document in 3 different ways:
            // - Specific pixel format, showing how to bring in the color struct via the generics and component model systems
            // - Using C# generics for some other pixel formats
            // - Without using C# generics for all other pixel formats

            if (document.PixelFormat == PixelFormats.Bgra32)
            {
                // We must use the component model system to "create a ref". We cannot use a standard cast.
                IReadOnlyFileTypeDocument<ColorBgra32> docBgra32 = document.CreateRef<IReadOnlyFileTypeDocument<ColorBgra32>>();
                WritePixelsToStreamBgra32(context.Output, docBgra32);
            }
            else if (document.PixelFormat == PixelFormats.Rgba64)
            {
                IReadOnlyFileTypeDocument<ColorRgba64> docRgba64 = document.CreateRef<IReadOnlyFileTypeDocument<ColorRgba64>>();
                WritePixelsToStream(context.Output, docRgba64);
            }
            else if (document.PixelFormat == PixelFormats.Bgr24)
            {
                IReadOnlyFileTypeDocument<ColorBgr24> docBgr24 = document.CreateRef<IReadOnlyFileTypeDocument<ColorBgr24>>();
                WritePixelsToStream(context.Output, docBgr24);
            }
            else
            {
                WritePixelsToStream(context.Output, document);
            }
        }

        private void WritePixelsToStreamBgra32(Stream output, IReadOnlyFileTypeDocument<ColorBgra32> document)
        {
            SizeInt32 size = document.Size;

            IReadOnlyFileTypeLayer<ColorBgra32> layer = document.Layers[0];
            using (IFileTypeBitmapSource<ColorBgra32> layerBitmap = layer.GetBitmap())
            using (IFileTypeBitmapLock<ColorBgra32> layerBitmapLock = layerBitmap.Lock())
            {
                RegionPtr<ColorBgra32> region = layerBitmapLock.AsRegionPtr();

                for (int y = 0; y < size.Height; ++y)
                {
                    output.Write(region.Rows[y].Span.AsBytes());
                }
            }
        }

        private void WritePixelsToStream<TPixel>(Stream output, IReadOnlyFileTypeDocument<TPixel> document)
            where TPixel : unmanaged, INaturalPixelInfo
        {
            SizeInt32 size = document.Size;

            IReadOnlyFileTypeLayer<TPixel> layer = document.Layers[0];
            using (IFileTypeBitmapSource<TPixel> layerBitmap = layer.GetBitmap())
            using (IFileTypeBitmapLock<TPixel> layerBitmapLock = layerBitmap.Lock())
            {
                RegionPtr<TPixel> region = layerBitmapLock.AsRegionPtr();

                for (int y = 0; y < size.Height; ++y)
                {
                    output.Write(region.Rows[y].Span.AsBytes());
                }
            }
        }

        private unsafe void WritePixelsToStream(Stream output, IReadOnlyFileTypeDocument document)
        {
            SizeInt32 size = document.Size;

            IPixelFormatInfo formatInfo = document.PixelFormat.GetInfo();
            if ((formatInfo.BitsPerPixel % 8) != 0)
            {
                throw new FormatException("Only pixel formats that are comprised of whole bytes are supported");
            }

            int bytesPerPixel = formatInfo.BitsPerPixel / 8;
            int bytesPerRow = checked(bytesPerPixel * size.Width);

            IReadOnlyFileTypeLayer layer = document.Layers[0];
            using (IFileTypeBitmapSource layerBitmap = layer.GetBitmap())
            using (IFileTypeBitmapLock layerBitmapLock = layerBitmap.Lock())
            {
                for (int y = 0; y < size.Height; ++y)
                {
                    byte* pRow = (byte*)BitmapUtil.GetRowPointer(layerBitmapLock.Buffer, layerBitmapLock.BufferStride, y);
                    Span<byte> rowSpan = new Span<byte>(pRow, bytesPerRow);
                    output.Write(rowSpan);
                }
            }
        }
    }
}
