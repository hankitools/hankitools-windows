using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;

namespace Hanki.Build
{
    // C# 5 compatible so Windows PowerShell 5.1 can compile the same implementation tested by the check harness.
    public static class ArchiveBuilder
    {
        public static void Create(string sourceDirectory, string destinationZip)
        {
            string source = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(destinationZip);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
            if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("The ZIP must be outside the source directory.");
            if (File.Exists(destination)) throw new IOException("Destination ZIP exists; nothing overwritten.");
            string staging = Path.Combine(Path.GetTempPath(), "Hanki-archive-" + Guid.NewGuid().ToString("N"));
            string partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
            Directory.CreateDirectory(staging);
            try
            {
                CopyTree(source, staging);
                ZipFile.CreateFromDirectory(staging, partial, CompressionLevel.Optimal, false);
                using (ZipArchive archive = ZipFile.OpenRead(partial))
                    foreach (ZipArchiveEntry entry in archive.Entries)
                        using (Stream stream = entry.Open()) stream.CopyTo(Stream.Null);
                File.Move(partial, destination);
            }
            finally
            {
                if (File.Exists(partial)) File.Delete(partial);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }
        private static void CopyTree(string source, string target)
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Archive source contains a link or reparse point.");
            Directory.CreateDirectory(target);
            foreach (string path in Directory.GetFileSystemEntries(source))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Archive source contains a link or reparse point.");
                string copy = Path.Combine(target, Path.GetFileName(path));
                if ((attributes & FileAttributes.Directory) != 0) CopyTree(path, copy);
                else CopyVerified(path, copy);
            }
        }
        private static void CopyVerified(string source, string target)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (SHA256 hash = SHA256.Create())
                    {
                        byte[] before = hash.ComputeHash(input);
                        input.Position = 0;
                        using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                        byte[] copied;
                        using (FileStream output = File.OpenRead(target)) copied = hash.ComputeHash(output);
                        input.Position = 0;
                        byte[] after = hash.ComputeHash(input);
                        if (!Equal(before, copied) || !Equal(before, after)) throw new IOException("A source file changed while packaging. Retry after the build has finished.");
                    }
                    return;
                }
                catch (IOException ex)
                {
                    int code = ex.HResult & 0xffff;
                    if (attempt >= 9 || (code != 32 && code != 33)) throw;
                    Thread.Sleep(500);
                }
            }
        }
        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
