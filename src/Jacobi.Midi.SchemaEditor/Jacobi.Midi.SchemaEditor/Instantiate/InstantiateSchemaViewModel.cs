using CommunityToolkit.Mvvm.Input;
using Jacobi.Midi.SchemaEditor.ViewModels;

namespace Jacobi.Midi.SchemaEditor.Instantiate;

/// <summary>
/// The view model of the instantiate dialog: lets the user provide the values of a group
/// (the <see cref="Root"/> instance tree) and confirm or cancel, or shows the <see cref="Error"/> that prevented it.
/// </summary>
internal sealed partial class InstantiateSchemaViewModel : ViewModelBase
{
    private readonly Action<InstantiateSchemaViewModel, bool> _close;

    /// <param name="root">The instance tree to fill out.</param>
    /// <param name="close">Called with this view model and whether the user confirmed (true) or cancelled (false).</param>
    public InstantiateSchemaViewModel(InstanceNode root, Action<InstantiateSchemaViewModel, bool> close)
    {
        Root = root;
        Title = $"Instantiate {root.Name}";
        _close = close;
    }

    /// <summary>Creates a dialog that only shows why <paramref name="name"/> could not be instantiated.</summary>
    public InstantiateSchemaViewModel(string name, string error, Action<InstantiateSchemaViewModel, bool> close)
    {
        Title = $"Cannot instantiate {name}";
        Error = error;
        _close = close;
    }

    /// <summary>The node to fill out; null when the dialog shows an <see cref="Error"/>.</summary>
    public InstanceNode? Root { get; }

    public string Title { get; }

    /// <summary>The reason the instantiation failed; empty when the dialog shows the <see cref="Root"/>.</summary>
    public string Error { get; } = String.Empty;

    public bool HasError => Error.Length > 0;

    public bool HasRoot => Root is not null;

    [RelayCommand]
    private void Confirm() => _close(this, HasRoot);

    [RelayCommand]
    private void Cancel() => _close(this, false);
}
