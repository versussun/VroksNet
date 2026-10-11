using System.IO.Compression;
using System.Text;

namespace VroksNet.ApiService.Endpoints;

/// <summary>
/// Reads the files of a .proto import (<c>POST /api/specifications/proto</c>, ADR 0004) from a
/// multipart form: one or more <c>.proto</c> files, or a single <c>.zip</c> whose <c>.proto</c>
/// entries keep their folders (other entries are skipped). It only unpacks: matching the paths to
/// the files' imports is the parser's job (<c>ProtoSpecificationParser</c>). The limits apply to
/// what's actually read, not to the sizes an archive declares.
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

            if (await ReadTextAsync(upload.OpenReadStream(), MaxTotalBytes, cancellationToken) is not { } text)
            {
                return (null, $"\"{upload.FileName}\" is larger than {MaxTotalBytes / (1024 * 1024)} MB.");
            }

            if (!files.TryAdd(path, text))
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
            // A folder entry's name ends with "/"; an empty file is still a file (and valid proto).
            var entries = archive.Entries.Where(entry => !entry.FullName.EndsWith('/') && HasExtension(entry.FullName, ".proto")).ToList();
            if (entries.Count == 0)
            {
                return (null, $"\"{upload.FileName}\" holds no .proto files.");
            }

            if (entries.Count > MaxFiles)
            {
                return (null, $"\"{upload.FileName}\" holds more than {MaxFiles} .proto files.");
            }

            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var budget = MaxTotalBytes;
            foreach (var entry in entries)
            {
                if (SafePath(entry.FullName) is not { } path)
                {
                    return (null, $"\"{entry.FullName}\" in \"{upload.FileName}\" isn't a usable file path.");
                }

                if (files.ContainsKey(path))
                {
                    return (null, $"\"{upload.FileName}\" holds \"{path}\" twice.");
                }

                // Read at most what's left of the budget: an entry's declared size can lie.
                await using var stream = await entry.OpenAsync(cancellationToken);
                var text = await ReadTextAsync(stream, budget, cancellationToken);
                if (text is null)
                {
                    return (null, $"The .proto files in \"{upload.FileName}\" are larger than {MaxTotalBytes / (1024 * 1024)} MB unpacked.");
                }

                budget -= Encoding.UTF8.GetByteCount(text);
                files[path] = text;
            }

            return (files, null);
        }
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

    /// <summary>The stream's text, or null when it holds more than <paramref name="maxBytes"/> — read no further than that.</summary>
    private static async Task<string?> ReadTextAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
