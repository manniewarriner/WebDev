using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace OrchDesk.App.Views;

public enum FileNodeKind { Folder, File, Info }

/// <summary>A row in <see cref="FileExplorer"/>'s tree. Folder children are read on first expand.</summary>
public sealed class FileNode : INotifyPropertyChanged
{
    public const int MaxEntriesPerFolder = 500;
    public const double IndentPerLevel = 14;
    public const string FolderGlyph = "\uED25";
    public const string FileGlyph = "\uE8A5";

    /// <summary>Names that are never listed or watched.</summary>
    public static readonly HashSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj", "node_modules", ".vs" };

    private bool isExpanded;
    private bool childrenLoaded;

    public FileNode(string fullPath, string name, FileNodeKind kind, int level)
    {
        FullPath = fullPath;
        Name = name;
        Kind = kind;
        Level = level;
        // Placeholder gives the folder an expander before it is read.
        if (kind == FileNodeKind.Folder)
            Children.Add(new FileNode(string.Empty, string.Empty, FileNodeKind.Info, level + 1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; }
    public string Name { get; }
    public FileNodeKind Kind { get; }
    public int Level { get; }
    public bool IsFolder => Kind == FileNodeKind.Folder;
    public bool IsFile => Kind == FileNodeKind.File;
    public Thickness Indent => new(Level * IndentPerLevel, 0, 0, 0);
    public string Glyph => Kind switch
    {
        FileNodeKind.Folder => FolderGlyph,
        FileNodeKind.File => FileGlyph,
        _ => string.Empty,
    };
    public ObservableCollection<FileNode> Children { get; } = new();

    /// <summary>Expanding a folder for the first time reads its children.</summary>
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value) return;
            isExpanded = value;
            if (value && IsFolder && !childrenLoaded) LoadChildren();
            Raise();
        }
    }

    private void LoadChildren()
    {
        childrenLoaded = true;
        var items = ReadDirectory(FullPath, Level + 1);
        if (items.Count == 0) items.Add(new FileNode(string.Empty, "(empty)", FileNodeKind.Info, Level + 1));
        Children.Clear();
        foreach (var item in items) Children.Add(item);
    }

    /// <summary>
    /// Lists one folder: folders first, then files, alphabetical, skipping
    /// <see cref="SkippedNames"/> and reparse points (symlinks/junctions), capped at
    /// <see cref="MaxEntriesPerFolder"/> with a trailing "more" row.
    /// </summary>
    public static List<FileNode> ReadDirectory(string directory, int level)
    {
        var result = new List<FileNode>();
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
            var entries = new List<FileSystemInfo>();
            foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
            {
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue; // never follow links
                if (SkippedNames.Contains(info.Name)) continue;
                entries.Add(info);
            }

            entries = entries
                .OrderBy(e => e is DirectoryInfo ? 0 : 1)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int shown = Math.Min(entries.Count, MaxEntriesPerFolder);
            for (int i = 0; i < shown; i++)
            {
                var entry = entries[i];
                var kind = entry is DirectoryInfo ? FileNodeKind.Folder : FileNodeKind.File;
                result.Add(new FileNode(entry.FullName, entry.Name, kind, level));
            }
            if (entries.Count > shown)
                result.Add(new FileNode(string.Empty, $"… {entries.Count - shown:N0} more", FileNodeKind.Info, level));
        }
        catch (UnauthorizedAccessException)
        {
            result.Clear();
            result.Add(new FileNode(string.Empty, "(access denied)", FileNodeKind.Info, level));
        }
        catch (IOException)
        {
            result.Clear();
            result.Add(new FileNode(string.Empty, "(unavailable)", FileNodeKind.Info, level));
        }
        return result;
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
