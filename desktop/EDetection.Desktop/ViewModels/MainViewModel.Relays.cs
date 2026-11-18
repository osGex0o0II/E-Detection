namespace EDetection.Desktop.ViewModels;

public partial class MainViewModel
{
    // Thin relays over the child view models.  They live in their own partial
    // so the main properties file stays inside the architecture line budget.
    public string BackendDiagnosticText => Diagnostics.BackendText;

    public string ConfigDiagnosticText => Diagnostics.ConfigText;

    public string InputDiagnosticText => Diagnostics.InputText;

    public string OutputDiagnosticText => Diagnostics.OutputText;

    public string DiagnosticActionText => Diagnostics.ActionText;

    public string CurrentFileText => RunTelemetry.CurrentFileText;

    public string RunElapsedText => RunTelemetry.ElapsedText;

    public int SelectedLogRetentionIndex => RuntimeLogs.SelectedRetentionIndex;
}
