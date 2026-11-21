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

    public static async Task<byte[]> ComputeAsync(VerifyAlgorithm algorithm, string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
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
        var buffer = new byte[1024 * 1024];
        var crc = 0xFFFFFFFFu;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            for (var i = 0; i < read; i++)
                crc = CrcTable[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
        crc ^= 0xFFFFFFFFu;
        return new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc };
    }

    public static string ToText(VerifyAlgorithm algorithm, byte[] hash)
    {
        if (algorithm == VerifyAlgorithm.None) return string.Empty;
        return Convert.ToHexString(hash);
    }

    public static bool Equal(byte[] left, byte[] right) => CryptographicOperations.FixedTimeEquals(left, right);
}
