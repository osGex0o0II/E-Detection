using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using EDetection.NativeCore.Models;

namespace EDetection.NativeCore.Services;

public sealed partial class NativeDetectionBackendService : IDetectionBackendService
{
    private static readonly string[] ReportValueColumns =
    [
        "Uab",
        "Ubc",
        "Uca",
        "Ia",
        "Ib",
        "Ic",
        "有功功率",
        "无功功率",
        "功率因数",
        "A相温度",
        "B相温度",
        "C相温度",
    ];

    private static readonly IReadOnlyDictionary<string, string> PhaseMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Uab"] = "A相电压",
            ["Ubc"] = "B相电压",
            ["Uca"] = "C相电压",
            ["Ia"] = "A相电流",
            ["Ib"] = "B相电流",
            ["Ic"] = "C相电流",
            ["A相温度"] = "A相温度",
            ["B相温度"] = "B相温度",
            ["C相温度"] = "C相温度",
        };

    static NativeDetectionBackendService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

}
