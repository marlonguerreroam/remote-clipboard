// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers;
using System.IO.Compression;

namespace RemoteClipboard.Core.Clipboard;

/// <summary>
/// Files and folders copied in Explorer travel as one ZIP archive (<see cref="ClipboardFormat.FileList"/>).
/// Extraction treats the archive as hostile even though it comes from a paired device: every entry name
/// is validated (no absolute paths, no "..", no names Windows reserves), nothing can be written outside
/// the target folder, and the size limit is enforced on the bytes actually written, not on what the
/// archive claims.
/// </summary>
public static class FileArchive
{
    public const int MaxEntries = 10_000;

    private const int MaxSegmentLength = 255;
    private static readonly SearchValues<char> InvalidNameChars = SearchValues.Create("<>:\"/\\|?*");
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Archives the given files and folders (folders recursively). Returns null when the files add up to
    /// more than <paramref name="maxBytes"/> or more than <see cref="MaxEntries"/> items; nothing is read
    /// in that case. Linked folders (junctions, symbolic links) are not followed.
    /// </summary>
    public static byte[]? Create(IReadOnlyList<string> paths, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var plan = Plan(paths, maxBytes);
        if (plan is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (source, name, isDirectory) in plan)
            {
                if (isDirectory)
                {
                    zip.CreateEntry(name + "/");
                    continue;
                }

                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                entry.LastWriteTime = File.GetLastWriteTime(source);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var output = entry.Open();
                input.CopyTo(output);
            }
        }

        return buffer.Length <= maxBytes ? buffer.ToArray() : null;
    }

    /// <summary>
    /// Extracts an archive into <paramref name="targetDirectory"/> (which must exist and be empty) and
    /// returns the full paths of its top-level items, in archive order, for the clipboard.
    /// </summary>
    /// <exception cref="InvalidDataException">The archive is corrupt, unsafe or too large.</exception>
    public static IReadOnlyList<string> Extract(ReadOnlyMemory<byte> archive, string targetDirectory, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);
        var root = Path.GetFullPath(targetDirectory);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var topLevel = new List<string>();
        var seenTopLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Windows names ignore case
        long written = 0;

        using var stream = new MemoryStream(archive.ToArray(), writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException("The archive has too many entries.");
        }

        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var isDirectory = name.EndsWith('/');
            var segments = name.TrimEnd('/').Split('/');
            if (segments.Length == 0 || !segments.All(IsSafeSegment))
            {
                throw new InvalidDataException("The archive contains an unsafe file name.");
            }

            var fullPath = Path.GetFullPath(Path.Combine([root, .. segments]));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The archive points outside its folder.");
            }

            if (!seenPaths.Add(fullPath))
            {
                throw new InvalidDataException("The archive contains the same name twice.");
            }

            if (seenTopLevel.Add(segments[0]))
            {
                topLevel.Add(Path.Combine(root, segments[0]));
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var input = entry.Open();
            using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
            written = CopyWithLimit(input, output, written, maxBytes);
            output.Flush();
            File.SetLastWriteTime(fullPath, entry.LastWriteTime.DateTime);
        }

        return topLevel;
    }

    /// <summary>Whether one path segment is a valid, harmless Windows file or folder name.</summary>
    public static bool IsSafeSegment(string segment) =>
        !string.IsNullOrEmpty(segment)
        && segment.Length <= MaxSegmentLength
        && segment is not "." and not ".."
        && segment.AsSpan().IndexOfAny(InvalidNameChars) < 0
        && !segment.Any(char.IsControl)
        && !segment.EndsWith('.') && !segment.EndsWith(' ')
        && !ReservedNames.Contains(segment.Split('.')[0].TrimEnd());

    private static long CopyWithLimit(Stream input, Stream output, long written, long maxBytes)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            if (written > maxBytes)
            {
                throw new InvalidDataException("The archive expands beyond the size limit.");
            }

            output.Write(buffer, 0, read);
        }

        return written;
    }

    /// <summary>What to archive, with entry names; null when over the limits.</summary>
    private static List<(string Source, string Name, bool IsDirectory)>? Plan(IReadOnlyList<string> paths, long maxBytes)
    {
        var plan = new List<(string, string, bool)>();
        var topNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = UniqueName(Path.GetFileName(full), topNames);
            if (Directory.Exists(full))
            {
                if (!AddDirectory(full, name, plan, ref total, maxBytes))
                {
                    return null;
                }
            }
            else if (File.Exists(full))
            {
                total += new FileInfo(full).Length;
                plan.Add((full, name, false));
            }

            if (total > maxBytes || plan.Count > MaxEntries)
            {
                return null;
            }
        }

        return plan.Count > 0 ? plan : null;
    }

    private static bool AddDirectory(string directory, string name, List<(string, string, bool)> plan, ref long total, long maxBytes)
    {
        plan.Add((directory, name, true));
        var info = new DirectoryInfo(directory);
        foreach (var child in info.EnumerateFileSystemInfos())
        {
            var childName = name + "/" + child.Name;
            if (child is DirectoryInfo childDirectory)
            {
                // Junctions and directory links could loop or reach outside what the user copied.
                if (childDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (!AddDirectory(childDirectory.FullName, childName, plan, ref total, maxBytes))
                {
                    return false;
                }
            }
            else if (child is FileInfo file)
            {
                total += file.Length;
                plan.Add((file.FullName, childName, false));
            }

            if (total > maxBytes || plan.Count > MaxEntries)
            {
                return false;
            }
        }

        return true;
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
        }

        return candidate;
    }
}
