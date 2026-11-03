using NanoCAD.Contracts;

namespace NanoCAD.Ipc;

// Shared preflight only: this class never creates, deletes or changes a file.
public static class DocumentFilePolicy
{
    public static void Check(string path, bool opening, bool overwrite)
    {
        if (!Directory.Exists(Path.GetDirectoryName(path))) throw new DomainException("DIRECTORY_NOT_FOUND", "Parent directory does not exist.");
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new DomainException("INVALID_PATH", "Reparse-point paths are not supported.");
        if (Directory.Exists(path)) throw new DomainException("INVALID_PATH", "Path names a directory.");
        if (opening && !File.Exists(path)) throw new DomainException("FILE_NOT_FOUND", "Requested drawing file does not exist.");
        if (!opening && File.Exists(path))
        {
            if (!overwrite) throw new DomainException("FILE_EXISTS", "Target exists; set overwrite=true only when replacement is intended.");
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new DomainException("FILE_READ_ONLY", "Target file is read-only.");
        }
    }
}
