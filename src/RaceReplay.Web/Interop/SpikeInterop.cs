using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace RaceReplay.Web.Interop;

/// <summary>Spike S2 (throwaway, branch spike/s2-browser): timing and memory helpers in <c>wwwroot/js/spike.js</c>.</summary>
[SupportedOSPlatform("browser")]
internal static partial class SpikeInterop
{
    public const string Module = "spike";

    [JSImport("watchFileInput", Module)]
    internal static partial void WatchFileInput(string inputId);

    [JSImport("changedAtMs", Module)]
    internal static partial double ChangedAtMs();

    [JSImport("nowMs", Module)]
    internal static partial double NowMs();

    [JSImport("wasmMemoryBytes", Module)]
    internal static partial double WasmMemoryBytes();

    [JSImport("driveFirstFrame", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    internal static partial Task<double> DriveFirstFrame();

    [JSImport("readChosenFile", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    internal static partial Task<double> ReadChosenFile(string inputId);

    [JSImport("fetchHeld", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    internal static partial Task<double> FetchHeld(string url);

    [JSImport("gunzipHeld", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    internal static partial Task<double> GunzipHeld();

    [JSImport("takeHeld", Module)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial byte[] TakeHeld();

    [JSImport("log", Module)]
    internal static partial void Log(string text);
}
