using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace FoxuMarsDisplay.Core.Sensors;

/// <summary>
/// Low-overhead hardware sensor provider:
/// - NVIDIA GPU via direct nvml.dll P/Invoke (sub-millisecond)
/// - CPU &amp; RAM via LibreHardwareMonitorLib 0.9.6 (PawnIO native) with unused subsystems disabled
/// - Network throughput via NetworkInterface delta bytes/sec
/// </summary>
public sealed class LightweightSensorProvider : IDisposable
{
    private Computer? _computer;
    private bool _nvmlAvailable;
    private IntPtr _nvmlDevice;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private SensorSnapshot _current = new();
    private long _prevNetSent;
    private long _prevNetRecv;
    private long _prevNetTickMs;
    private volatile bool _disposed;

    public SensorSnapshot Current => Volatile.Read(ref _current);

    public void Start(int pollIntervalMs = 1000)
    {
        if (_disposed || _pollTask != null)
            return;

        InitNvml();
        InitLhm();
        SampleNetworkDelta(out _, out _); // Prime baseline
        SampleOnce();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _pollTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(250, pollIntervalMs)));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    SampleOnce();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }, token);
    }

    private void InitNvml()
    {
        try
        {
            if (NvmlNative.nvmlInit_v2() == 0 &&
                NvmlNative.nvmlDeviceGetHandleByIndex_v2(0, out _nvmlDevice) == 0)
            {
                _nvmlAvailable = true;
            }
        }
        catch
        {
            _nvmlAvailable = false;
        }
    }

    private void InitLhm()
    {
        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsMemoryEnabled = true,
                IsGpuEnabled = !_nvmlAvailable,
                IsMotherboardEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
                IsControllerEnabled = false,
                IsPsuEnabled = false,
                IsBatteryEnabled = false
            };
            computer.Open();
            _computer = computer;
        }
        catch
        {
            _computer = null;
        }
    }

    public void SampleOnce()
    {
        double cpuTemp = 0, cpuUsage = 0, cpuFreq = 0, cpuPower = 0, cpuVolt = 0;
        double gpuTemp = 0, gpuUsage = 0, gpuFreq = 0, gpuPower = 0, gpuMemLoad = 0;
        double memPct = 0, memUsedGb = 0;

        if (_nvmlAvailable && _nvmlDevice != IntPtr.Zero)
        {
            try
            {
                if (NvmlNative.nvmlDeviceGetTemperature(_nvmlDevice, 0, out uint t) == 0)
                    gpuTemp = t;
                if (NvmlNative.nvmlDeviceGetUtilizationRates(_nvmlDevice, out var util) == 0)
                    gpuUsage = util.gpu;
                if (NvmlNative.nvmlDeviceGetPowerUsage(_nvmlDevice, out uint mw) == 0)
                    gpuPower = mw / 1000.0;
                if (NvmlNative.nvmlDeviceGetClockInfo(_nvmlDevice, 0, out uint mhz) == 0)
                    gpuFreq = mhz;
                if (NvmlNative.nvmlDeviceGetMemoryInfo(_nvmlDevice, out var mem) == 0 && mem.total > 0)
                    gpuMemLoad = (mem.used * 100.0) / mem.total;
            }
            catch
            {
                // Ignore transient NVML read errors.
            }
        }

        if (_computer != null)
        {
            try
            {
                int cpuTempRank = int.MaxValue;
                int cpuPowerRank = int.MaxValue;

                foreach (var hw in _computer.Hardware)
                {
                    hw.Update();
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            float v = s.Value.Value;

                            switch (s.SensorType)
                            {
                                case SensorType.Temperature when v > 0:
                                {
                                    int rank = GetCpuTempPriority(s.Name);
                                    if (rank < cpuTempRank)
                                    {
                                        cpuTempRank = rank;
                                        cpuTemp = v;
                                    }
                                    break;
                                }
                                case SensorType.Load when s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase):
                                    cpuUsage = v;
                                    break;
                                case SensorType.Clock when !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase):
                                    if (s.Name.Contains("Core #1", StringComparison.OrdinalIgnoreCase) || cpuFreq == 0)
                                        cpuFreq = v;
                                    break;
                                case SensorType.Power when v > 0:
                                {
                                    int rank = GetCpuPowerPriority(s.Name);
                                    if (rank < cpuPowerRank)
                                    {
                                        cpuPowerRank = rank;
                                        cpuPower = v;
                                    }
                                    break;
                                }
                                case SensorType.Voltage when cpuVolt == 0 && v > 0:
                                    cpuVolt = v;
                                    break;
                            }
                        }
                    }
                    else if (hw.HardwareType == HardwareType.Memory)
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            if (s.SensorType == SensorType.Load && s.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase))
                                memPct = s.Value.Value;
                            else if (s.SensorType == SensorType.Data && s.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase))
                                memUsedGb = s.Value.Value;
                        }
                    }
                    else if (!_nvmlAvailable &&
                             (hw.HardwareType == HardwareType.GpuNvidia ||
                              hw.HardwareType == HardwareType.GpuAmd ||
                              hw.HardwareType == HardwareType.GpuIntel))
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            float v = s.Value.Value;
                            if (s.SensorType == SensorType.Temperature && gpuTemp == 0) gpuTemp = v;
                            else if (s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) gpuUsage = v;
                            else if (s.SensorType == SensorType.Power && gpuPower == 0) gpuPower = v;
                            else if (s.SensorType == SensorType.Clock && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) gpuFreq = v;
                        }
                    }
                }
            }
            catch
            {
                // Ignore LHM transient errors.
            }
        }

        if (cpuTemp <= 0)
        {
            double tzTemp = TryReadAcpiThermalZoneCelsius();
            if (tzTemp > 0)
                cpuTemp = tzTemp;
        }

        if (memPct <= 0)
        {
            var memStatus = MemoryStatusEx.Query();
            if (memStatus.dwMemoryLoad > 0)
            {
                memPct = memStatus.dwMemoryLoad;
                memUsedGb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
            }
        }

        SampleNetworkDelta(out double netUpKbps, out double netDownKbps);

        Volatile.Write(ref _current, new SensorSnapshot
        {
            CpuTemp = cpuTemp,
            CpuUsage = cpuUsage,
            CpuFrequencyMhz = cpuFreq,
            CpuPowerW = cpuPower,
            CpuVoltageV = cpuVolt,
            GpuTemp = gpuTemp,
            GpuUsage = gpuUsage,
            GpuFrequencyMhz = gpuFreq,
            GpuPowerW = gpuPower,
            GpuMemoryLoad = gpuMemLoad,
            MemoryUsagePct = memPct,
            MemoryUsedGb = memUsedGb,
            NetUpKbps = netUpKbps,
            NetDownKbps = netDownKbps,
            Now = DateTime.Now
        });
    }

    private static int GetCpuTempPriority(string name)
    {
        if (name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains("Core Average", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains("Core Max", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.Contains("Core", StringComparison.OrdinalIgnoreCase)) return 5;
        return 10;
    }

    private static int GetCpuPowerPriority(string name)
    {
        if (name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains("Cores", StringComparison.OrdinalIgnoreCase)) return 2;
        return 10;
    }

    private void SampleNetworkDelta(out double upKbps, out double downKbps)
    {
        upKbps = 0;
        downKbps = 0;
        try
        {
            long sent = 0, recv = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                var stats = ni.GetIPv4Statistics();
                sent += stats.BytesSent;
                recv += stats.BytesReceived;
            }

            long nowMs = Environment.TickCount64;
            if (_prevNetTickMs > 0 && nowMs > _prevNetTickMs)
            {
                double dt = (nowMs - _prevNetTickMs) / 1000.0;
                upKbps = Math.Max(0, (sent - _prevNetSent) / 1024.0 / dt);
                downKbps = Math.Max(0, (recv - _prevNetRecv) / 1024.0 / dt);
            }

            _prevNetSent = sent;
            _prevNetRecv = recv;
            _prevNetTickMs = nowMs;
        }
        catch
        {
            // Ignore transient NIC enumeration errors.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try { _cts?.Cancel(); } catch { }
        try { _pollTask?.Wait(500); } catch { }
        _cts?.Dispose();

        try { _computer?.Close(); } catch { }
        _computer = null;

        if (_nvmlAvailable)
        {
            try { NvmlNative.nvmlShutdown(); } catch { }
            _nvmlAvailable = false;
        }

        if (_pdhQuery != IntPtr.Zero)
        {
            try { PdhNative.PdhCloseQuery(_pdhQuery); } catch { }
            _pdhQuery = IntPtr.Zero;
            _pdhCounter = IntPtr.Zero;
        }
    }

    private static class NvmlNative
    {
        private const string Dll = "nvml.dll";

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlUtilization
        {
            public uint gpu;
            public uint memory;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlMemory
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlInit_v2();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlShutdown();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint powerMw);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint clockMhz);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        public static MemoryStatusEx Query()
        {
            var s = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            GlobalMemoryStatusEx(ref s);
            return s;
        }
    }

    private IntPtr _pdhQuery;
    private IntPtr _pdhCounter;
    private bool _pdhInitAttempted;

    private double TryReadAcpiThermalZoneCelsius()
    {
        try
        {
            if (!_pdhInitAttempted)
            {
                _pdhInitAttempted = true;
                if (PdhNative.PdhOpenQueryW(null, IntPtr.Zero, out _pdhQuery) == 0)
                {
                    if (PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\Thermal Zone Information(\_TZ.TZ00)\High Precision Temperature", IntPtr.Zero, out _pdhCounter) != 0)
                    {
                        PdhNative.PdhAddEnglishCounterW(_pdhQuery, @"\Thermal Zone Information(*)\High Precision Temperature", IntPtr.Zero, out _pdhCounter);
                    }
                    PdhNative.PdhCollectQueryData(_pdhQuery);
                }
            }

            if (_pdhQuery != IntPtr.Zero && _pdhCounter != IntPtr.Zero &&
                PdhNative.PdhCollectQueryData(_pdhQuery) == 0 &&
                PdhNative.PdhGetFormattedCounterValue(_pdhCounter, 0x00000200, out _, out var val) == 0 &&
                val.CStatus == 0 && val.DoubleValue > 2000)
            {
                return (val.DoubleValue - 2731.5) / 10.0;
            }
        }
        catch
        {
            // Non-fatal fallback
        }
        return 0;
    }

    private static class PdhNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PdhFmtCounterValue
        {
            public uint CStatus;
            public double DoubleValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern int PdhOpenQueryW(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        public static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")]
        public static extern int PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll")]
        public static extern int PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PdhFmtCounterValue pValue);

        [DllImport("pdh.dll")]
        public static extern int PdhCloseQuery(IntPtr hQuery);
    }
}
