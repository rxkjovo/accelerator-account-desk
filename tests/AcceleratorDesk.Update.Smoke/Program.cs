using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;
using AcceleratorDesk.Update;

if (args.Length != 4) { Console.Error.WriteLine("Arguments: old-publish-directory new-publish-directory archive output-directory"); return 2; }
var oldPublish = Path.GetFullPath(args[0]); var newPublish = Path.GetFullPath(args[1]); var sourceArchive = Path.GetFullPath(args[2]); var root = Path.GetFullPath(args[3]);
if (Directory.Exists(root)) throw new InvalidOperationException("Use a fresh isolated output directory.");
Directory.CreateDirectory(root);
var reports = new List<object>(); var failures = 0;
foreach (var scenario in new[] { "success", "bad-hash", "startup-failure" })
{
    var directory = Path.Combine(root, scenario); var current = Path.Combine(directory, "previous"); var data = Path.Combine(directory, "独立记录 带空格");
    Directory.CreateDirectory(directory); CopyDirectory(oldPublish, current);
    foreach (var helperFile in new[] { "AcceleratorDesk.Updater.exe", "AcceleratorDesk.Updater.dll", "AcceleratorDesk.Updater.deps.json", "AcceleratorDesk.Updater.runtimeconfig.json", "AcceleratorDesk.Update.dll" })
        File.Copy(Path.Combine(newPublish, helperFile), Path.Combine(current, helperFile), true);
    Directory.CreateDirectory(data);
    var repository = new DeskRepository(Path.Combine(data, "accounts.db")); repository.Initialize();
    var phone = new PhoneRecord { Number = "13800138000", Label = "isolated update fixture" }; repository.SavePhone(phone);
    var account = new AccountRecord { Identifier = "9081762354", PhoneId = phone.Id, Nickname = "isolated update fixture", ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) };
    var credentials = new AccountCredentialService(repository); const string fixturePassword = "SmokeFixture27!"; credentials.Save(account, fixturePassword);
    var protectedBefore = repository.GetProtectedPassword(account.Id)!;
    var sentinels = new Dictionary<string, byte[]> { ["settings.json"] = Encoding.UTF8.GetBytes("{\"ClientExecutable\":\"\"}"), ["redemption-codes.json"] = Encoding.UTF8.GetBytes("[\"IsolatedFixtureCode\"]"), ["qq-registration-draft.bin"] = [1, 2, 3, 4], ["qq-browser/fixture.txt"] = Encoding.UTF8.GetBytes("isolated browser fixture") };
    foreach (var entry in sentinels) { var file = Path.Combine(data, entry.Key); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, entry.Value); }
    File.WriteAllText(Path.Combine(current, "keep-original.txt"), "original installation retained");
    Process? parent = null; Process? helper = null;
    try
    {
        var cache = Path.Combine(data, "updates", "downloads"); Directory.CreateDirectory(cache);
        var archivePath = Path.Combine(cache, "fixture.zip"); File.Copy(sourceArchive, archivePath);
        var originalManifest = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourceArchive)!, "update-manifest.json")))!;
        var manifest = originalManifest;
        if (scenario == "bad-hash") { using var file = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite); var first = file.ReadByte(); file.Position = 0; file.WriteByte((byte)(first ^ 1)); }
        if (scenario == "startup-failure")
        {
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
            { archive.GetEntry("加速账户助手.exe")!.Delete(); using var invalid = archive.CreateEntry("加速账户助手.exe").Open(); invalid.Write(Encoding.UTF8.GetBytes("deliberately invalid isolated executable")); }
            using var stream = File.OpenRead(archivePath); manifest = originalManifest with { ArchiveSize = stream.Length, Sha256 = Convert.ToHexString(SHA256.HashData(stream)) };
        }
        parent = Start(current, data); await WaitAsync(() => !parent.HasExited && parent.MainWindowHandle != IntPtr.Zero, TimeSpan.FromSeconds(15));
        var jobPath = Path.Combine(data, "updates", "job-" + Guid.NewGuid().ToString("N"), "job.json");
        var job = new UpdateInstallJob(current, data, archivePath, manifest, "rxkjovo/accelerator-account-desk", parent.Id, parent.StartTime.ToUniversalTime().Ticks); UpdateHandoff.WriteJson(jobPath, job);
        var info = new ProcessStartInfo(Path.Combine(current, "AcceleratorDesk.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = current }; info.ArgumentList.Add(jobPath);
        helper = Process.Start(info)!;
        var readyPath = Path.Combine(Path.GetDirectoryName(jobPath)!, "ready.json");
        await WaitAsync(() => File.Exists(readyPath) || helper.HasExited, TimeSpan.FromSeconds(12));
        Equal(true, File.Exists(readyPath)); Equal(true, JsonSerializer.Deserialize<UpdateHelperReady>(File.ReadAllText(readyPath))!.Ready);
        Equal(true, parent.CloseMainWindow()); using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10))) await parent.WaitForExitAsync(timeout.Token);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50))) await helper.WaitForExitAsync(timeout.Token);
        var result = JsonSerializer.Deserialize<UpdateInstallResult>(File.ReadAllText(Path.Combine(data, "updates", "update-result.json")))!;
        Equal(scenario == "success", result.Succeeded); Equal(scenario == "success" ? 0 : 1, helper.ExitCode);
        var expectedDirectory = scenario == "success" ? UpdateHandoff.TargetDirectory(job) : current;
        await WaitAsync(() => FindOwned(expectedDirectory).Any(), TimeSpan.FromSeconds(12));
        Equal(true, repository.GetAccounts().Single().Id == account.Id); Equal(phone.Id, repository.GetPhones().Single().Id);
        Equal(fixturePassword, credentials.ReadPassword(account.Id)); Equal(true, protectedBefore.SequenceEqual(repository.GetProtectedPassword(account.Id)!));
        foreach (var sentinel in sentinels) Equal(true, sentinel.Value.SequenceEqual(File.ReadAllBytes(Path.Combine(data, sentinel.Key))));
        Equal("original installation retained", File.ReadAllText(Path.Combine(current, "keep-original.txt")));
        if (scenario == "success")
        {
            var receipt = JsonSerializer.Deserialize<UpdateStartupReceipt>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(jobPath)!, "started.json")))!;
            Equal(true, receipt.Succeeded); Equal(manifest.Version, receipt.Version); Equal(false, File.Exists(Path.Combine(expectedDirectory, "accounts.db")));
        }
        Console.WriteLine("PASS UPDATE SMOKE " + scenario + ": actual helper, actual app window, custom data and encrypted password preserved");
        reports.Add(new { Scenario = scenario, Passed = true, Result = result, DataPreserved = true, PasswordDecryptionVerified = true, RealApplicationOpened = true });
    }
    catch (Exception error) { failures++; Console.WriteLine("FAIL UPDATE SMOKE " + scenario + ": " + error.Message); reports.Add(new { Scenario = scenario, Passed = false, Message = error.Message }); }
    finally
    {
        // Only processes whose full executable path is inside this freshly created fixture are closed.
        foreach (var candidate in FindOwned(current).Concat(FindOwned(Path.Combine(directory, "加速账户助手-" + JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourceArchive)!, "update-manifest.json")))!.Version))))
        {
            using (candidate)
            {
                if (!candidate.HasExited)
                {
                    try { await WaitAsync(() => candidate.HasExited || candidate.MainWindowHandle != IntPtr.Zero, TimeSpan.FromSeconds(12)); } catch { }
                    candidate.Refresh();
                    if (!candidate.HasExited) { candidate.CloseMainWindow(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); try { await candidate.WaitForExitAsync(timeout.Token); } catch { } }
                }
            }
        }
        parent?.Dispose(); helper?.Dispose();
    }
}
File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT UPDATE SMOKE {3 - failures}/3 passed; {failures} failed"); return failures == 0 ? 0 : 1;

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    { var target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
}
static Process Start(string directory, string data)
{
    var info = new ProcessStartInfo(Path.Combine(directory, "加速账户助手.exe")) { UseShellExecute = false, WorkingDirectory = directory }; info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(data); return Process.Start(info)!;
}
static IEnumerable<Process> FindOwned(string directory)
{
    foreach (var process in Process.GetProcessesByName("加速账户助手"))
    {
        bool matches; try { matches = !process.HasExited && UpdateHandoff.SamePath(process.MainModule!.FileName, Path.Combine(directory, "加速账户助手.exe")); } catch { matches = false; }
        if (matches) yield return process; else process.Dispose();
    }
}
static async Task WaitAsync(Func<bool> predicate, TimeSpan timeout)
{
    var elapsed = Stopwatch.StartNew(); while (!predicate()) { if (elapsed.Elapsed > timeout) throw new TimeoutException("Isolated process check timed out."); await Task.Delay(100); }
}
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Fixture verification did not match its expected state."); }
