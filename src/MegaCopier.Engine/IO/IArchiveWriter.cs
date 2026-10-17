namespace MegaCopier.Engine.IO;

/// <summary>Formato del archivo comprimido en el que acaba una copia con <see cref="CopyOptions.Zip"/>.</summary>
public enum ArchiveFormat
{
    /// <summary>ZIP: lo abre cualquier programa (cifrado con AES-256 de WinZip, que abren 7-Zip, WinZip y WinRAR).</summary>
    Zip,

    /// <summary>7z: cifrado, oculta también los nombres de los archivos.</summary>
    SevenZip,
}

/// <summary>Lo común a escribir un ZIP o un 7z: añadir carpetas y archivos, terminar y releer para verificar.</summary>
internal interface IArchiveWriter
{
    void AddDirectory(string name, DateTime lastWriteUtc);

    void AddFile(string name, Stream source, DateTime lastWriteUtc, Action<ReadOnlySpan<byte>> onRead, CancellationToken cancellationToken);

    void Finish();

    /// <summary>
    /// Comprime (y cifra) un archivo aparte, en memoria, para poder hacer varios a la vez en distintos núcleos; luego se
    /// añaden en orden con <see cref="Append"/>. Para archivos pequeños y medianos.
    /// </summary>
    IPreparedEntry Prepare(string name, Stream source, DateTime lastWriteUtc, Action<ReadOnlySpan<byte>> onRead, CancellationToken cancellationToken);

    void Append(IPreparedEntry entry);

    /// <summary>Relee del disco lo escrito de un archivo (descifrando y descomprimiendo), para verificar la copia.</summary>
    void ReadBack(string name, Action<ReadOnlySpan<byte>> onData, CancellationToken cancellationToken);
}

/// <summary>Una entrada ya comprimida y cifrada, lista para añadirse al archivo.</summary>
internal interface IPreparedEntry
{
    long Size { get; }
}
