using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Tests;

/// <summary>Las pruebas comprueban los mensajes en español: el motor habla en el idioma de la interfaz.</summary>
internal static class TestCulture
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void UseSpanish()
    {
        var spanish = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = spanish;
        System.Globalization.CultureInfo.CurrentUICulture = spanish;
    }
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "megacopier-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            // Sin seguir enlaces ni uniones: algunas pruebas crean bucles a propósito.
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(Path, "*", options))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            RemoveLinks(Path);
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Quita los enlaces (solo el enlace) antes de borrar, sin entrar en ellos.</summary>
    private static void RemoveLinks(string directory)
    {
        foreach (string child in Directory.EnumerateDirectories(directory, "*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(child);
            }
            else
            {
                RemoveLinks(child);
            }
        }
    }
}

/// <summary>Simula un corte de luz: aborta la copia en seco en el punto indicado.</summary>
internal sealed class SimulatedPowerLoss : Exception;

internal sealed class CrashAt(FaultPoint point, long chunkIndex) : IFaultInjector
{
    public void Hit(FaultPoint at, long chunk, SafeFileHandle destination)
    {
        if (at == point && chunk == chunkIndex)
        {
            throw new SimulatedPowerLoss();
        }
    }
}

/// <summary>Justo antes de verificar, estropea 4 KB del destino en cada desplazamiento indicado.</summary>
internal sealed unsafe class CorruptBeforeVerify(params long[] offsets) : IFaultInjector
{
    public void Hit(FaultPoint at, long chunk, SafeFileHandle destination)
    {
        if (at != FaultPoint.BeforeVerify)
        {
            return;
        }

        void* memory = NativeMemory.AlignedAlloc(4096, 4096);
        try
        {
            var span = new Span<byte>(memory, 4096);
            span.Fill(0xEE);
            foreach (long offset in offsets)
            {
                RandomAccess.Write(destination, span, offset);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
    }
}

internal static class TestFiles
{
    public static void Create(string path, long length, int seed)
    {
        var random = new Random(seed);
        var buffer = new byte[64 * 1024];
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        long remaining = length;
        while (remaining > 0)
        {
            random.NextBytes(buffer);
            int count = (int)Math.Min(buffer.Length, remaining);
            stream.Write(buffer, 0, count);
            remaining -= count;
        }
    }

    public static void AssertSameContent(string expected, string actual)
    {
        Assert.True(File.Exists(actual), $"No existe {actual}");
        Assert.Equal(new FileInfo(expected).Length, new FileInfo(actual).Length);
        Assert.True(File.ReadAllBytes(expected).AsSpan().SequenceEqual(File.ReadAllBytes(actual)), $"El contenido de {actual} no coincide.");
    }
}
