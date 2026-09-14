using System.Buffers;

namespace UnrealPluginFlattening;

public static partial class PluginFlattener
{
    // Synchronization owns only declared payload roots. Build caches and caller-owned output outside them survive.
    private static void Synchronize(string output, IReadOnlyDictionary<string, GeneratedFile> files,
        Action<string> log, CancellationToken cancellationToken)
    {
        foreach (string root in PayloadDirectories.Prepend(""))
        {
            string path = Path.Combine(output, root);
            if (!Directory.Exists(path)) continue;
            RejectLink(path);
            if (root.Length == 0) continue;
            foreach (string entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)) RejectLink(entry);
        }
        // A file cannot also be an ancestor directory of another planned file, regardless of enumeration order.
        foreach (string relative in files.Keys)
        {
            string? parent = Path.GetDirectoryName(relative);
            while (!string.IsNullOrEmpty(parent))
            {
                if (files.ContainsKey(parent)) throw new InvalidOperationException($"Generated file/directory collision: {parent}");
                parent = Path.GetDirectoryName(parent);
            }
        }
        Directory.CreateDirectory(output);
        foreach ((string relative, GeneratedFile file) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.Combine(output, relative);
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            EnsureDirectory(Path.GetDirectoryName(destination)!);
            DateTime? oldTimestamp = File.Exists(destination) ? File.GetLastWriteTimeUtc(destination) : null;
            if (file.Text != null)
            {
                if (File.Exists(destination) && File.ReadAllText(destination) == file.Text) continue;
                File.WriteAllText(destination, file.Text);
            }
            else
            {
                if (File.Exists(destination) && FilesEqual(file.Source!, destination, cancellationToken)) continue;
                File.Copy(file.Source!, destination, true);
            }
            // Equal mtimes after a changed copy can make UBT reuse output compiled from different generated source.
            if (oldTimestamp.HasValue && File.GetLastWriteTimeUtc(destination) == oldTimestamp.Value)
                File.SetLastWriteTimeUtc(destination, DateTime.UtcNow > oldTimestamp.Value ? DateTime.UtcNow : oldTimestamp.Value.AddSeconds(1));
            log($"Updated generated plugin file: {relative}");
        }
        foreach (string root in PayloadDirectories)
        {
            string directory = Path.Combine(output, root);
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (files.ContainsKey(Path.GetRelativePath(output, path))) continue;
                File.Delete(path);
                log($"Removed stale generated plugin file: {Path.GetRelativePath(output, path)}");
            }
            foreach (string path in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
                if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }

    // Generated payload must not redirect writes or stale-file deletion into another tree through a symlink/junction.
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Generated plugin payload contains a link: {path}");
    }

    // A source directory replacing a previously generated file must work without clearing the surrounding build cache.
    private static void EnsureDirectory(string directory)
    {
        if (Directory.Exists(directory)) return;
        if (File.Exists(directory)) File.Delete(directory);
        string? parent = Path.GetDirectoryName(directory);
        if (parent != null) EnsureDirectory(parent);
        Directory.CreateDirectory(directory);
    }

    // Stream binary assets in bounded chunks; timestamps alone cannot establish equality after dependency updates.
    private static bool FilesEqual(string source, string destination, CancellationToken cancellationToken)
    {
        if (new FileInfo(source).Length != new FileInfo(destination).Length) return false;
        const int bufferSize = 128 * 1024;
        byte[] left = ArrayPool<byte>.Shared.Rent(bufferSize);
        byte[] right = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            using FileStream first = File.OpenRead(source);
            using FileStream second = File.OpenRead(destination);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = ReadChunk(first, left, bufferSize);
                int otherCount = ReadChunk(second, right, bufferSize);
                if (count != otherCount || !left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, otherCount))) return false;
                if (count == 0) return true;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(left);
            ArrayPool<byte>.Shared.Return(right);
        }
    }

    // Fill a comparison chunk because FileStream is allowed to return a short read before end-of-file.
    private static int ReadChunk(Stream stream, byte[] buffer, int size)
    {
        int total = 0;
        while (total < size)
        {
            int count = stream.Read(buffer, total, size - total);
            if (count == 0) break;
            total += count;
        }
        return total;
    }
}
