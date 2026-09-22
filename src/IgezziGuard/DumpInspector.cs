using System.Text;

namespace IgezziGuard;

public static class DumpInspector
{
    // Bounded structure inspection only. No dump content is executed or deserialized into objects.
    public static string Inspect(Stream file)
    {
        if (!file.CanSeek || file.Length < 32) throw new IOException("File too small for a supported dump header.");
        using var reader = new BinaryReader(file, Encoding.Unicode, true);
        void Range(long offset, long size) { if (offset < 0 || size < 0 || offset > file.Length || size > file.Length - offset) throw new IOException("Dump references data outside the file (truncated or malformed)."); }
        uint U32(long offset) { Range(offset, 4); file.Position = offset; return reader.ReadUInt32(); }
        ulong U64(long offset) { Range(offset, 8); file.Position = offset; return reader.ReadUInt64(); }
        uint signature = U32(0);
        if (signature == 0x45474150) return "Windows kernel dump header detected (PAGE). Built-in stream inspection supports MDMP minidumps; use local KD analysis for kernel bugcheck/stack evidence. Header detection alone does not establish a valid complete dump.";
        if (signature != 0x504d444d) throw new IOException("Not a recognized Windows MDMP or PAGE dump.");
        if ((U32(4) & 0xffff) != 0xa793) throw new IOException("Unsupported minidump format version; use a current Microsoft debugger.");
        uint count = U32(8), directory = U32(12);
        if (count > 4096) throw new IOException("Excessive stream count."); Range(directory, count * 12L);
        var streams = new Dictionary<uint, (uint Size, uint Rva)>();
        for (int i = 0; i < count; i++) { long at = directory + i * 12L; var type = U32(at); var size = U32(at + 4); var rva = U32(at + 8); Range(rva, size); if (!streams.TryAdd(type, (size, rva))) throw new IOException("Duplicate stream type; inspect with debugger."); }
        var report = new StringBuilder($"MDMP minidump: {count} streams, {file.Length:N0} bytes\r\nHeader timestamp: {DateTimeOffset.FromUnixTimeSeconds(U32(20)):O}\r\n");
        ulong? address = null;
        if (streams.TryGetValue(6, out var exception)) {
            if (exception.Size < 168) throw new IOException("Truncated exception stream.");
            var code = U32(exception.Rva + 8L); address = U64(exception.Rva + 24L);
            report.AppendLine($"Exception thread: {U32(exception.Rva)}\r\nException code: 0x{code:X8}\r\nException address: 0x{address:X16}");
            report.AppendLine(code == 0xc0000005 ? "Access violation: a memory access failed. This alone does not distinguish an application bug, driver issue or hardware instability." : "Use the exception code with the stack and symbols; this is not a root-cause verdict.");
        } else report.AppendLine("No exception stream present; absence is not proof there was no fault.");
        if (streams.TryGetValue(4, out var modules)) {
            if (modules.Size < 4) throw new IOException("Truncated module stream."); var total = U32(modules.Rva);
            if (total > 10000 || 4L + total * 108L > modules.Size) throw new IOException("Invalid module count/size.");
            report.AppendLine($"\r\nLoaded module snapshot: {total} entries (display first 80 plus any containing exception address)");
            for (uint i = 0; i < total; i++) {
                long at = modules.Rva + 4L + i * 108L; ulong start = U64(at); uint size = U32(at + 8);
                bool contains = address.HasValue && address >= start && address.Value - start < size;
                if (i >= 80 && !contains) continue;
                uint nameAt = U32(at + 20); uint bytes = U32(nameAt);
                if (bytes > 32768 || bytes % 2 != 0) throw new IOException("Invalid module-name string."); Range(nameAt + 4L, bytes); file.Position = nameAt + 4L;
                string name = Encoding.Unicode.GetString(reader.ReadBytes((int)bytes));
                report.AppendLine($"{(contains ? "AT EXCEPTION ADDRESS: " : "")}{name}  base 0x{start:X}, size {size:N0}");
            }
        }
        report.AppendLine("\r\nThis is structural triage, not symbolized stack analysis. A module containing the exception address is not necessarily responsible. Names/paths and memory dumps can contain sensitive information. No upload performed.");
        return report.ToString();
    }
}
