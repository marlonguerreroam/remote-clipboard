// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Security.Cryptography;
using RemoteClipboard.Core.Clipboard;
using RemoteClipboard.Core.Tests.TestSupport;

namespace RemoteClipboard.Core.Tests.Clipboard;

public sealed class FileArchiveTests : IDisposable
{
    private const long Limit = 16 * 1024 * 1024;
    private readonly TempDirectory _source = new();
    private readonly TempDirectory _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    [Fact]
    public void Files_and_folders_round_trip_with_names_content_and_empty_folders()
    {
        var report = Write("Informe ñandú 🚀.docx", RandomNumberGenerator.GetBytes(300_000));
        var folder = Directory.CreateDirectory(_source.File("Proyecto")).FullName;
        File.WriteAllText(Path.Combine(folder, "notas.txt"), "hola");
        Directory.CreateDirectory(Path.Combine(folder, "sub", "vacía"));
        File.WriteAllBytes(Path.Combine(folder, "sub", "datos.bin"), [1, 2, 3]);

        var archive = FileArchive.Create([report, folder], Limit);
        var top = FileArchive.Extract(Assert.IsType<byte[]>(archive), _target.Path, Limit);

        Assert.Equal([_target.File("Informe ñandú 🚀.docx"), _target.File("Proyecto")], top);
        Assert.Equal(File.ReadAllBytes(report), File.ReadAllBytes(top[0]));
        Assert.Equal("hola", File.ReadAllText(Path.Combine(top[1], "notas.txt")));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(top[1], "sub", "datos.bin")));
        Assert.True(Directory.Exists(Path.Combine(top[1], "sub", "vacía")));
    }

    [Fact]
    public void Same_name_from_two_folders_is_kept_twice()
    {
        var first = Write(Path.Combine("a", "foto.png"), [1]);
        var second = Write(Path.Combine("b", "foto.png"), [2]);

        var top = FileArchive.Extract(FileArchive.Create([first, second], Limit)!, _target.Path, Limit);

        Assert.Equal([_target.File("foto.png"), _target.File("foto (2).png")], top);
        Assert.Equal([2], File.ReadAllBytes(top[1]));
    }

    [Fact]
    public void Copies_over_the_limit_are_not_archived()
    {
        var big = Write("grande.bin", new byte[2048]);

        Assert.Null(FileArchive.Create([big], maxBytes: 1024));
        Assert.Null(FileArchive.Create([_source.File("no-existe.txt")], Limit)); // nothing to send
    }

    [Theory]
    [InlineData("../fuera.txt")]
    [InlineData("a/../../fuera.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("C:evil.txt")]
    [InlineData("..\\fuera.txt")]
    [InlineData("ok/./x.txt")]
    [InlineData("CON")]
    [InlineData("carpeta/nul.txt")]
    [InlineData("archivo.txt:oculto")]
    [InlineData("termina en punto.")]
    [InlineData("a//b.txt")]
    public void Unsafe_entry_names_are_rejected_and_nothing_escapes(string name)
    {
        var archive = Zip((name, [1, 2, 3]));

        Assert.Throws<InvalidDataException>(() => FileArchive.Extract(archive, _target.Path, Limit));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_target.Path)!, "fuera.txt")));
    }

    [Fact]
    public void Compression_bombs_are_stopped_by_the_bytes_written()
    {
        var archive = Zip(("ceros.bin", new byte[4 * 1024 * 1024])); // compresses to a few KB

        Assert.True(archive.Length < 64 * 1024);
        Assert.Throws<InvalidDataException>(() => FileArchive.Extract(archive, _target.Path, maxBytes: 1024 * 1024));
    }

    [Fact]
    public void Corrupt_archives_and_duplicate_entries_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => FileArchive.Extract(RandomNumberGenerator.GetBytes(500), _target.Path, Limit));
        Assert.Throws<InvalidDataException>(() => FileArchive.Extract(Zip(("x.txt", [1]), ("X.TXT", [2])), _target.Path, Limit));
    }

    [Theory]
    [InlineData("Informe final.docx", true)]
    [InlineData("ñandú 🚀", true)]
    [InlineData(".gitignore", true)]
    [InlineData("CONSOLE.txt", true)]
    [InlineData("aux.log", false)]
    [InlineData("LPT1", false)]
    [InlineData("a|b", false)]
    [InlineData("tab\there", false)]
    [InlineData("", false)]
    public void Segment_validation(string segment, bool safe) => Assert.Equal(safe, FileArchive.IsSafeSegment(segment));

    private string Write(string relative, byte[] content)
    {
        var path = _source.File(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
