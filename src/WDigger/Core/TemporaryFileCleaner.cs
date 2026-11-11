namespace WDigger.Core;

/// <summary>
/// Deletes only entries below an explicitly supplied temporary directory. It
/// never follows reparse points and reports every skipped or failed item.
/// </summary>
public sealed class TemporaryFileCleaner
{
    public sealed record Preview(string Root, int Files, int Directories, long Bytes, int ReparsePoints, int Errors);
    public sealed record Result(int DeletedFiles, int DeletedDirectories, long DeletedBytes, int SkippedReparsePoints,
        IReadOnlyList<string> Errors);

    public Preview Inspect(string root, CancellationToken cancellationToken = default)
    {
        var fullRoot = ValidateRoot(root);
        var files = 0;
        var directories = 0;
        var bytes = 0L;
        var reparsePoints = 0;
        var errors = 0;
        InspectDirectory(fullRoot, ref files, ref directories, ref bytes, ref reparsePoints, ref errors, cancellationToken);
        return new Preview(fullRoot, files, directories, bytes, reparsePoints, errors);
    }

    public Result Delete(string root, CancellationToken cancellationToken = default)
    {
        var fullRoot = ValidateRoot(root);
        var errors = new List<string>();
        var deletedFiles = 0;
        var deletedDirectories = 0;
        var deletedBytes = 0L;
        var skippedReparsePoints = 0;
        DeleteChildren(fullRoot, ref deletedFiles, ref deletedDirectories, ref deletedBytes, ref skippedReparsePoints, errors,
            cancellationToken);
        return new Result(deletedFiles, deletedDirectories, deletedBytes, skippedReparsePoints, errors);
    }

    private static string ValidateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("No se indicó una carpeta temporal.", nameof(root));
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) throw new DirectoryNotFoundException("La carpeta temporal no existe.");
        var attributes = File.GetAttributes(fullRoot);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("La carpeta temporal es un punto de redirección; no se eliminará contenido.");
        }
        return fullRoot;
    }

    private static void InspectDirectory(string directory, ref int files, ref int directories, ref long bytes,
        ref int reparsePoints, ref int errors, CancellationToken token)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
        catch { errors++; return; }

        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    reparsePoints++;
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories++;
                    InspectDirectory(entry, ref files, ref directories, ref bytes, ref reparsePoints, ref errors, token);
                }
                else
                {
                    files++;
                    bytes += new FileInfo(entry).Length;
                }
            }
            catch
            {
                errors++;
            }
        }
    }

    private static void DeleteChildren(string directory, ref int deletedFiles, ref int deletedDirectories, ref long deletedBytes,
        ref int skippedReparsePoints, List<string> errors, CancellationToken token)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
        catch (Exception exception)
        {
            errors.Add($"No se pudo enumerar una carpeta temporal: {exception.Message}");
            return;
        }

        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedReparsePoints++;
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    DeleteChildren(entry, ref deletedFiles, ref deletedDirectories, ref deletedBytes, ref skippedReparsePoints, errors, token);
                    Directory.Delete(entry, recursive: false);
                    deletedDirectories++;
                }
                else
                {
                    var length = new FileInfo(entry).Length;
                    File.Delete(entry);
                    deletedFiles++;
                    deletedBytes += length;
                }
            }
            catch (Exception exception)
            {
                errors.Add($"No se pudo eliminar un elemento temporal: {exception.Message}");
            }
        }
    }
}
