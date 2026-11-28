using System.Security.Cryptography;

namespace XoPy;

public static class Hashing
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }

    /// <summary>
    /// EL BÚFER SE AJUSTA AL ARCHIVO. Con un megabyte fijo, verificar tres mil archivos de
    /// dos kilobytes pedía (y tiraba) tres gigabytes de memoria, y el recolector de basura
    /// paraba la copia cada dos por tres: así es como copiar una carpeta con muchos
    /// archivos pequeños se volvía «un poco, parón, un poco». Además, para archivos
    /// pequeños la lectura asíncrona cuesta más de lo que ahorra, así que no se usa.
    /// </summary>
    public static async Task<byte[]> ComputeAsync(VerifyAlgorithm algorithm, string path, CancellationToken token)
    {
        long length = 0;
        try { length = new FileInfo(path).Length; } catch { }
        var buffer = (int)Math.Clamp(length > 0 ? length : 64L * 1024, 4 * 1024, 1024 * 1024);
        var mode = FileOptions.SequentialScan;
        if (length > 1024 * 1024) mode |= FileOptions.Asynchronous;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, buffer, mode);
        return algorithm switch
        {
            VerifyAlgorithm.Crc32 => await Crc32Async(stream, token).ConfigureAwait(false),
            VerifyAlgorithm.Md5 => await MD5.HashDataAsync(stream, token).ConfigureAwait(false),
            VerifyAlgorithm.Sha1 => await SHA1.HashDataAsync(stream, token).ConfigureAwait(false),
            VerifyAlgorithm.Sha512 => await SHA512.HashDataAsync(stream, token).ConfigureAwait(false),
            _ => await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)
        };
    }

    private static async Task<byte[]> Crc32Async(Stream stream, CancellationToken token)
    {
        var size = 1024 * 1024;
        try { if (stream.CanSeek && stream.Length > 0) size = (int)Math.Clamp(stream.Length, 4 * 1024, 1024 * 1024); } catch { }
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
        try
        {
            var crc = 0xFFFFFFFFu;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, size), token).ConfigureAwait(false)) > 0)
                for (var i = 0; i < read; i++)
                    crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            crc ^= 0xFFFFFFFFu;
            return new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc };
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
    }

    public static string ToText(VerifyAlgorithm algorithm, byte[] hash)
    {
        if (algorithm == VerifyAlgorithm.None) return string.Empty;
        return Convert.ToHexString(hash);
    }

    public static bool Equal(byte[] left, byte[] right) => CryptographicOperations.FixedTimeEquals(left, right);
}
