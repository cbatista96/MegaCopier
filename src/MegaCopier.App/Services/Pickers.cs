using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;

namespace MegaCopier.App.Services;

/// <summary>Selectores de archivos y carpetas del sistema, asociados a la ventana principal.</summary>
internal static class Pickers
{
    private static Microsoft.UI.WindowId WindowId => ((App)Application.Current).Window.AppWindow.Id;

    public static async Task<IReadOnlyList<string>> PickFilesAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker(WindowId) { ViewMode = PickerViewMode.List };
        foreach (string extension in extensions.Length == 0 ? ["*"] : extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(f => f.Path).ToList() ?? [];
    }

    public static async Task<string?> PickFileAsync(string extension)
    {
        var picker = new FileOpenPicker(WindowId) { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickFolderAsync(string? commitText = null)
    {
        var picker = new FolderPicker(WindowId);
        if (commitText is not null)
        {
            picker.CommitButtonText = commitText;
        }

        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public static async Task<string?> SaveAsync(string suggestedName, string extension, string typeName)
    {
        var picker = new FileSavePicker(WindowId) { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(typeName, [extension]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
