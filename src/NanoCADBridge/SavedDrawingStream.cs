using Teigha.Runtime;

namespace NanoCADBridge;

// OdMgStreamBuf calls back into this read-only adapter. The bytes come
// from the private immutable file. DWG-relative references keep their
// original base path. The native read and its dependent layout operation
// must finish while both the adapter and its backing stream remain alive.
internal sealed class SavedDrawingStream : StreamBuf
{
    readonly FileStreamBuf bytes;
    readonly string drawingPath;

    public SavedDrawingStream(FileStreamBuf bytes, string drawingPath)
    {
        this.bytes = bytes;
        this.drawingPath = drawingPath;
    }

    public override string FileName => drawingPath;
    public override bool IsEof => bytes.IsEof;
    public override ulong Tell => bytes.Tell;
    public override ulong Length => bytes.Length;
    public override uint ShareMode => bytes.ShareMode;
    public override void Rewind() => bytes.Rewind();
    public override ulong Seek(long offset, FilerSeekType location) => bytes.Seek(offset, location);
    public override byte Read() => bytes.Read();
    public override byte[] Read(uint count) => bytes.Read(count);
    public override void CopyTo(StreamBuf destination, ulong start, ulong end) => bytes.CopyTo(destination, start, end);
    public override void Truncate() => throw new NotSupportedException("The saved-drawing capture stream is read-only.");
    public override void Write(byte value) => throw new NotSupportedException("The saved-drawing capture stream is read-only.");
    public override void Write(byte[] data, uint count) => throw new NotSupportedException("The saved-drawing capture stream is read-only.");
}
