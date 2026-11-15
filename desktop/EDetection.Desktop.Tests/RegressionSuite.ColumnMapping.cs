using System.Text;

internal static partial class RegressionSuite
{
    // A unit annotation must never be read as phase evidence.  Before the fix
    // the ampere unit in "B相电流(A)" matched the bare-letter fallback in the
    // Ia branch, so all three phases collapsed into one group and were averaged:
    // (1500+10+10)/3 = 506.7 < I_MAX_THRESHOLD, and a single-phase overload was
    // reported as a healthy device.
    private static async Task PhaseCurrentUnitSuffixAsync()
    {
        var root = TestSupport.CreateFixtureDirectory("PhaseCurrentUnitSuffix");
        try
        {
            File.WriteAllText(
                Path.Combine(root, "current.csv"),
                "时间,A相电流(A),B相电流(A),C相电流(A)\n0,1500,10,10\n",
                Encoding.UTF8);
            var (_, events) = await RunBackendAsync(root);
            var result = events.Single(e => e.EventName == "file_result");
            TestSupport.Equal(
                "anomaly",
                result.Status,
                "A 1500 A single-phase overload must be detected when headers carry the ampere unit.");
            TestSupport.True(
                result.AnomalyTypes?.Contains("电流", StringComparison.Ordinal) is true,
                "The overload must be reported as a current anomaly.");
        }
        finally
        {
            TestSupport.DeleteFixtureDirectory(root);
        }
    }

    // Ambient temperature is a different physical quantity from a winding
    // probe.  Before the fix "环境温度(°C)" matched the bare-letter C branch,
    // joined the C相温度 group and was averaged with the real probe:
    // (110+25)/2 = 67.5 < T_MAX_THRESHOLD, masking a 110 °C winding.
    private static async Task AmbientTemperatureIsNotPhaseTemperatureAsync()
    {
        var root = TestSupport.CreateFixtureDirectory("AmbientTemperature");
        try
        {
            File.WriteAllText(
                Path.Combine(root, "temperature.csv"),
                "时间,Uab,Ubc,Uca,C相温度,环境温度(°C)\n0,380,380,380,110,25\n",
                Encoding.UTF8);
            var (_, events) = await RunBackendAsync(root);
            var result = events.Single(e => e.EventName == "file_result");
            TestSupport.Equal(
                "anomaly",
                result.Status,
                "A 110 C winding must stay detected when an ambient column is present.");
            TestSupport.True(
                result.AnomalyTypes?.Contains("温度", StringComparison.Ordinal) is true,
                "The overheating must be reported as a temperature anomaly.");
        }
        finally
        {
            TestSupport.DeleteFixtureDirectory(root);
        }
    }

    // Two different source headers that resolve to one measurement cannot be
    // told apart.  Averaging them fabricates a reading no sensor produced, so
    // the file must be skipped and reported rather than silently analysed.
    private static async Task AmbiguousAliasColumnsAreSkippedAsync()
    {
        var root = TestSupport.CreateFixtureDirectory("AmbiguousAliasColumns");
        try
        {
            File.WriteAllText(
                Path.Combine(root, "alias.csv"),
                "time,Uab,A相电压,Ubc,Uca\n0,-1,420,400,400\n",
                Encoding.UTF8);
            var (_, events) = await RunBackendAsync(root);
            var result = events.Single(e => e.EventName == "file_result");
            TestSupport.True(
                result.Status is not "normal",
                "A file with colliding column aliases must not be certified normal.");
            TestSupport.True(
                result.Message?.Contains("列名歧义", StringComparison.Ordinal) is true,
                "The skip reason must name the ambiguous columns.");
        }
        finally
        {
            TestSupport.DeleteFixtureDirectory(root);
        }
    }

    // The statistics-footer heuristic ran against column 0 whenever no header
    // named a time column, and matched on a substring.  A measuring point
    // called "MIN-01柜" therefore truncated the file to EOF - every remaining
    // row was discarded and the file was reported 正常.
    private static async Task StatisticsLabelInPointNameKeepsRowsAsync()
    {
        var root = TestSupport.CreateFixtureDirectory("StatisticsLabelInPointName");
        try
        {
            File.WriteAllText(
                Path.Combine(root, "points.csv"),
                "点名,Uab,Ubc,Uca\nMIN-01柜,300,380,380\nTM-02柜,300,380,380\nTM-03柜,300,380,380\n",
                Encoding.UTF8);
            var (_, events) = await RunBackendAsync(root);
            var result = events.Single(e => e.EventName == "file_result");
            TestSupport.Equal(
                "anomaly",
                result.Status,
                "Undervoltage must stay detected when a point name contains 'MIN'.");
            TestSupport.True(
                result.AnomalyCount >= 3,
                $"Every row must be analysed, not truncated. AnomalyCount={result.AnomalyCount}");
        }
        finally
        {
            TestSupport.DeleteFixtureDirectory(root);
        }
    }

    // Guards the fix above against over-correction: a real statistics footer
    // under a real time column must still be dropped, so its aggregate values
    // are never evaluated as telemetry.
    private static async Task StatisticsFooterRowIsDroppedAsync()
    {
        var root = TestSupport.CreateFixtureDirectory("StatisticsFooterRow");
        try
        {
            File.WriteAllText(
                Path.Combine(root, "footer.csv"),
                "time,Uab,Ubc,Uca\n0,380,380,380\n1,380,380,380\n最大值,999,999,999\n",
                Encoding.UTF8);
            var (_, events) = await RunBackendAsync(root);
            var result = events.Single(e => e.EventName == "file_result");
            TestSupport.Equal(
                "normal",
                result.Status,
                "A statistics footer must be dropped, not evaluated as a 999 V reading.");
        }
        finally
        {
            TestSupport.DeleteFixtureDirectory(root);
        }
    }
}
