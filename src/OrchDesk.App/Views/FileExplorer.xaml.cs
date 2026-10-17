using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OrchDesk.App.Views;

/// <summary>Project file tree above a preview pane. Set <see cref="RootPath"/> to a folder.</summary>
public partial class FileExplorer : UserControl
{
    private const int MaxTextBytes = 200 * 1024;
    private const long MaxPreviewBytes = 5L * 1024 * 1024;
    private const int BinarySniffBytes = 8000;
    private const string SelectPrompt = "Select a file to preview.";
    private const string EmptyPrompt = "Choose a project folder to see its files.";

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "xaml", "json", "md", "txt", "js", "ts", "py", "lua", "luau", "html", "css", "xml",
        "csproj", "yml", "yaml", "toml", "ini", "log", "sh", "ps1", "sql",
    };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "webp",
    };

    private enum PreviewMode { Message, Text, Image, Info }

    public static readonly DependencyProperty RootPathProperty = DependencyProperty.Register(
        nameof(RootPath), typeof(string), typeof(FileExplorer),
        new PropertyMetadata(string.Empty, OnRootPathChanged));

    private readonly ObservableCollection<FileNode> roots = new();
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly HashSet<string> pendingChanged = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? watcher;
    private bool pendingStructure;
    private bool needsRebuild = true;
    private bool suppressSelection;
    private string? previewPath;

    public FileExplorer()
    {
        InitializeComponent();
        Tree.ItemsSource = roots;
        debounce.Tick += (_, _) => OnDebounced();
        Loaded += (_, _) =>
        {
            if (needsRebuild) RebuildTree();
            StartWatcher();
        };
        Unloaded += (_, _) => StopWatcher();
    }

    /// <summary>Folder shown in the tree. Changing it reloads the tree and restarts the watcher.</summary>
    public string RootPath
    {
        get => (string)GetValue(RootPathProperty);
        set => SetValue(RootPathProperty, value ?? string.Empty);
    }

    /// <summary>Raised with the full path when the user selects a file in the tree.</summary>
    public event EventHandler<string>? FileSelected;

    /// <summary>Re-reads the folder tree (expanded folders and selection are kept where possible).</summary>
    public void Refresh() => RebuildTree();

    private static void OnRootPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FileExplorer)d).HandleRootChanged();

    private void HandleRootChanged()
    {
        if (!IsLoaded)
        {
            needsRebuild = true;
            return;
        }
        StopWatcher();
        RebuildTree();
        StartWatcher();
    }

    // ----- Tree -------------------------------------------------------------

    private void RebuildTree()
    {
        needsRebuild = false;
        var root = NormalizedRoot();
        var keep = previewPath;
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpanded(roots, expanded);

        FolderName.Text = root == null ? string.Empty : FolderDisplayName(root);
        Tree.Visibility = ToVisibility(root != null);
        EmptyText.Visibility = ToVisibility(root == null);

        suppressSelection = true;
        try
        {
            roots.Clear();
            if (root != null)
            {
                foreach (var node in FileNode.ReadDirectory(root, 0))
                {
                    roots.Add(node);
                    ExpandRestored(node, expanded);
                }
                Tree.UpdateLayout();
                if (keep != null) RestoreSelection(keep);
            }
        }
        finally
        {
            suppressSelection = false;
        }

        if (root == null || keep == null) ClearPreview();
        else ShowPreview(keep);
    }

    private static void CollectExpanded(IEnumerable<FileNode> nodes, HashSet<string> set)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder || !node.IsExpanded) continue;
            set.Add(node.FullPath);
            CollectExpanded(node.Children, set);
        }
    }

    private static void ExpandRestored(FileNode node, HashSet<string> expanded)
    {
        if (!node.IsFolder || !expanded.Contains(node.FullPath)) return;
        node.IsExpanded = true; // loads children
        foreach (var child in node.Children) ExpandRestored(child, expanded);
    }

    /// <summary>Walks down the generated containers to select the node for <paramref name="path"/>.</summary>
    private void RestoreSelection(string path)
    {
        ItemsControl host = Tree;
        while (true)
        {
            var node = host.Items.OfType<FileNode>()
                .FirstOrDefault(n => n.Kind != FileNodeKind.Info && Covers(n.FullPath, path));
            if (node == null) return;
            if (host.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem container) return;
            if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                container.IsSelected = true;
                container.BringIntoView();
                return;
            }
            container.ApplyTemplate();
            container.UpdateLayout();
            host = container;
        }
    }

    private static bool Covers(string ancestorOrSelf, string path) =>
        string.Equals(ancestorOrSelf, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ancestorOrSelf.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (suppressSelection) return;
        if (e.NewValue is not FileNode node || node.Kind == FileNodeKind.Info) return;
        if (node.IsFile)
        {
            ShowPreview(node.FullPath);
            FileSelected?.Invoke(this, node.FullPath);
        }
        else
        {
            ClearPreview();
        }
    }

    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Right-click selects the row under the pointer so the context menu acts on it.
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item != null) item.IsSelected = true;
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var enabled = SelectedPath != null;
        MenuOpen.IsEnabled = enabled;
        MenuShow.IsEnabled = enabled;
        MenuCopy.IsEnabled = enabled;
    }

    private string? SelectedPath =>
        Tree.SelectedItem is FileNode { Kind: not FileNodeKind.Info } node ? node.FullPath : null;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RebuildTree();
    private void MenuOpen_Click(object sender, RoutedEventArgs e) => OpenPath(SelectedPath);
    private void MenuShow_Click(object sender, RoutedEventArgs e) => ShowInFolder(SelectedPath);
    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CopyPath(SelectedPath);
    private void InfoOpen_Click(object sender, RoutedEventArgs e) => OpenPath(previewPath);
    private void InfoShow_Click(object sender, RoutedEventArgs e) => ShowInFolder(previewPath);
    private void ImageScroll_SizeChanged(object sender, SizeChangedEventArgs e) => FitImage();

    // ----- Watcher ----------------------------------------------------------

    private void StartWatcher()
    {
        var root = NormalizedRoot();
        if (root == null || watcher != null) return;
        FileSystemWatcher? w = null;
        try
        {
            w = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            var source = w;
            source.Created += (_, e) => Post(source, e.FullPath, structural: true);
            source.Deleted += (_, e) => Post(source, e.FullPath, structural: true);
            source.Renamed += (_, e) => Post(source, e.FullPath, structural: true);
            source.Changed += (_, e) => Post(source, e.FullPath, structural: false);
            source.Error += (_, _) => Post(source, null, structural: true);
            source.EnableRaisingEvents = true;
            watcher = source;
        }
        catch (Exception)
        {
            w?.Dispose();
            watcher = null;
        }
    }

    private void StopWatcher()
    {
        debounce.Stop();
        pendingStructure = false;
        pendingChanged.Clear();
        var old = watcher;
        watcher = null;
        if (old == null) return;
        old.EnableRaisingEvents = false;
        old.Dispose();
    }

    // Watcher events arrive on a thread-pool thread; hop to the dispatcher before touching state.
    private void Post(FileSystemWatcher source, string? path, bool structural) =>
        Dispatcher.BeginInvoke(new Action(() => OnFileSystemEvent(source, path, structural)));

    private void OnFileSystemEvent(FileSystemWatcher source, string? path, bool structural)
    {
        if (!ReferenceEquals(source, watcher)) return;
        if (path != null && IsIgnoredPath(path)) return;
        if (structural || path == null) pendingStructure = true;
        else pendingChanged.Add(path);
        debounce.Stop();
        debounce.Start();
    }

    private void OnDebounced()
    {
        debounce.Stop();
        bool structural = pendingStructure;
        bool previewChanged = previewPath != null && pendingChanged.Contains(previewPath);
        pendingStructure = false;
        pendingChanged.Clear();

        if (structural) RebuildTree();
        else if (previewChanged) ShowPreview(previewPath);
    }

    private bool IsIgnoredPath(string path)
    {
        var root = NormalizedRoot();
        if (root == null) return true;
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(FileNode.SkippedNames.Contains);
    }

    // ----- Preview ----------------------------------------------------------

    private void ShowPreview(string? path)
    {
        previewPath = path;
        ErrorText.Visibility = Visibility.Collapsed;
        if (path == null)
        {
            ClearPreview();
            return;
        }

        PreviewTitle.Text = Path.GetFileName(path);
        var full = SafePath(path);
        if (full == null)
        {
            ShowMessage("Not available: outside the project folder or a link.");
            return;
        }

        try
        {
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                ShowMessage("File no longer exists.");
                return;
            }

            var ext = info.Extension.TrimStart('.');
            if (ImageExtensions.Contains(ext)) ShowImage(full);
            else if (TextExtensions.Contains(ext) && info.Length <= MaxPreviewBytes) ShowText(full, info);
            else ShowInfo(info);
        }
        catch (Exception ex)
        {
            ShowMessage("Can't read this file: " + FirstLine(ex));
        }
    }

    private void ClearPreview()
    {
        previewPath = null;
        PreviewTitle.Text = "Preview";
        ShowMessage(SelectPrompt);
    }

    private void ShowText(string full, FileInfo info)
    {
        var buffer = new byte[(int)Math.Min(info.Length, MaxTextBytes)];
        int read;
        using (var stream = OpenShared(full))
        {
            read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }

        // Binary content behind a text extension gets the info card instead of garbage text.
        if (Array.IndexOf(buffer, (byte)0, 0, Math.Min(read, BinarySniffBytes)) >= 0)
        {
            ShowInfo(info);
            return;
        }

        CodeView.Text = Encoding.UTF8.GetString(buffer, 0, read).TrimStart('﻿');
        CodeView.ScrollToHome();
        if (info.Length > read)
            PreviewTitle.Text += $"  ·  first {FormatSize(read)} of {FormatSize(info.Length)}";
        SetMode(PreviewMode.Text);
    }

    private void ShowImage(string full)
    {
        // OnLoad with a stream: the bitmap is fully decoded and the file handle is released.
        using var stream = OpenShared(full);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        PreviewImage.Source = image;
        FitImage();
        SetMode(PreviewMode.Image);
    }

    private void FitImage()
    {
        // Fit-to-view: never larger than the viewport, scaled down with Uniform stretch.
        PreviewImage.MaxWidth = ImageScroll.ViewportWidth > 0 ? ImageScroll.ViewportWidth : double.PositiveInfinity;
        PreviewImage.MaxHeight = ImageScroll.ViewportHeight > 0 ? ImageScroll.ViewportHeight : double.PositiveInfinity;
    }

    private void ShowInfo(FileInfo info)
    {
        InfoName.Text = info.Name;
        InfoMeta.Text = $"{FormatSize(info.Length)}  ·  modified {info.LastWriteTime:g}";
        SetMode(PreviewMode.Info);
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        SetMode(PreviewMode.Message);
    }

    private void SetMode(PreviewMode mode)
    {
        MessageText.Visibility = ToVisibility(mode == PreviewMode.Message);
        CodeView.Visibility = ToVisibility(mode == PreviewMode.Text);
        ImageScroll.Visibility = ToVisibility(mode == PreviewMode.Image);
        InfoCard.Visibility = ToVisibility(mode == PreviewMode.Info);
    }

    // ----- Actions ----------------------------------------------------------

    private void OpenPath(string? path)
    {
        var full = SafePath(path);
        if (full == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open: " + FirstLine(ex));
        }
    }

    private void ShowInFolder(string? path)
    {
        var full = SafePath(path);
        if (full == null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            ShowError("Couldn't show in folder: " + FirstLine(ex));
        }
    }

    private void CopyPath(string? path)
    {
        var full = SafePath(path);
        if (full == null) return;
        try
        {
            Clipboard.SetText(full);
        }
        catch (Exception ex)
        {
            ShowError("Couldn't copy path: " + FirstLine(ex));
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    // ----- Helpers ----------------------------------------------------------

    private string? NormalizedRoot()
    {
        if (string.IsNullOrWhiteSpace(RootPath)) return null;
        try
        {
            var full = Path.GetFullPath(RootPath);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the full path if it lies under RootPath and no segment between the root and the
    /// path is a reparse point; otherwise null.
    /// </summary>
    private string? SafePath(string? path)
    {
        var root = NormalizedRoot();
        if (root == null || string.IsNullOrEmpty(path)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            return HasReparsePointBelowRoot(full, root) ? null : full;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasReparsePointBelowRoot(string full, string root)
    {
        var rootTrimmed = root.TrimEnd(Path.DirectorySeparatorChar);
        string? current = full;
        while (current != null && !string.Equals(current.TrimEnd(Path.DirectorySeparatorChar), rootTrimmed, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    private static FileStream OpenShared(string full) =>
        new(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current != null && current is not T)
            current = VisualTreeHelper.GetParent(current);
        return current as T;
    }

    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static string FolderDisplayName(string root)
    {
        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? root : name;
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB"
        : $"{bytes / (1024.0 * 1024):0.#} MB";

    private static string FirstLine(Exception ex) => ex.Message.Split('\n')[0].Trim();
}
