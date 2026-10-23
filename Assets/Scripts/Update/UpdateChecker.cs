using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace GoingBatty
{
    /// <summary>
    /// On startup, asks GitHub for the latest release of this repo. If it's newer than this
    /// build, the main menu shows "Update available" with Yes / No.
    ///   Yes: download the Windows zip, quit, swap the files in place, relaunch.
    ///   No:  hide the prompt for the rest of this session.
    /// Releases are produced automatically by .github/workflows/release.yml when a version tag
    /// (e.g. v0.2.0) is pushed — which is how a Claude prompt ships an update to every player.
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        public static UpdateChecker I { get; private set; }

        public enum State { Idle, Checking, UpToDate, Available, Dismissed, Downloading, Installing, Failed }
        public State Status { get; private set; } = State.Idle;
        public string LatestVersion { get; private set; } = "";
        public string ReleaseNotes { get; private set; } = "";
        public float Progress { get; private set; }
        public string Error { get; private set; } = "";

        string _zipUrl;

        [Serializable] class GhAsset { public string name; public string browser_download_url; }
        [Serializable] class GhRelease { public string tag_name; public string body; public bool draft; public bool prerelease; public GhAsset[] assets; }

        void Awake() { I = this; }

        void Start()
        {
            if (Application.isEditor) { Status = State.UpToDate; return; }
            StartCoroutine(Check());
        }

        IEnumerator Check()
        {
            Status = State.Checking;
            string url = $"https://api.github.com/repos/{BuildInfo.RepoOwner}/{BuildInfo.RepoName}/releases/latest";
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", "GoingBatty-Updater");
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("[Update] check failed: " + req.error);
                Status = State.UpToDate; // stay quiet if offline
                yield break;
            }

            GhRelease rel = null;
            try { rel = JsonUtility.FromJson<GhRelease>(req.downloadHandler.text); } catch { }
            if (rel == null || string.IsNullOrEmpty(rel.tag_name)) { Status = State.UpToDate; yield break; }

            LatestVersion = rel.tag_name.TrimStart('v', 'V');
            ReleaseNotes = rel.body ?? "";
            if (rel.assets != null)
                foreach (var a in rel.assets)
                    if (a.name != null && a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && a.name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0)
                        _zipUrl = a.browser_download_url;

            Status = IsNewer(LatestVersion, Application.version) && _zipUrl != null ? State.Available : State.UpToDate;
            Debug.Log($"[Update] current {Application.version}, latest {LatestVersion} -> {Status}");
        }

        public static bool IsNewer(string latest, string current)
        {
            int[] Parse(string v)
            {
                var parts = v.Split('-')[0].Split('.');
                var n = new int[3];
                for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out n[i]);
                return n;
            }
            var a = Parse(latest); var b = Parse(current);
            for (int i = 0; i < 3; i++) { if (a[i] != b[i]) return a[i] > b[i]; }
            return false;
        }

        public void Dismiss() { if (Status == State.Available) Status = State.Dismissed; }

        public void Accept()
        {
            if (Status != State.Available && Status != State.Failed) return;
            StartCoroutine(DownloadAndInstall());
        }

        IEnumerator DownloadAndInstall()
        {
            Status = State.Downloading;
            Progress = 0;
            string dir = Path.Combine(Path.GetTempPath(), "GoingBattyUpdate");
            try { Directory.CreateDirectory(dir); } catch { }
            string zip = Path.Combine(dir, "update.zip");
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }

            using (var req = new UnityWebRequest(_zipUrl, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true };
                req.SetRequestHeader("User-Agent", "GoingBatty-Updater");
                var op = req.SendWebRequest();
                while (!op.isDone) { Progress = req.downloadProgress; yield return null; }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Fail("Download failed: " + req.error);
                    yield break;
                }
            }

            Status = State.Installing;
            Progress = 1;
            yield return null;

            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string installDir = Path.GetDirectoryName(exe);
                string script = Path.Combine(dir, "apply_update.ps1");
                File.WriteAllText(script, PowerShellScript);
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -Zip \"{zip}\" -Dest \"{installDir}\" -Exe \"{exe}\" -GamePid {Process.GetCurrentProcess().Id}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
                Application.Quit();
            }
            catch (Exception e)
            {
                Fail("Couldn't start the installer: " + e.Message);
            }
        }

        void Fail(string msg)
        {
            Error = msg;
            Status = State.Failed;
            Debug.LogWarning("[Update] " + msg);
        }

        // Waits for the game to close, unpacks the new build over the install folder, relaunches.
        const string PowerShellScript = @"param([string]$Zip, [string]$Dest, [string]$Exe, [int]$GamePid)
$ErrorActionPreference = 'Stop'
try { Wait-Process -Id $GamePid -Timeout 30 -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Milliseconds 700
$tmp = Join-Path $env:TEMP ('GoingBattyUnpack_' + [guid]::NewGuid().ToString('N'))
Expand-Archive -Path $Zip -DestinationPath $tmp -Force
$src = $tmp
$items = @(Get-ChildItem -Path $tmp)
if ($items.Count -eq 1 -and $items[0].PSIsContainer) { $src = $items[0].FullName }
Copy-Item -Path (Join-Path $src '*') -Destination $Dest -Recurse -Force
Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $Zip -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $Exe
";
    }
}
