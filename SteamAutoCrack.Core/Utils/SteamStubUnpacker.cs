using System.ComponentModel;
using System.Text.Json;
using Serilog;
using Steamless.API.Model;
using Steamless.API.PE32;
using Steamless.API.Services;
using Steamless.Unpacker.Variant10.x86;

namespace SteamAutoCrack.Core.Utils;

public class SteamStubUnpackerConfig
{
    public enum SteamAPICheckBypassModes
    {
        [Description("Disabled")] Disabled,
        [Description("Enable All Time")] All,
        [Description("Enable Only Nth Time")] OnlyN,

        [Description("Enable Only Not Nth Time")]
        OnlyNotN
    }

    /// <summary>
    ///     Keeps the .bind section in the unpacked file.
    /// </summary>
    public bool KeepBind { get; set; } = true;

    /// <summary>
    ///     Keeps the DOS stub in the unpacked file.
    /// </summary>
    public bool KeepStub { get; set; } = false;

    /// <summary>
    ///     Realigns the unpacked file sections.
    /// </summary>
    public bool Realign { get; set; } = false;

    /// <summary>
    ///     Recalculates the unpacked file checksum.
    /// </summary>
    public bool ReCalcChecksum { get; set; } = false;

    /// <summary>
    ///     Use Experimental Features.
    /// </summary>
    public bool UseExperimentalFeatures { get; set; } = false;

    /// <summary>
    ///     SteamAPICheckBypass Mode
    /// </summary>
    public SteamAPICheckBypassModes SteamAPICheckBypassMode { get; set; } = SteamAPICheckBypassModes.Disabled;

    /// <summary>
    ///     SteamAPI Check Bypass Nth Time Setting
    /// </summary>
    public long SteamAPICheckBypassNthTime { get; set; } = 1;
}

public class SteamStubUnpackerConfigDefault
{
    /// <summary>
    ///     Keeps the .bind section in the unpacked file.
    /// </summary>
    public static readonly bool KeepBind = true;

    /// <summary>
    ///     Keeps the DOS stub in the unpacked file.
    /// </summary>
    public static readonly bool KeepStub = false;

    /// <summary>
    ///     Realigns the unpacked file sections.
    /// </summary>
    public static readonly bool Realign = false;

    /// <summary>
    ///     Recalculates the unpacked file checksum.
    /// </summary>
    public static readonly bool ReCalcChecksum = false;

    /// <summary>
    ///     Use Experimental Features.
    /// </summary>
    public static readonly bool UseExperimentalFeatures = false;

    /// <summary>
    ///     SteamAPICheckBypass Mode
    /// </summary>
    public static readonly SteamStubUnpackerConfig.SteamAPICheckBypassModes SteamAPICheckBypassMode =
        SteamStubUnpackerConfig.SteamAPICheckBypassModes.Disabled;

    /// <summary>
    ///     SteamAPI Check Bypass Nth Time Setting
    /// </summary>
    public static readonly long SteamAPICheckBypassNthTime = 1;
}

public interface ISteamStubUnpacker
{
    public Task<bool> Unpack(string path);
}

public class SteamStubUnpacker : ISteamStubUnpacker
{
    private readonly ILogger _log;
    private readonly LoggingService steamlessLoggingService = new();
    private readonly SteamlessOptions steamlessOptions;
    private readonly List<SteamlessPlugin> steamlessPlugins = new();

    public SteamStubUnpacker(SteamStubUnpackerConfig SteamStubUnpackerConfig)
    {
        _log = Log.ForContext<SteamStubUnpacker>();
        steamlessOptions = new SteamlessOptions
        {
            KeepBindSection = SteamStubUnpackerConfig.KeepBind,
            ZeroDosStubData = !SteamStubUnpackerConfig.KeepStub,
            DontRealignSections = !SteamStubUnpackerConfig.Realign,
            RecalculateFileChecksum = SteamStubUnpackerConfig.ReCalcChecksum,
            UseExperimentalFeatures = SteamStubUnpackerConfig.UseExperimentalFeatures
        };
        _SteamAPICheckBypassMode = SteamStubUnpackerConfig.SteamAPICheckBypassMode;
        _SteamAPICheckBypassNthTime = SteamStubUnpackerConfig.SteamAPICheckBypassNthTime;
        steamlessLoggingService.AddLogMessage += (sender, e) =>
        {
            try
            {
                Log.ForContext("SourceContext", sender?.GetType().Assembly.GetName().Name?.Replace(".", ""))
                    .Debug(e.Message);
            }
            catch
            {
            }
        };
        GetSteamlessPlugins();
    }

    private SteamStubUnpackerConfig.SteamAPICheckBypassModes _SteamAPICheckBypassMode { get; }
    private long _SteamAPICheckBypassNthTime { get; }

