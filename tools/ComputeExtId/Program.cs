using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// Computes Chrome Extension ID from a PEM private key file
// Usage: dotnet run -- <path-to-pem> <output-dir>

string pemPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\tools\extension.pem");
string outputDir = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\build\extension");

// Read PEM and extract DER bytes
string pemContent = File.ReadAllText(pemPath);
string base64 = pemContent
    .Replace("-----BEGIN PRIVATE KEY-----", "")
    .Replace("-----END PRIVATE KEY-----", "")
    .Replace("-----BEGIN RSA PRIVATE KEY-----", "")
    .Replace("-----END RSA PRIVATE KEY-----", "")
    .Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();
byte[] privateKeyDer = Convert.FromBase64String(base64);

// Import and get public key
using var rsa = RSA.Create();
// Try PKCS#8 first, then RSA format
try { rsa.ImportPkcs8PrivateKey(privateKeyDer, out _); }
catch { rsa.ImportRSAPrivateKey(privateKeyDer, out _); }
byte[] publicKeyDer = rsa.ExportSubjectPublicKeyInfo();

// SHA256 of public key
byte[] hash = SHA256.HashData(publicKeyDer);

// Extension ID: first 16 bytes, each nibble -> 'a'..'p'
var sb = new StringBuilder(32);
for (int i = 0; i < 16; i++)
{
    sb.Append((char)('a' + ((hash[i] >> 4) & 0xF)));
    sb.Append((char)('a' + (hash[i] & 0xF)));
}
string extensionId = sb.ToString();

// Public key in Base64 (for manifest.json "key" field)
string publicKeyBase64 = Convert.ToBase64String(publicKeyDer);

Console.WriteLine($"Extension ID: {extensionId}");
Console.WriteLine($"Public Key (base64): {publicKeyBase64}");

// Save outputs
Directory.CreateDirectory(outputDir);
File.WriteAllText(Path.Combine(outputDir, "extension_id.txt"), extensionId);

// Generate updates.xml
string updatesXml = $@"<?xml version='1.0' encoding='UTF-8'?>
<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>
  <app appid='{extensionId}'>
    <updatecheck codebase='file:///C:/Program Files/AppBlocker/Extension/appblocker.crx' version='1.0.0' />
  </app>
</gupdate>";
File.WriteAllText(Path.Combine(outputDir, "updates.xml"), updatesXml, Encoding.UTF8);

Console.WriteLine($"Saved to: {outputDir}");
