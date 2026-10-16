using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MegaCopier.Engine;

/// <summary>
/// Fecha en que se hizo una foto o un vídeo, en el mismo orden que Windows ("Fecha de captura"): EXIF DateTimeOriginal,
/// luego DateTimeDigitized, luego las fechas de creación del XMP y, solo al final, la de la última edición. Sirve para
/// JPEG, los RAW basados en TIFF (CR2, NEF, ARW, DNG, ORF, RW2, TIFF) y MP4/MOV/M4V/3GP (fecha de grabación).
/// Lee solo el principio del archivo.
/// </summary>
public static partial class PhotoDate
{
    private const int HeadSize = 256 * 1024;

    public static DateTime? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            byte[] head = new byte[(int)Math.Min(HeadSize, stream.Length)];
            stream.ReadExactly(head);
            if (head.Length >= 4 && head[0] == 0xFF && head[1] == 0xD8)
            {
                return FromJpeg(head);
            }

            if (head.Length >= 8 && (head.AsSpan(0, 2).SequenceEqual("II"u8) || head.AsSpan(0, 2).SequenceEqual("MM"u8)))
            {
                var (taken, edited) = Exif(head, 0);
                return taken ?? FromXmp(Encoding.UTF8.GetString(head)) ?? edited;
            }

            if (head.Length >= 12 && head.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            {
                return FromQuickTime(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return null;
    }

    /// <summary>JPEG: APP1 "Exif" (un TIFF dentro) y APP1 XMP.</summary>
    private static DateTime? FromJpeg(byte[] data)
    {
        DateTime? edited = null;
        DateTime? xmp = null;
        int position = 2;
        while (position + 4 <= data.Length && data[position] == 0xFF)
        {
            byte marker = data[position + 1];
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 2));
            if (marker == 0xE1 && position + 10 <= data.Length && data.AsSpan(position + 4, 6).SequenceEqual("Exif\0\0"u8))
            {
                var (taken, modified) = Exif(data, position + 10);
                if (taken is not null)
                {
                    return taken;
                }

                edited ??= modified;
            }
            else if (marker == 0xE1 && length > 2 && position + 2 + length <= data.Length)
            {
                xmp ??= FromXmp(Encoding.UTF8.GetString(data, position + 4, length - 2));
            }

            if (marker is 0xDA or 0xD9)
            {
                break; // empiezan los datos de la imagen: ya no hay más metadatos
            }

            position += 2 + length;
        }

        return xmp ?? edited;
    }

    /// <summary>TIFF: IFD0 → subIFD EXIF (0x8769) → DateTimeOriginal (0x9003) o DateTimeDigitized (0x9004); y DateTime (0x0132).</summary>
    private static (DateTime? Taken, DateTime? Edited) Exif(byte[] data, int start)
    {
        if (start + 8 > data.Length)
        {
            return (null, null);
        }

        bool little = data[start] == (byte)'I';
        uint U32(int at) => at + 4 <= data.Length ? (little ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at))) : 0;
        ushort U16(int at) => at + 2 <= data.Length ? (little ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at))) : (ushort)0;

        DateTime? original = null, digitized = null, edited = null;
        void Search(uint ifd, int depth)
        {
            int at = start + (int)ifd;
            if (depth > 2 || ifd == 0 || at + 2 > data.Length)
            {
                return;
            }

            int count = U16(at);
            for (int i = 0; i < count && at + 2 + (i * 12) + 12 <= data.Length; i++)
            {
                int entry = at + 2 + (i * 12);
                switch (U16(entry))
                {
                    case 0x9003:
                        original ??= Ascii(data, start + (int)U32(entry + 8), (int)U32(entry + 4));
                        break;
                    case 0x9004:
                        digitized ??= Ascii(data, start + (int)U32(entry + 8), (int)U32(entry + 4));
                        break;
                    case 0x0132:
                        edited ??= Ascii(data, start + (int)U32(entry + 8), (int)U32(entry + 4));
                        break;
                    case 0x8769:
                        Search(U32(entry + 8), depth + 1);
                        break;
                }
            }
        }

        Search(U32(start + 4), 0);
        return (original ?? digitized, edited);
    }

    /// <summary>XMP: exif:DateTimeOriginal, photoshop:DateCreated o xmp:CreateDate (como atributo o como elemento).</summary>
    private static DateTime? FromXmp(string xml)
    {
        foreach (string name in (string[])["exif:DateTimeOriginal", "photoshop:DateCreated", "xmp:CreateDate"])
        {
            var match = Regex.Match(xml, Regex.Escape(name) + @"(?:=""|>)(?<d>\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2})?)?)");
            if (match.Success && DateTime.TryParse(match.Groups["d"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1900)
            {
                return date;
            }
        }

        return null;
    }

    private static DateTime? Ascii(byte[] data, int at, int length)
    {
        if (at < 0 || length < 19 || at + 19 > data.Length)
        {
            return null;
        }

        string text = Encoding.ASCII.GetString(data, at, 19);
        return DateTime.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1900
            ? date
            : null;
    }

    /// <summary>MP4/MOV: "moov" → "mvhd" lleva la fecha de creación (segundos desde 1904, en UTC).</summary>
    private static DateTime? FromQuickTime(FileStream stream)
    {
        var header = new byte[16];
        long position = 0;
        while (position + 8 <= stream.Length)
        {
            stream.Position = position;
            stream.ReadExactly(header.AsSpan(0, 8));
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = Encoding.ASCII.GetString(header, 4, 4);
            int headerSize = 8;
            if (size == 1)
            {
                stream.ReadExactly(header.AsSpan(8, 8));
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = stream.Length - position;
            }

            if (size < headerSize)
            {
                return null;
            }

            if (type == "moov")
            {
                position += headerSize; // se entra en "moov"
                continue;
            }

            if (type == "mvhd")
            {
                var body = new byte[12];
                stream.ReadExactly(body);
                ulong seconds = body[0] == 1
                    ? BinaryPrimitives.ReadUInt64BigEndian(body.AsSpan(4))
                    : BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(4));
                return seconds > 0 ? new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime() : null;
            }

            position += size;
        }

        return null;
    }
}