    public async Task<bool> Unpack(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path)))
            {
                _log.Error("Invaild input path.");
                return false;
            }

            if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                await UnpackFolder(path);
            else
                await UnpackFile(path);

            if (_SteamAPICheckBypassMode != SteamStubUnpackerConfig.SteamAPICheckBypassModes.Disabled)
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                    await ApplySteamAPICheckBypass(path, true);
                else
                    await ApplySteamAPICheckBypass(path);
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to unpack.");
            return false;
        }
    }

    private void GetSteamlessPlugins()
    {
        try
        {
            var existsteamlessPlugins = new List<SteamlessPlugin>
            {
                new Main(),
                new Steamless.Unpacker.Variant20.x86.Main(),
                new Steamless.Unpacker.Variant21.x86.Main(),
                new Steamless.Unpacker.Variant30.x86.Main(),
                new Steamless.Unpacker.Variant30.x64.Main(),
                new Steamless.Unpacker.Variant31.x86.Main(),
                new Steamless.Unpacker.Variant31.x64.Main()
            };
            foreach (var plugin in existsteamlessPlugins)
            {
                if (!plugin.Initialize(steamlessLoggingService))
                {
                    _log.Error($"Failed to load plugin: plugin failed to initialize. ({plugin.Name})");
                    continue;
                }

                steamlessPlugins.Add(plugin);
            }
        }
        catch
        {
            _log.Error("Failed to load plugin.");
        }
    }

    private async Task UnpackFolder(string path)
    {
        try
        {
            _log.Information("Unpacking all file in folder \"{path}\"...", path);
            foreach (var exepath in Directory.EnumerateFiles(path, "*.exe", SearchOption.AllDirectories))
                await UnpackFile(exepath);
            _log.Information("All file in folder \"{path}\" processed.", path);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to unpack folder \"{path}\".", path);
        }
    }

    private async Task UnpackFile(string path)
    {
        try
        {
            var bSuccess = false;
            var bError = false;
            _log.Information("Unpacking file \"{path}\"...", path);
            foreach (var p in steamlessPlugins)
                if (p.CanProcessFile(path))
                {
                    if (await Task.Run(() => p.ProcessFile(path, steamlessOptions)))
                    {
                        bSuccess = true;
                        bError = false;
                        _log.Information("Successfully unpacked file \"{path}\"", path);
                        if (File.Exists(Path.ChangeExtension(path, ".exe.bak")))
                        {
                            _log.Debug("Backup file already exists, skipping backup process...");
                            File.Delete(path);
                        }
                        else
                        {
                            File.Move(path, Path.ChangeExtension(path, ".exe.bak"));
                        }

                        File.Move(Path.ChangeExtension(path, ".exe.unpacked.exe"), path);
                    }
                    else
                    {
                        bError = true;
                        _log.Warning("Failed to unpack file \"{path}\".(File not Packed/Other Protector)",
                            path);
                    }
                }

            if (!bSuccess && !bError)
                _log.Warning("Cannot to unpack file \"{path}\".(File not Packed/Other Protector)", path);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to unpack or backup File \"{path}\".", path);
            throw new Exception($"Failed to unpack or backup File \"{path}\".");
        }
    }

    private async Task ApplySteamAPICheckBypass(string path, bool folder = false)
    {
        try
        {
            var dllPaths = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES")?.ToString();

            var pathsList = new List<string>(dllPaths?.Split(';'));
            var dllPath = "";
            var dll = "";
            foreach (var dirPath in pathsList)
            {
                var fullPath = Path.Combine(dirPath, "SteamAPICheckBypass");
                if (Directory.Exists(fullPath))
                {
                    dllPath = fullPath;
                    break;
                }
            }

            if (folder)
            {
                foreach (var file in Directory.GetFiles(path, "*.exe", SearchOption.AllDirectories))
                {
                    var f = new Pe32File(file);
                    f.Parse();
                    if (!f.IsFile64Bit())
                        dll = Path.Combine(dllPath, "SteamAPICheckBypass_x32.dll");
                    else
                        dll = Path.Combine(dllPath, "SteamAPICheckBypass.dll");
                    if (File.Exists(Path.Combine(Path.GetDirectoryName(file), "version.dll")))
                        _log.Information("Steam API Check Bypass dll already exists, skipping...");
                    else
                        File.Copy(dll, Path.Combine(Path.GetDirectoryName(file), "version.dll"));
                    var jsonContent = new Dictionary<string, object>();
                    if (File.Exists(Path.Combine(Path.GetDirectoryName(file), "SteamAPICheckBypass.json")))
                    {
                        var oldjsonString =
                            File.ReadAllText(Path.Combine(Path.GetDirectoryName(file), "SteamAPICheckBypass.json"));
                        jsonContent = JsonSerializer.Deserialize<Dictionary<string, object>>(oldjsonString);
                    }

                    jsonContent[Path.GetFileName(file)] = new
                    {
                        mode = "file_redirect",
                        to = Path.GetFileName(file) + ".bak",
                        file_must_exist = true
                    };

                    var apidlls = Directory.GetFiles(path, "steam_api.dll",
                            SearchOption.AllDirectories)
                        .Select(p => Path.GetRelativePath(Path.GetDirectoryName(file), p)).ToArray();
                    apidlls = apidlls.Concat(Directory
                        .GetFiles(path, "steam_api64.dll", SearchOption.AllDirectories)
                        .Select(p => Path.GetRelativePath(Path.GetDirectoryName(file), p))).ToArray();
                    foreach (var apiDllPath in apidlls)
                        jsonContent[Path.Combine(Path.GetDirectoryName(apiDllPath), "steam_settings")] = new
                        {
                            mode = "file_hide"
                        };
                    foreach (var apiDllPath in apidlls)
                        if (_SteamAPICheckBypassMode == SteamStubUnpackerConfig.SteamAPICheckBypassModes.All)
                            jsonContent[apiDllPath] = new
                            {
                                mode = "file_redirect",
                                to = apiDllPath + ".bak",
                                file_must_exist = true
                            };
                        else if (_SteamAPICheckBypassMode ==
                                 SteamStubUnpackerConfig.SteamAPICheckBypassModes.OnlyN)
                            jsonContent[apiDllPath] = new
                            {
                                mode = "file_redirect",
                                to = apiDllPath + ".bak",
                                file_must_exist = true,
                                hook_times_mode = "nth_time_only",
                                hook_time_n = _SteamAPICheckBypassNthTime
                            };
                        else if (_SteamAPICheckBypassMode ==
                                 SteamStubUnpackerConfig.SteamAPICheckBypassModes.OnlyNotN)
                            jsonContent[apiDllPath] = new
                            {
                                mode = "file_redirect",
                                to = apiDllPath + ".bak",
                                file_must_exist = true,
                                hook_times_mode = "not_nth_time_only",
                                hook_time_n = _SteamAPICheckBypassNthTime
                            };

                    var jsonString = JsonSerializer.Serialize(jsonContent,
                        new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(file), "SteamAPICheckBypass.json"),
                        jsonString);
                }
            }
            else
            {
                var f = new Pe32File(path);
                f.Parse();
                if (!f.IsFile64Bit())
                    dll = Path.Combine(dllPath, "SteamAPICheckBypass_x32.dll");
                else
                    dll = Path.Combine(dllPath, "SteamAPICheckBypass.dll");
                if (File.Exists(Path.Combine(Path.GetDirectoryName(path), "version.dll")))
                    _log.Information("Steam API Check Bypass dll already exists, skipping...");
                else
                    File.Copy(dll, Path.Combine(Path.GetDirectoryName(path), "version.dll"));
                var jsonContent = new Dictionary<string, object>
                {
                    {
                        Path.GetFileName(path),
                        new
                        {
                            mode = "file_redirect",
                            to = Path.GetFileName(path) + ".bak",
                            file_must_exist = true
                        }
                    }
                };

                var apidlls = Directory.GetFiles(Path.GetDirectoryName(path), "steam_api.dll",
                        SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(Path.GetDirectoryName(path), p)).ToArray();
                apidlls = apidlls.Concat(Directory
                    .GetFiles(Path.GetDirectoryName(path), "steam_api64.dll", SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(Path.GetDirectoryName(path), p))).ToArray();
                foreach (var apiDllPath in apidlls)
                    jsonContent[Path.Combine(Path.GetDirectoryName(apiDllPath), "steam_settings")] = new
                    {
                        mode = "file_hide"
                    };
                foreach (var apiDllPath in apidlls)
                    if (_SteamAPICheckBypassMode == SteamStubUnpackerConfig.SteamAPICheckBypassModes.All)
                        jsonContent[apiDllPath] = new
                        {
                            mode = "file_redirect",
                            to = apiDllPath + ".bak",
                            file_must_exist = true
                        };
                    else if (_SteamAPICheckBypassMode == SteamStubUnpackerConfig.SteamAPICheckBypassModes.OnlyN)
                        jsonContent[apiDllPath] = new
                        {
                            mode = "file_redirect",
                            to = apiDllPath + ".bak",
                            file_must_exist = true,
                            hook_times_mode = "nth_time_only",
                            hook_time_n = _SteamAPICheckBypassNthTime
                        };
                    else if (_SteamAPICheckBypassMode ==
                             SteamStubUnpackerConfig.SteamAPICheckBypassModes.OnlyNotN)
                        jsonContent[apiDllPath] = new
                        {
                            mode = "file_redirect",
                            to = apiDllPath + ".bak",
                            file_must_exist = true,
                            hook_times_mode = "not_nth_time_only",
                            hook_time_n = _SteamAPICheckBypassNthTime
                        };

                var jsonString = JsonSerializer.Serialize(jsonContent,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "SteamAPICheckBypass.json"),
                    jsonString);
            }

            _log.Information("Successfully applied SteamAPICheckBypass.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to apply SteamAPICheckBypass.");
            throw new Exception("Failed to apply SteamAPICheckBypass.");
        }
    }
}