using System.IO.Compression;
using System.Text;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Reads the files of a .proto import (<c>POST /api/specifications/proto</c>, ADR 0004) from a
/// multipart form: one or more <c>.proto</c> files, or a single <c>.zip</c> whose <c>.proto</c>
/// entries keep their folders (other entries are skipped). When every entry sits under one top
/// folder — a zipped directory — that folder is dropped, so the paths match the files' imports.
/// </summary>
public static class ProtoUpload
{
    public const int MaxFiles = 500;
    public const long MaxTotalBytes = 10 * 1024 * 1024;

    /// <summary>The files' text by path, or the reason the upload can't be read.</summary>
    public static async Task<(Dictionary<string, string>? Files, string? Error)> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return (null, "Send the files as multipart/form-data: one or more .proto files, or one .zip.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var uploads = form.Files;
        if (uploads.Count == 0)
        {
            return (null, "No files were uploaded.");
        }

        if (uploads.Sum(upload => upload.Length) > MaxTotalBytes)
        {
            return (null, $"The upload is larger than {MaxTotalBytes / (1024 * 1024)} MB.");
        }

        if (uploads.Count == 1 && HasExtension(uploads[0].FileName, ".zip"))
        {
            return await ReadZipAsync(uploads[0], cancellationToken);
        }

        if (uploads.FirstOrDefault(upload => !HasExtension(upload.FileName, ".proto")) is { } other)
        {
            return (null, $"\"{other.FileName}\" isn't a .proto file. Upload .proto files, or one .zip of them.");
        }

        if (uploads.Count > MaxFiles)
        {
            return (null, $"More than {MaxFiles} files were uploaded.");
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var upload in uploads)
        {
            if (SafePath(upload.FileName) is not { } path)
            {
                return (null, $"\"{upload.FileName}\" isn't a usable file path.");
            }

            if (!files.TryAdd(path, await ReadTextAsync(upload.OpenReadStream(), cancellationToken)))
            {
                return (null, $"\"{path}\" was uploaded twice.");
            }
        }

        return (files, null);
    }

    private static async Task<(Dictionary<string, string>? Files, string? Error)> ReadZipAsync(IFormFile upload, CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(upload.OpenReadStream(), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            return (null, $"\"{upload.FileName}\" isn't a readable .zip file.");
        }

        using (archive)
        {
            var entries = archive.Entries.Where(entry => entry.Length > 0 && HasExtension(entry.FullName, ".proto")).ToList();
            if (entries.Count == 0)
            {
                return (null, $"\"{upload.FileName}\" holds no .proto files.");
            }

            if (entries.Count > MaxFiles)
            {
                return (null, $"\"{upload.FileName}\" holds more than {MaxFiles} .proto files.");
            }

            // Uncompressed size: a small zip can expand a lot.
            if (entries.Sum(entry => entry.Length) > MaxTotalBytes)
            {
                return (null, $"The .proto files in \"{upload.FileName}\" are larger than {MaxTotalBytes / (1024 * 1024)} MB unpacked.");
            }

            var paths = new List<(ZipArchiveEntry Entry, string Path)>();
            foreach (var entry in entries)
            {
                if (SafePath(entry.FullName) is not { } path)
                {
                    return (null, $"\"{entry.FullName}\" in \"{upload.FileName}\" isn't a usable file path.");
                }

                paths.Add((entry, path));
            }

            var prefix = CommonTopFolder(paths.Select(item => item.Path).ToList());
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (entry, path) in paths)
            {
                await using var stream = await entry.OpenAsync(cancellationToken);
                files[path[prefix.Length..]] = await ReadTextAsync(stream, cancellationToken);
            }

            return (files, null);
        }
    }

    /// <summary>"protos/" when every path starts with that one folder and none sits beside it; otherwise "".</summary>
    private static string CommonTopFolder(IReadOnlyList<string> paths)
    {
        var tops = paths.Select(path => path.IndexOf('/') is var slash and > 0 ? path[..(slash + 1)] : string.Empty).Distinct().ToList();
        return tops is [{ Length: > 0 } top] ? top : string.Empty;
    }

    /// <summary>A relative path with "/" separators and no "." or ".." segments; null when it can't be one.</summary>
    private static string? SafePath(string name)
    {
        var segments = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            return null;
        }

        return string.Join('/', segments);
    }

    private static bool HasExtension(string name, string extension)
        => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
