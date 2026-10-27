using System.Globalization;
using Jacobi.Midi.Console.Core.Dashboards;
using Jacobi.Midi.Console.Core.Faces;

namespace Jacobi.Midi.Console.Core.Parameters;

/// <summary>
/// An index variable of an open dashboard as a control sees it (a control bound to <c>$patch</c>): the item number,
/// the index + 1, from 1 to the number of items of the repeat it indexes. Setting it selects another item: the
/// dashboard session binds the controls that use the variable to that item.
/// </summary>
public sealed class VariableValue : IControlValue
{
    private readonly DashboardSession _session;

    internal VariableValue(DashboardSession session, VariableReference variable, int? itemCount)
    {
        _session = session;
        Variable = variable;
        Info = new ParameterInfo(variable.Name, typeof(int), itemCount is > 0 ? new ValueRange(1, itemCount.Value) : null, 1, null)
        {
            Default = 1,
        };
    }

    public VariableReference Variable { get; }

    public ParameterInfo Info { get; }

    public object? Value => _session.IndexOf(Variable) + 1;

    public decimal? Number => _session.IndexOf(Variable) + 1;

    public event EventHandler? Changed;

    public void Set(object value)
        => _session.SetIndex(Variable, Convert.ToInt32(value, CultureInfo.InvariantCulture) - 1);

    internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
