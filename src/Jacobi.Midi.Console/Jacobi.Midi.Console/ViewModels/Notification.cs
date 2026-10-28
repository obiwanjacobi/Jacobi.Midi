namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// Something the console tells the user in the notifications: a problem (something did not or will not work) or
/// information (worth knowing, nothing is wrong).
/// </summary>
internal sealed record Notification(string Message, bool IsProblem);
