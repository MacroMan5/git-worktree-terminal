using System.IO;

namespace tmuxlike.Services;

/// <summary>
/// Represents a file or directory entry in the file explorer tree.
/// </summary>
public class FileItem
{
    /// <summary>The file or directory name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The absolute filesystem path.</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>Whether this item is a directory.</summary>
    public bool IsDirectory { get; set; }

    /// <summary>Child items for directories, or null for files.</summary>
    public List<FileItem>? Children { get; set; }

    /// <summary>Whether the directory is expanded in the tree view.</summary>
    public bool IsExpanded { get; set; }

    /// <summary>Whether the children list contains only the lazy-load placeholder.</summary>
    public bool HasDummyChild => Children?.Count == 1 && Children[0].Name == _dummyName;

    private const string _dummyName = "__dummy__";

    /// <summary>
    /// Creates a dummy placeholder child used for lazy-loading directory contents.
    /// </summary>
    public static FileItem CreateDummy() => new() { Name = _dummyName };
}

/// <summary>
/// Provides methods for reading directory contents for the file explorer panel.
/// </summary>
public static class FileExplorerService
{
    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "__pycache__", ".venv"
    };

    /// <summary>
    /// Returns the files and subdirectories of the given path, sorted alphabetically
    /// with directories first. Common build and IDE directories are excluded.
    /// </summary>
    /// <param name="path">The directory path to enumerate.</param>
    /// <returns>A list of <see cref="FileItem"/> entries, or an empty list if the path doesn't exist.</returns>
    public static List<FileItem> GetDirectoryItems(string path)
    {
        var items = new List<FileItem>();

        if (!Directory.Exists(path))
            return items;

        try
        {
            foreach (var dir in Directory.GetDirectories(path).OrderBy(d => System.IO.Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var name = System.IO.Path.GetFileName(dir);
                if (IgnoredDirs.Contains(name))
                    continue;

                var item = new FileItem
                {
                    Name = name,
                    FullPath = dir,
                    IsDirectory = true,
                    Children = [FileItem.CreateDummy()]
                };
                items.Add(item);
            }

            foreach (var file in Directory.GetFiles(path).OrderBy(f => System.IO.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                items.Add(new FileItem
                {
                    Name = System.IO.Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return items;
    }
}
