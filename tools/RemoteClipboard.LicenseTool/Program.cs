// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using RemoteClipboard.Core.Licensing;

// Remote Clipboard license tool (author only).
//   LicenseTool keygen [--out private-key.pem]
//   LicenseTool issue --key private-key.pem --to "Nombre del cliente" [--edition personal|business] [--count N]
//   LicenseTool verify --public <base64> --license <RC1-...>
Console.OutputEncoding = Encoding.UTF8;
try
{
    return args.FirstOrDefault() switch
    {
        "keygen" => KeyGen(Option(args, "--out") ?? "private-key.pem"),
        "issue" => Issue(Required(args, "--key"), Required(args, "--to"), Option(args, "--edition") ?? "personal",
            int.Parse(Option(args, "--count") ?? "1", System.Globalization.CultureInfo.InvariantCulture)),
        "verify" => Verify(Required(args, "--public"), Required(args, "--license")),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is ArgumentException or IOException or CryptographicException or FormatException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static int KeyGen(string path)
{
    if (File.Exists(path))
    {
        throw new IOException($"{path} ya existe. No se sobrescribe una clave privada: muévela o elige otro --out.");
    }

    var password = ReadPassword("Contraseña para proteger la clave privada (mínimo 12 caracteres): ");
    if (password.Length < 12 || password != ReadPassword("Repite la contraseña: "))
    {
        throw new ArgumentException("La contraseña es demasiado corta o no coincide.");
    }

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var pem = key.ExportEncryptedPkcs8PrivateKeyPem(password,
        new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
    File.WriteAllText(path, pem);

    Console.WriteLine();
    Console.WriteLine($"Clave privada guardada (cifrada) en: {Path.GetFullPath(path)}");
    Console.WriteLine("GUÁRDALA CON COPIA DE SEGURIDAD y NUNCA la compartas ni la subas a GitHub.");
    Console.WriteLine("Si la pierdes no podrás emitir más licencias; si se filtra, cualquiera podrá crearlas.");
    Console.WriteLine();
    Console.WriteLine("Clave PÚBLICA (no es secreta; va dentro de la app, LicensingConfig.PublicKey):");
    Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    return 0;
}

static int Issue(string keyPath, string licensee, string edition, int count)
{
    if (count is < 1 or > 1000)
    {
        throw new ArgumentException("--count debe estar entre 1 y 1000.");
    }

    var parsedEdition = edition switch
    {
        "personal" => LicenseEdition.Personal,
        "business" => LicenseEdition.Business,
        _ => throw new ArgumentException("--edition debe ser personal o business."),
    };

    using var key = ECDsa.Create();
    key.ImportFromEncryptedPem(File.ReadAllText(keyPath), ReadPassword("Contraseña de la clave privada: "));
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    Console.WriteLine();
    for (var i = 0; i < count; i++)
    {
        var id = "L-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        Console.WriteLine(LicenseKeyFormat.Issue(new License(id, licensee, parsedEdition, today), key));
    }

    return 0;
}

static int Verify(string publicKeyBase64, string license)
{
    using var key = ECDsa.Create();
    key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
    var check = LicenseKeyFormat.Verify(license, key);
    Console.WriteLine(check.IsValid
        ? $"Válida: {check.License!.Id} · {check.License.Licensee} · {check.License.Edition} · {check.License.Issued:yyyy-MM-dd}"
        : $"No válida: {check.Status}");
    return check.IsValid ? 0 : 2;
}

static string ReadPassword(string prompt)
{
    Console.Write(prompt);
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? string.Empty;
    }

    var password = new StringBuilder();
    while (true)
    {
        var keyInfo = Console.ReadKey(intercept: true);
        if (keyInfo.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return password.ToString();
        }

        if (keyInfo.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Length--;
            }
        }
        else if (!char.IsControl(keyInfo.KeyChar))
        {
            password.Append(keyInfo.KeyChar);
        }
    }
}

static string? Option(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string Required(string[] args, string name) =>
    Option(args, name) ?? throw new ArgumentException($"Falta {name}.");

static int Usage()
{
    Console.WriteLine("""
        Remote Clipboard - herramienta de licencias (solo para el autor)

          LicenseTool keygen [--out private-key.pem]
              Crea el par de claves. Muestra la clave pública para la app.

          LicenseTool issue --key private-key.pem --to "Nombre del cliente" [--edition personal|business] [--count N]
              Emite N claves de licencia a nombre del cliente.

          LicenseTool verify --public <clave pública base64> --license <RC1-...>
              Comprueba una clave.
        """);
    return 1;
}
