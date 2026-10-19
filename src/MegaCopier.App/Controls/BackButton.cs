using MegaCopier.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MegaCopier.App.Controls;

/// <summary>
/// Flecha «Volver» arriba a la izquierda de cada página, junto al título, como en la Configuración de Windows 11.
/// También vuelve con Esc y con Alt+←.
/// </summary>
public sealed partial class BackButton : Button
{
    public BackButton()
    {
        Style = (Style)Application.Current.Resources["McToolButton"];
        Width = 40;
        Height = 36;
        VerticalAlignment = VerticalAlignment.Center;
        Content = new McIcon { Glyph = "", FontSize = 16 };
        string name = L.Get("Page_Back/Text");
        ToolTipService.SetToolTip(this, name + " (Esc)");
        AutomationProperties.SetName(this, name);
        KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.Escape });
        KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu });
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }
}
