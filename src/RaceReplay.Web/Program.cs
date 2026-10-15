using System.Runtime.InteropServices.JavaScript;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using RaceReplay.Playback;
using RaceReplay.Web;
using RaceReplay.Web.Interop;
using RaceReplay.Web.Loading;
using RaceReplay.Web.Rendering;
using RaceReplay.Web.State;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// In Blazor WebAssembly a scoped service lives as long as the app, like a singleton.
builder.Services
    .AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
    .AddSingleton(new PlaybackOptions())
    .AddScoped<SiteCatalog>()
    .AddScoped<HttpReplaySource>()
    .AddScoped<ReplayStore>()
    .AddScoped<ReplaySessionHost>();

if (OperatingSystem.IsBrowser())
{
    builder.Services
        .AddScoped<ITrackMapRenderer, CanvasTrackMapRenderer>()
        .AddScoped<IFrameScheduler, AnimationFrameScheduler>();

    // Relative to _framework/, where the runtime loads from; also correct under the Pages sub-path (docs/05 §2).
    await JSHost.ImportAsync(RenderLoopInterop.Module, "../js/renderLoop.js");
    await JSHost.ImportAsync(TrackMapInterop.Module, "../js/trackMap.js");
}

await builder.Build().RunAsync();