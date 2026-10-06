// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using RemoteClipboard.Core.Pairing;

namespace RemoteClipboard.Core.Tests.Pairing;

public class PairingCodeTests
{
    [Fact]
    public void Generated_codes_are_six_digits()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = PairingCode.Generate();
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.InRange(c, '0', '9'));
        }
    }

    [Theory]
    [InlineData("847291", "847291")]
    [InlineData(" 847 291 ", "847291")]
    [InlineData("847-291", "847291")]
    [InlineData("000000", "000000")]
    public void Valid_input_is_normalized(string input, string expected)
    {
        Assert.True(PairingCode.TryNormalize(input, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("84729a")]
    [InlineData("８４７２９１")] // full-width digits
    public void Invalid_input_is_rejected(string? input)
    {
        Assert.False(PairingCode.TryNormalize(input, out _));
    }
}
