// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Networking;

namespace RemoteClipboard.Core.Tests.Pairing;

public class EndpointParserTests
{
    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20", 47800)]
    [InlineData(" 192.168.1.20:47801 ", "192.168.1.20", 47801)]
    [InlineData("[fe80::1]:47802", "fe80::1", 47802)]
    [InlineData("fe80::1", "fe80::1", 47800)]
    [InlineData("SERVIDOR", "SERVIDOR", 47800)]
    [InlineData("pc-marlon.local:47805", "pc-marlon.local", 47805)]
    public void Valid_input(string input, string host, int port)
    {
        Assert.True(EndpointParser.TryParse(input, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("192.168.1.20:99999")]
    [InlineData("host:abc")]
    [InlineData("two words")]
    public void Invalid_input(string? input) => Assert.False(EndpointParser.TryParse(input, out _, out _));
}
