// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

namespace RemoteClipboard.Core.Tests.TestSupport;

internal static class TextSamples
{
    public const string Plain = "Hola, este es un texto de prueba.";
    public const string Spanish = "Canción, pingüino, ÁÉÍÓÚ ñÑ ¿¡";
    public const string Emojis = "Listo ✅ 🚀 👨‍👩‍👧‍👦 🇨🇴";
    public const string WindowsNewLines = "línea 1\r\nlínea 2\r\n\r\nlínea 4";
    public const string UnixNewLines = "línea 1\nlínea 2\n";
    public const string Mixed = "日本語 / Ελληνικά / العربية / \t tab";

    public static TheoryData<string> All => new(Plain, Spanish, Emojis, WindowsNewLines, UnixNewLines, Mixed);
}
