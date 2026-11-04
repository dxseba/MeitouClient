using System.Diagnostics;
using System.Text.Json;
using Meitou.Brutality;
using Meitou.Content;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Tests.Simulation;

namespace Meitou.Tests.Modding;

public sealed class BrutalityDllTests
{
    [Fact]
    public async Task Enabled_trusted_DLL_loads_in_its_own_context_and_registers_after_combat()
    {
        using var package = new LocalPackage();
        using var result = await Probe(package);
        var report = result.RootElement;
        Assert.Equal(1, report.GetProperty("loaded").GetInt32());
        Assert.Empty(report.GetProperty("notices").EnumerateArray());
        Assert.Equal("dxseba.brutality", report.GetProperty("id").GetString());
        Assert.Equal("mod dxseba.brutality", report.GetProperty("context").GetString());
        Assert.True(report.GetProperty("anatomy").GetBoolean());
        Assert.Equal(1, report.GetProperty("systems").GetInt32());
        Assert.True(report.GetProperty("afterCombat").GetBoolean());
        Assert.True(report.GetProperty("observer").GetBoolean());
        Assert.DoesNotContain(Directory.EnumerateFiles(package.Folder), p => Path.GetFileName(p).StartsWith("Meitou.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Untrusted_DLL_does_not_load_or_register_gameplay()
    {
        using var package = new LocalPackage();
        using var result = await Probe(package, trusted: false);
        Assert.Equal(0, result.RootElement.GetProperty("loaded").GetInt32());
        Assert.Single(result.RootElement.GetProperty("notices").EnumerateArray());
        Assert.Equal(0, result.RootElement.GetProperty("systems").GetInt32());
    }

    [Fact]
    public async Task Disabled_DLL_has_no_systems_or_asset_imports()
    {
        using var package = new LocalPackage();
        File.WriteAllText(Path.Combine(package.Folder, "brutality.json"), "{\"enabled\":false}");
        using var result = await Probe(package);
        Assert.Equal(1, result.RootElement.GetProperty("loaded").GetInt32());
        Assert.Empty(result.RootElement.GetProperty("notices").EnumerateArray());
        Assert.False(result.RootElement.GetProperty("enabled").GetBoolean());
        Assert.False(result.RootElement.GetProperty("anatomy").GetBoolean());
        Assert.Equal(0, result.RootElement.GetProperty("systems").GetInt32());
    }

    static async Task<JsonDocument> Probe(LocalPackage package, bool trusted = true)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "loader-probe", "Meitou.Brutality.LoaderProbe.dll"));
        start.ArgumentList.Add(package.Folder);
        if (!trusted) start.ArgumentList.Add("--untrusted");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        string log = await output, errors = await error;
        Assert.True(process.ExitCode == 0, log + errors);
        var line = log.Split('\n').SingleOrDefault(l => l.StartsWith("BRUTALITY_PROBE:", StringComparison.Ordinal));
        Assert.True(line is not null, log + errors);
        return JsonDocument.Parse(line!["BRUTALITY_PROBE:".Length..]);
    }

    sealed class LocalPackage : IDisposable
    {
        public string Folder { get; } = Directory.CreateTempSubdirectory("brutality-dll-").FullName;
        public ModInfo Info => new("Brutality", Path.Combine(Folder, "Brutality.mod"), ModSource.Local);

        public LocalPackage()
        {
            FcsWriter.WriteFile(new FcsFile { Author = "test", Description = "Code mod fixture" }, Path.Combine(Folder, "Brutality.mod"));
            File.Copy(typeof(BrutalityMod).Assembly.Location, Path.Combine(Folder, "Brutality.dll"));
            File.WriteAllText(Path.Combine(Folder, "meitou.json"), JsonSerializer.Serialize(new
            {
                id = "dxseba.brutality", apiVersion = ModApi.Version, assemblies = new[] { "Brutality.dll" }, entry = "Meitou.Brutality.BrutalityMod",
            }));
        }

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }
}
