using System.IO.Enumeration;

namespace MegaCopier.Engine;

/// <summary>
/// Archivo de exclusiones «.mcignore» (como .gitignore, el .stignore de Syncthing o los filtros de rclone): se deja en una
/// carpeta y lo que dice no se copia nunca, de ella ni de sus subcarpetas, sin configurarlo en cada copia.
/// Una máscara por línea: «*.tmp», «node_modules/» (solo carpetas), «/build» (solo en esa carpeta, no más abajo),
/// «fotos/*.raw». Las líneas vacías y las que empiezan por # no cuentan.
/// </summary>
public static class IgnoreFile
{
    public const string Name = ".mcignore";

    /// <summary>Las máscaras del archivo, o null si no existe o no se puede leer.</summary>
    public static List<string>? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return [.. File.ReadLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && l[0] != '#' && l[0] != '!')
                .Select(l => l.Replace('\\', '/'))]; // se compara con «/»: «\» es el carácter de escape de las máscaras
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>La ruta queda fuera por algún .mcignore de las carpetas que la contienen.</summary>
    public static bool Ignored(Stack ignores, string path, bool isDirectory)
    {
        foreach (var (folder, masks) in ignores.Items)
        {
            string relative = Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');
            string name = Path.GetFileName(path);
            foreach (string raw in masks)
            {
                string mask = raw;
                bool onlyDirectories = mask.EndsWith('/');
                if (onlyDirectories)
                {
                    if (!isDirectory)
                    {
                        continue;
                    }

                    mask = mask.TrimEnd('/');
                }

                // «/build»: solo junto al .mcignore. Sin barra: en cualquier nivel por su nombre, o por la ruta.
                bool anchored = mask.StartsWith('/');
                mask = mask.TrimStart('/');
                if (mask.Length == 0)
                {
                    continue;
                }

                bool matches = anchored || mask.Contains('/')
                    ? FileSystemName.MatchesSimpleExpression(mask, relative)
                    : FileSystemName.MatchesSimpleExpression(mask, name);
                if (matches)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Los .mcignore activos mientras se recorre: el de cada carpeta vale para todo lo que tiene debajo.</summary>
    public sealed class Stack
    {
        private readonly List<(string Folder, List<string> Masks)> _items = [];

        public IReadOnlyList<(string Folder, List<string> Masks)> Items => _items;

        public int Count => _items.Count;

        public bool Add(string folder, List<string> masks)
        {
            _items.Add((folder, masks));
            return true;
        }

        public void RemoveLast() => _items.RemoveAt(_items.Count - 1);
    }
}
