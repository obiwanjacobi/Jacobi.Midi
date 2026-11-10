namespace Jacobi.Midi.Console.ViewModels;

/// <summary>
/// Something the console tells the user in the notifications: a problem (something did not or will not work) or
/// information (worth knowing, nothing is wrong).
/// </summary>
/// <param name="Message">What the user is told.</param>
/// <param name="IsProblem">A problem, not information.</param>
/// <param name="IsReport">
/// A report of something that happened (a file, dump or exchange), which the user can dismiss; not a problem of the
/// current state (of the rig, the dashboard or the schema library), which stays until its cause is gone.
/// </param>
internal sealed record Notification(string Message, bool IsProblem, bool IsReport = false);
