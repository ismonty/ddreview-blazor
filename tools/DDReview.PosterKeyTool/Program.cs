using System.Security.Cryptography;
using System.Text;
using DDReview.QuickBooks.Launch;

// Makes the key pair that lets the website sign "Post to QuickBooks" links and the poster check them.
//   dotnet run --project tools/DDReview.PosterKeyTool -- <key id> [folder for the private key file]
// The private key goes into a file (never onto the screen); the public key is printed for deploy/poster-trusted-keys.json.

if (args.Length < 1 || args[0] is "-h" or "--help" or "/?")
{
    Console.WriteLine("Usage: PosterKeyTool <key id> [folder]");
    Console.WriteLine("  key id : 1 to 24 characters, a-z 0-9 and '-', for example prod1 or staging1 (use a different one per site).");
    Console.WriteLine("  folder : where to write <key id>.signing-key.private.txt (default: the current folder).");
    return args.Length < 1 ? 1 : 0;
}

string kid = args[0];
if (!PosterLinkRules.IsValidKeyId(kid)) { Console.Error.WriteLine("The key id must be 1 to 24 characters: a-z, 0-9 and '-'."); return 1; }
string folder = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(folder);
string file = Path.Combine(folder, kid + ".signing-key.private.txt");

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
try
{
    using var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
    fs.Write(Encoding.ASCII.GetBytes(Convert.ToBase64String(key.ExportPkcs8PrivateKey())));
}
catch (IOException) when (File.Exists(file)) { Console.Error.WriteLine($"{file} already exists. Nothing was changed; pick another key id or folder."); return 1; }

Console.WriteLine($"Private key written to: {file}");
Console.WriteLine("  Put its contents in the website's secret settings as PosterLink:SigningKeyPkcs8 (with PosterLink:KeyId = " + kid + "),");
Console.WriteLine("  then delete the file. It is the only copy. Never commit it or send it by email or chat.");
Console.WriteLine();
Console.WriteLine("Public key (not secret): add this entry to the \"keys\" list in deploy/poster-trusted-keys.json, commit it, and publish the poster again:");
Console.WriteLine($"    {{ \"kid\": \"{kid}\", \"publicKey\": \"{TrustedKey.ExportPublicBase64(key)}\" }}");
return 0;
