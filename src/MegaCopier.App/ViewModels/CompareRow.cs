using MegaCopier.App.Services;
using MegaCopier.Engine.Sync;

namespace MegaCopier.App.ViewModels;

/// <summary>Una fila de la vista de comparación: el elemento en cada lado, qué se hará y por qué.</summary>
public sealed partial class CompareRow(SyncItem item)
{
    public SyncItem Item { get; } = item;

    /// <summary>Marcada = se sincroniza; desmarcada = no se toca.</summary>
    public bool Include { get; set; } = true;

    public bool CanInclude => Item.Action != SyncAction.Equal;

    public string Relative => Item.Relative;

    public string Glyph => Item.IsFolder ? "" : "";

    public string LeftText => Describe(Item.Left);

    public string RightText => Describe(Item.Right);

    public string ActionGlyph => Item.Action switch
    {
        SyncAction.CopyToRight or SyncAction.CreateFolderRight => "",   // →
        SyncAction.CopyToLeft or SyncAction.CreateFolderLeft => "",     // ←
        SyncAction.DeleteLeft or SyncAction.DeleteRight or SyncAction.DeleteFolderLeft or SyncAction.DeleteFolderRight => "",
        _ => "",                                                        // igual
    };

    /// <summary>Quitar: va hacia el lado del que se quita (para leer la flecha como "pasa a").</summary>
    public bool IsRemoval => Item.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight or SyncAction.DeleteFolderLeft or SyncAction.DeleteFolderRight;

    public string ActionText => L.Get(Item.Action switch
    {
        SyncAction.CopyToRight => "Compare_ToRight",
        SyncAction.CopyToLeft => "Compare_ToLeft",
        SyncAction.DeleteLeft or SyncAction.DeleteFolderLeft => "Compare_DeleteLeft",
        SyncAction.DeleteRight or SyncAction.DeleteFolderRight => "Compare_DeleteRight",
        SyncAction.CreateFolderLeft => "Compare_FolderLeft",
        SyncAction.CreateFolderRight => "Compare_FolderRight",
        _ => "Compare_Equal",
    });

    public string ReasonText => L.Get(Item.Reason switch
    {
        SyncReason.New => "Compare_New",
        SyncReason.Changed => "Compare_Changed",
        SyncReason.Deleted => "Compare_Deleted",
        SyncReason.BothChanged => "Compare_BothChanged",
        SyncReason.ChangedAndDeleted => "Compare_ChangedAndDeleted",
        SyncReason.Extra => "Compare_Extra",
        _ => "Compare_Same",
    });

    /// <summary>Para los lectores de pantalla y el filtro de la lista.</summary>
    public override string ToString() => $"{Relative}, {ActionText}, {ReasonText}";

    private static string Describe(SyncEntry? entry) => entry switch
    {
        null => "—",
        { IsFolder: true } => "",
        { } file => $"{Format.Bytes(file.Size)} · {Format.Date(file.ModifiedUtc)}",
    };
}
