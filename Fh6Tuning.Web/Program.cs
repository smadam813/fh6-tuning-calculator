using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Fh6Tuning.Core;
using Fh6Tuning.Web;
using Fh6Tuning.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

// Pure tuning engine (compute / validate / overallTireDiameter). Stateless → singleton.
builder.Services.AddSingleton<ITuningEngine, TuningEngine>();

// Unit conversion + display formatters (M2I / toImp / fromImp / nf / *Disp). Stateless → singleton.
builder.Services.AddSingleton<UnitService>();

// Output display layer (cards / compare rows / changes effects / copy text). Stateless → singleton.
builder.Services.AddSingleton<TuneFormatter>();

// Clipboard interop for the Copy-tune button (writeText with a prompt fallback). Scoped (per-user JS).
builder.Services.AddScoped<ClipboardInterop>();

// Live UI state container (input model, goal, units, dials, compare flag). Per-user → scoped.
builder.Services.AddScoped<CalculatorState>();

// localStorage interop for saved Car Setups (pure storage logic stays in Fh6Tuning.Core).
builder.Services.AddScoped<SetupsStorage>();

// File-download interop for the saved-setups "Export JSON" button. Scoped (per-user JS).
builder.Services.AddScoped<FileDownloadInterop>();

await builder.Build().RunAsync();
