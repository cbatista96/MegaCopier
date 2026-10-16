using System.Buffers.Binary;
using System.Text;

namespace MegaCopier.Engine.Tests;

public sealed class PhotoTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>JPEG mínimo con EXIF (TIFF little-endian: IFD0 → subIFD EXIF → DateTimeOriginal).</summary>
    internal static byte[] Jpeg(string dateTimeOriginal, byte salt = 0)
    {
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange(LE16(42));
        tiff.AddRange(LE32(8));
        tiff.AddRange(LE16(1)); // IFD0: una entrada
        tiff.AddRange(LE16(0x8769));
        tiff.AddRange(LE16(4));
        tiff.AddRange(LE32(1));
        tiff.AddRange(LE32(26)); // el subIFD empieza en 26
        tiff.AddRange(LE32(0));
        tiff.AddRange(LE16(1)); // subIFD: una entrada
        tiff.AddRange(LE16(0x9003));
        tiff.AddRange(LE16(2));
        tiff.AddRange(LE32(20));
        tiff.AddRange(LE32(44)); // el texto empieza en 44
        tiff.AddRange(LE32(0));
        tiff.AddRange(Encoding.ASCII.GetBytes(dateTimeOriginal + "\0"));
        var app1 = new List<byte>();
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(tiff);
        var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1 };
        jpeg.AddRange(BE16(app1.Count + 2));
        jpeg.AddRange(app1);
        jpeg.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x04, salt, 0x00, 0xFF, 0xD9 });
        return [.. jpeg];
    }

    /// <summary>MP4 mínimo: ftyp + moov/mvhd con la fecha de creación.</summary>
    internal static byte[] Mp4(DateTime createdUtc)
    {
        uint seconds = (uint)(createdUtc - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        var mvhd = new List<byte>();
        mvhd.AddRange(BE32(8 + 12));
        mvhd.AddRange("mvhd"u8.ToArray());
        mvhd.AddRange(new byte[4]); // versión 0
        mvhd.AddRange(BE32((int)seconds));
        mvhd.AddRange(BE32((int)seconds));
        var mp4 = new List<byte>();
        mp4.AddRange(BE32(16));
        mp4.AddRange("ftypisom"u8.ToArray());
        mp4.AddRange(new byte[4]);
        mp4.AddRange(BE32(8 + mvhd.Count));
        mp4.AddRange("moov"u8.ToArray());
        mp4.AddRange(mvhd);
        return [.. mp4];
    }

    private static byte[] LE16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); return b; }

    private static byte[] LE32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); return b; }

    private static byte[] BE16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); return b; }

    private static byte[] BE32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, v); return b; }

    private string Write(string relative, byte[] data)
    {
        string path = _temp.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void ReadsTheDateThePhotoOrVideoWasTaken()
    {
        Assert.Equal(new DateTime(2024, 7, 15, 18, 30, 5), PhotoDate.Read(Write("a.jpg", Jpeg("2024:07:15 18:30:05"))));
        Assert.Equal(new DateTime(2022, 5, 5, 10, 0, 0, DateTimeKind.Utc).ToLocalTime(), PhotoDate.Read(Write("v.mp4", Mp4(new DateTime(2022, 5, 5, 10, 0, 0, DateTimeKind.Utc)))));
        Assert.Null(PhotoDate.Read(Write("n.txt", "hola"u8.ToArray())));
    }

    [Fact]
    public void OrganizesByDateTakenWithoutSourceFolders()
    {
        Write("camara/DCIM/100/IMG_1.jpg", Jpeg("2024:07:15 18:30:05", 1));
        Write("camara/DCIM/101/IMG_1.jpg", Jpeg("2024:07:20 09:00:00", 2)); // mismo nombre, mismo mes
        Write("camara/DCIM/101/IMG_2.jpg", Jpeg("2023:01:02 12:00:00", 3));
        Write("camara/VID_1.mp4", Mp4(new DateTime(2022, 5, 5, 10, 0, 0, DateTimeKind.Utc)));
        string note = Write("camara/nota.txt", "sin fecha"u8.ToArray());
        File.SetLastWriteTime(note, new DateTime(2021, 3, 3));

        var options = new CopyOptions { Mode = CopyMode.OrganizeByDate, ProgressInterval = TimeSpan.Zero };
        var result = new CopyEngine(options).Run(new CopyJobRequest([_temp.Combine("camara")], _temp.Combine("fotos")));

        Assert.True(result.Success);
        Assert.True(File.Exists(_temp.Combine(@"fotos\2024\2024-07\IMG_1.jpg")));
        Assert.True(File.Exists(_temp.Combine(@"fotos\2024\2024-07\IMG_1 (2).jpg")));
        Assert.True(File.Exists(_temp.Combine(@"fotos\2023\2023-01\IMG_2.jpg")));
        Assert.True(File.Exists(_temp.Combine(@"fotos\2022\2022-05\VID_1.mp4")));
        Assert.True(File.Exists(_temp.Combine(@"fotos\2021\2021-03\nota.txt")));
        Assert.False(Directory.Exists(_temp.Combine(@"fotos\camara")));
    }

    [Fact]
    public void DuplicatesAreNotCopiedAgain()
    {
        byte[] beach = Jpeg("2024:07:15 18:30:05", 7);
        Write("src/playa.jpg", beach);
        Write("src/copia de playa.jpg", beach); // repetida en el propio origen
        Write("src/montaña.jpg", Jpeg("2024:08:01 10:00:00", 8));
        Write("dst/2023/vacaciones/IMG_9999.jpg", Jpeg("2024:08:01 10:00:00", 8)); // ya estaba, con otro nombre

        var options = new CopyOptions { SkipDuplicates = true, ProgressInterval = TimeSpan.Zero };
        var engine = new CopyEngine(options);
        var result = engine.Run(new CopyJobRequest([_temp.Combine("src")], _temp.Combine("dst")));

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(2, result.FilesSkipped);
        Assert.Single(Directory.GetFiles(_temp.Combine(@"dst\src")));
        Assert.All(engine.Files.Snapshot().Where(i => i.State == FileState.Skipped), i => Assert.Equal(PlannedAction.SkipDuplicate, i.Action));
    }
}
