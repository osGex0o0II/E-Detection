namespace EDetection.Desktop.ViewModels;

public partial class MainViewModel
{
    // Thin relays over the child view models.  They live in their own partial
    // so the main properties file stays inside the architecture line budget.
    public string DiagnosticSummaryText => Diagnostics.SummaryText;

    public string BackendDiagnosticText => Diagnostics.BackendText;

    public string ConfigDiagnosticText => Diagnostics.ConfigText;

    public string InputDiagnosticText => Diagnostics.InputText;

    public string OutputDiagnosticText => Diagnostics.OutputText;

    public string DiagnosticCheckedAtText => Diagnostics.CheckedAtText;

    public string DiagnosticActionText => Diagnostics.ActionText;

    public string CurrentFileText => RunTelemetry.CurrentFileText;

    public string RunElapsedText => RunTelemetry.ElapsedText;

    public string RunSpeedText => RunTelemetry.SpeedText;

    public string RunRemainingText => RunTelemetry.RemainingText;

    public string RunProgressDetailText => RunTelemetry.ProgressDetailText;

    public int SelectedLogRetentionIndex => RuntimeLogs.SelectedRetentionIndex;

    public string LogSearchText => RuntimeLogs.SearchText;

    public int SelectedLogKindFilterIndex => RuntimeLogs.SelectedKindFilterIndex;
}
