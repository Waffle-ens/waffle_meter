using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// 게임 최적화 탭의 Windows I/O: 전용 VRAM 자동 감지, 아이온2 <c>Engine.ini</c> 경로 찾기, 우리 최적화 블록
/// 적용/되돌리기, 게임 실행 여부. 문자열 생성/제거 로직은 순수 <see cref="EngineIniOptimizer"/>가 갖고, 여기선
/// 파일·레지스트리·프로세스만 만진다.
/// </summary>
public sealed class GameOptimizerService
{
    // Process.GetProcessesByName 기준(확장자 없음). OverlayController의 "Aion2.exe"와 같은 프로세스.
    private const string AionProcess = "Aion2";

    // GPU 클래스 키. 각 어댑터가 0000/0001/… 하위키로 들어가고 HardwareInformation.qwMemorySize에 실제 VRAM.
    private const string GpuClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public readonly record struct Gpu(string Name, long VramBytes);

    /// <summary><c>%LOCALAPPDATA%\AION2\Saved\Config\Windows\Engine.ini</c>. 아직 없을 수 있다(적용 시 생성).</summary>
    public string EngineIniPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AION2", "Saved", "Config", "Windows", "Engine.ini");

    /// <summary>전용 VRAM이 가장 큰 GPU. WMI <c>Win32_VideoController.AdapterRAM</c>은 uint32라 4GB↑ 카드에서
    /// 값이 잘려(everyone≈4GB) 티어 분류에 못 쓰므로, 드라이버가 레지스트리에 쓰는 실제 바이트 값을 읽는다.
    /// 감지 실패 시 Name=""·VramBytes=0(호출부는 가장 보수적인 tier5로 폴백).</summary>
    public Gpu DetectGpu()
    {
        var best = new Gpu(string.Empty, 0);
        try
        {
            using RegistryKey? cls = Registry.LocalMachine.OpenSubKey(GpuClassKey);
            if (cls is null)
            {
                return best;
            }

            foreach (string sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !int.TryParse(sub, out _))
                {
                    continue; // 0000, 0001 … 숫자 하위키만 어댑터
                }

                using RegistryKey? k = cls.OpenSubKey(sub);
                if (k is null)
                {
                    continue;
                }

                long vram = ReadVram(k);
                if (vram <= best.VramBytes)
                {
                    continue;
                }

                best = new Gpu((k.GetValue("DriverDesc") as string) ?? "그래픽카드", vram);
            }
        }
        catch
        {
            // 감지 실패는 tier5 폴백으로 흡수 — 절대 던지지 않는다.
        }

        return best;
    }

    private static long ReadVram(RegistryKey k)
    {
        // 1순위: qwMemorySize (REG_QWORD = 실제 바이트)
        if (k.GetValue("HardwareInformation.qwMemorySize") is long qw && qw > 0)
        {
            return qw;
        }

        // 폴백: 드라이버마다 REG_DWORD 또는 4/8바이트 REG_BINARY
        return k.GetValue("HardwareInformation.MemorySize") switch
        {
            long l => l,
            int i => i < 0 ? (uint)i : i,
            byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
            byte[] b when b.Length == 4 => BitConverter.ToUInt32(b, 0),
            _ => 0,
        };
    }

    /// <summary>아이온2 게임 클라이언트가 실행 중인가(적용/되돌리기 전 경고용).</summary>
    public bool IsGameRunning()
    {
        Process[] procs = Process.GetProcessesByName(AionProcess);
        try
        {
            return procs.Length > 0;
        }
        finally
        {
            foreach (Process p in procs)
            {
                p.Dispose();
            }
        }
    }

    /// <summary>우리 최적화가 적용된 상태인가. 마커(주석)는 게임이 Engine.ini를 다시 쓰면 사라지므로
    /// 마커 유무가 아니라 <see cref="EngineIniOptimizer.IsApplied"/>(마커 + 관리 키 폴백)로 판정한다 —
    /// 종전에는 게임을 한 번 켜고 나면 실제로 적용돼 있어도 "미적용"으로 표시됐다.</summary>
    public bool IsApplied()
    {
        try
        {
            return File.Exists(EngineIniPath) && EngineIniOptimizer.IsApplied(File.ReadAllText(EngineIniPath));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>감지된 티어로 우리 블록을 적용/재적용한다. 폴더/파일이 없으면 만들고, 첫 적용 때 원본을 한 번
    /// <c>.waffle-backup</c>으로 복사해 둔다(되돌리기는 마커 제거로 하지만 안전망). 게임은 <b>재실행 후</b> 반영.</summary>
    public void Apply(EngineIniOptimizer.Tier tier, bool includeAdvanced)
    {
        string? dir = Path.GetDirectoryName(EngineIniPath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        BackupOnce();
        string existing = ReadNormalized();
        string next = EngineIniOptimizer.ApplyBlock(existing, EngineIniOptimizer.BuildBlock(tier, includeAdvanced));
        WriteCrlf(next);
    }

    /// <summary>우리 최적화만 제거한다(사용자의 다른 Engine.ini 설정은 그대로). 마커 블록이 살아 있으면 그
    /// 구간을, 게임이 마커를 지우고 키만 <c>[SystemSettings]</c>에 병합해 놨으면 그 키들을 걷어낸다 — 종전에는
    /// 마커가 없으면 조기 반환해 <b>되돌리기 버튼이 아무 일도 하지 않았다</b>. 적용된 게 없으면 무동작.</summary>
    public void Revert()
    {
        if (!File.Exists(EngineIniPath))
        {
            return;
        }

        string existing = ReadNormalized();
        if (!EngineIniOptimizer.IsApplied(existing))
        {
            return;
        }

        // 키 폴백으로 지우는 경우 사용자가 손수 넣은 같은 키까지 지워질 수 있다(출처를 구분할 방법이 없다).
        // 최초 적용 때 파일이 없었으면 백업도 없으므로, 여기서 '우리가 건드리기 직전 상태'를 남긴다.
        BackupOnce();
        WriteCrlf(EngineIniOptimizer.Remove(existing));
    }

    private string ReadNormalized() =>
        File.Exists(EngineIniPath) ? File.ReadAllText(EngineIniPath).Replace("\r\n", "\n") : string.Empty;

    // Engine.ini는 Windows 관례대로 CRLF로 쓴다(내부 로직은 \n 정규화).
    private void WriteCrlf(string content) => File.WriteAllText(EngineIniPath, content.Replace("\n", "\r\n"));

    /// <summary>우리가 이 파일을 <b>처음 수정하기 직전</b>의 상태를 한 번만 <c>.waffle-backup</c>으로 남긴다.
    /// 적용·되돌리기 양쪽에서 부른다 — 최초 적용 시점에 Engine.ini가 아직 없었으면(게임을 한 번도 안 켠 상태)
    /// 그때는 백업할 것이 없고, 이후 되돌리기가 첫 수정이 되기 때문이다.</summary>
    private void BackupOnce()
    {
        try
        {
            string bak = EngineIniPath + ".waffle-backup";
            if (File.Exists(EngineIniPath) && !File.Exists(bak))
            {
                File.Copy(EngineIniPath, bak);
            }
        }
        catch
        {
            // 백업 실패가 적용을 막지는 않는다(되돌리기는 마커 제거로도 된다).
        }
    }
}
