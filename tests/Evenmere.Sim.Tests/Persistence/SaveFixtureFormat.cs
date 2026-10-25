using System.Text;

namespace Evenmere.Sim.Tests.Persistence;

/// <summary>
/// Reads a save file's envelope version without content: the four magic bytes <c>EVMR</c> and the little-endian
/// <c>uint16</c> format version that follows them (ADR-0007). Tests use this to tell frozen old-version fixtures apart
/// from current-version goldens, since only the envelope carries the version before the content tables are read.
/// </summary>
internal static class SaveFixtureFormat
{
    /// <summary>Reads the format version a save file was written with.</summary>
    /// <param name="path">The save file.</param>
    /// <returns>The envelope version.</returns>
    /// <exception cref="InvalidDataException">The file is too short or lacks the magic.</exception>
    internal static ushort ReadEnvelopeVersion(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] prefix = new byte[6];
        using (FileStream stream = File.OpenRead(path))
        {
            int read = 0;
            while (read < prefix.Length)
            {
                int got = stream.Read(prefix.AsSpan(read));
                if (got == 0)
                {
                    throw new InvalidDataException($"The save {path} ends before its envelope version.");
                }

                read += got;
            }
        }

        if (!prefix.AsSpan(0, 4).SequenceEqual(Encoding.ASCII.GetBytes("EVMR")))
        {
            throw new InvalidDataException($"The save {path} lacks the EVMR magic.");
        }

        return (ushort)(prefix[4] | (prefix[5] << 8));
    }
}
