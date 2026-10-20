using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MegaCopier.App.Services;

/// <summary>
/// La imagen con la que Windows muestra un archivo, como en el Explorador: la miniatura en fotos y vídeos, y en lo
/// demás (PDF, música, documentos...) el icono del programa que lo abre. Se pide al shell (IShellItemImageFactory) en un
/// hilo propio, así que un archivo grande no frena la ventana.
/// </summary>
internal static partial class ShellImage
{
    private static readonly Guid IidImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    private const int SiigbfResizeToFit = 0x0, SiigbfIconOnly = 0x4;

    /// <summary>La imagen del archivo a ese tamaño (en píxeles), o null si Windows no da ninguna.</summary>
    public static async Task<WriteableBitmap?> LoadAsync(string path, int size)
    {
        var pixels = await Task.Factory.StartNew(
            () => RunOnSta(() => Read(path, size, iconOnly: !HasPreview(path))),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (pixels is not var (width, height, data))
        {
            return null;
        }

        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(data);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>Fotos y vídeos se ven por su contenido; lo demás, por el icono de su programa.</summary>
    private static bool HasPreview(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length == 0)
        {
            return false;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(extension);
            string? perceived = key?.GetValue("PerceivedType") as string;
            return perceived is not null && (perceived.Equals("image", StringComparison.OrdinalIgnoreCase) || perceived.Equals("video", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static T? RunOnSta<T>(Func<T?> work)
    {
        T? result = default;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        })
        { IsBackground = true, Name = "MegaCopier.ShellImage" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>Los píxeles (BGRA premultiplicado, de arriba abajo) que da el shell.</summary>
    private static unsafe (int Width, int Height, byte[] Data)? Read(string path, int size, bool iconOnly)
    {
        void* factory = null;
        Guid iid = IidImageFactory;
        if (SHCreateItemFromParsingName(path, null, &iid, &factory) < 0 || factory == null)
        {
            return null;
        }

        nint bitmap = 0;
        try
        {
            // IShellItemImageFactory::GetImage (índice 3)
            var getImage = (delegate* unmanaged[Stdcall]<void*, Size, int, nint*, int>)(*(void***)factory)[3];
            int flags = iconOnly ? SiigbfIconOnly : SiigbfResizeToFit;
            if (getImage(factory, new Size(size, size), flags, &bitmap) < 0 || bitmap == 0)
            {
                if (iconOnly || getImage(factory, new Size(size, size), SiigbfIconOnly, &bitmap) < 0 || bitmap == 0)
                {
                    return null;
                }
            }

            return Pixels(bitmap);
        }
        finally
        {
            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)factory)[2])(factory); // Release
        }
    }

    private static unsafe (int, int, byte[])? Pixels(nint bitmap)
    {
        Bitmap info;
        if (GetObjectW(bitmap, sizeof(Bitmap), &info) == 0 || info.Width <= 0 || info.Height == 0)
        {
            return null;
        }

        int width = info.Width, height = Math.Abs(info.Height);
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height, // de arriba abajo
            Planes = 1,
            BitCount = 32,
        };
        var data = new byte[width * height * 4];
        nint dc = GetDC(0);
        try
        {
            fixed (byte* buffer = data)
            {
                if (GetDIBits(dc, bitmap, 0, (uint)height, buffer, &header, 0 /* DIB_RGB_COLORS */) == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            ReleaseDC(0, dc);
        }

        // Sin canal alfa (algunas miniaturas antiguas): opaca.
        bool anyAlpha = false;
        for (int i = 3; i < data.Length && !anyAlpha; i += 4)
        {
            anyAlpha = data[i] != 0;
        }

        if (!anyAlpha)
        {
            for (int i = 3; i < data.Length; i += 4)
            {
                data[i] = 255;
            }
        }

        return (width, height, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Size(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public uint Colors; // una entrada de la paleta: GetDIBits la espera aunque no se use
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHCreateItemFromParsingName(string path, void* context, Guid* iid, void** item);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetObjectW(nint handle, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BitmapInfoHeader* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);
}
