using System.Text;
using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>Pestaña Registro: inicio, avisos, errores, reanudaciones y resumen del trabajo.</summary>
public sealed partial class LogTab : UserControl
{
    public static readonly DependencyProperty JobProperty =
        DependencyProperty.Register(nameof(Job), typeof(JobViewModel), typeof(LogTab), new PropertyMetadata(null, (d, _) => ((LogTab)d).Bindings.Update()));

    public LogTab()
    {
        InitializeComponent();
    }

    public JobViewModel? Job
    {
        get => (JobViewModel?)GetValue(JobProperty);
        set => SetValue(JobProperty, value);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Job is null || await Pickers.SaveAsync("MegaCopier-registro", ".txt", L.Get("Log_FileType")) is not { } path)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var row in Job.Log)
        {
            text.Append(row.Time).Append("  ").Append(row.Message);
            if (row.HasPath)
            {
                text.Append("  [").Append(row.PathText).Append(']');
            }

            text.AppendLine();
        }

        await File.WriteAllTextAsync(path, text.ToString(), new UTF8Encoding(true));
    }
}
