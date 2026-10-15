namespace OrchDesk.App.Services;
public interface IFolderPicker { string? PickFolder(string? currentFolder); }
public sealed class FolderPicker : IFolderPicker
{
    public string? PickFolder(string? currentFolder)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the project folder OrchDesk may work in" };
        if (currentFolder is not null) dialog.InitialDirectory = currentFolder;
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }
}
public sealed class SuppliedFolderPicker(string path) : IFolderPicker
{
    public string? PickFolder(string? currentFolder) => path;
}
