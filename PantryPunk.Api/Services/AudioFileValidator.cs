using System.Text;

namespace PantryPunk.Api.Services;

public static class AudioFileValidator
{
    // Accepted major brands in the ISO-BMFF 'ftyp' box for m4a/mp4 audio containers.
    private static readonly HashSet<string> AllowedBrands = new(StringComparer.Ordinal)
    {
        "M4A ", "mp42", "mp41", "isom", "iso2", "avc1", "f4v ", "M4V "
    };

    /// <summary>
    /// Checks whether the first bytes of an audio stream match an ISO Base Media File Format
    /// container (MP4/M4A). Returns false for anything that is not a valid ftyp box.
    /// </summary>
    public static bool IsIsoBmff(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12) return false;

        // Bytes 4–7 must be the ASCII string "ftyp"
        if (header[4] != (byte)'f' || header[5] != (byte)'t'
            || header[6] != (byte)'y' || header[7] != (byte)'p')
        {
            return false;
        }

        var brand = Encoding.ASCII.GetString(header.Slice(8, 4));
        return AllowedBrands.Contains(brand);
    }
}
