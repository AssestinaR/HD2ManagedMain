using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HD2ModManager.Services;

// Purpose: Samples process and machine resource usage during long-running operations without changing operation scheduling.
public sealed class ResourceUsageMonitor : IAsyncDisposable
{
	private readonly Guid _operationId;
	private readonly string _operationName;
	private readonly TimeSpan _interval;
	private readonly CancellationTokenSource _stopSource = new();
	private readonly Process _process;
	private readonly Stopwatch _clock = Stopwatch.StartNew();
	private readonly Task _samplingTask;
	private TimeSpan _lastCpu;
	private TimeSpan _lastSampleTime;
	private double _peakCpuPercent;
	private long _peakWorkingSet;
	private long _peakPrivateMemory;
	private long _peakManagedHeap;
	private int _sampleCount;
	private static readonly bool LogDetailedSamples = false;

	private ResourceUsageMonitor(Guid operationId, string operationName, TimeSpan interval)
	{
		_operationId = operationId;
		_operationName = operationName;
		_interval = interval;
		_process = Process.GetCurrentProcess();
		_lastCpu = _process.TotalProcessorTime;
		_lastSampleTime = _clock.Elapsed;
		_samplingTask = SampleAsync();
	}

	public static ResourceUsageMonitor Start(Guid operationId, string operationName, TimeSpan? interval = null)
	{
		if (operationId == Guid.Empty) throw new ArgumentException("操作 ID 不能为 Guid.Empty。", nameof(operationId));
		if (string.IsNullOrWhiteSpace(operationName)) throw new ArgumentException("操作名称不能为空。", nameof(operationName));
		return new ResourceUsageMonitor(operationId, operationName, interval ?? TimeSpan.FromSeconds(1));
	}

	private async Task SampleAsync()
	{
		LogService.Info($"资源监控开始：操作={_operationName}，operation={_operationId:N}，逻辑处理器={Environment.ProcessorCount}。");
		try
		{
			while (true)
			{
				await Task.Delay(_interval, _stopSource.Token).ConfigureAwait(false);
				Sample();
			}
		}
		catch (OperationCanceledException) when (_stopSource.IsCancellationRequested)
		{
			// Normal shutdown when the operation finishes or the application closes.
		}
		catch (Exception exception)
		{
			LogService.Warn($"资源监控采样失败：操作={_operationName}，operation={_operationId:N}，错误={exception.Message}。");
		}
	}

	private void Sample()
	{
		_process.Refresh();
		var now = _clock.Elapsed;
		var wallSeconds = (now - _lastSampleTime).TotalSeconds;
		var cpu = _process.TotalProcessorTime;
		var cpuPercent = wallSeconds > 0
			? (cpu - _lastCpu).TotalSeconds / wallSeconds / Math.Max(1, Environment.ProcessorCount) * 100d
			: 0d;
		var workingSet = _process.WorkingSet64;
		var privateMemory = _process.PrivateMemorySize64;
		var managedHeap = GC.GetGCMemoryInfo().HeapSizeBytes;
		var memory = ReadMemoryStatus();
		_peakCpuPercent = Math.Max(_peakCpuPercent, cpuPercent);
		_peakWorkingSet = Math.Max(_peakWorkingSet, workingSet);
		_peakPrivateMemory = Math.Max(_peakPrivateMemory, privateMemory);
		_peakManagedHeap = Math.Max(_peakManagedHeap, managedHeap);
		_sampleCount++;
		if (!LogDetailedSamples)
		{
			_lastCpu = cpu;
			_lastSampleTime = now;
			return;
		}
		LogService.Info($"资源采样：操作={_operationName}，operation={_operationId:N}，样本={_sampleCount}，CPU={cpuPercent:0.0}%（进程/逻辑核归一化），工作集={ToMb(workingSet):0}MB，私有内存={ToMb(privateMemory):0}MB，托管堆={ToMb(managedHeap):0}MB，系统内存={ToMb(memory.Total):0}MB，可用={ToMb(memory.Available):0}MB，可用率={memory.AvailablePercent:0.0}%。");
		_lastCpu = cpu;
		_lastSampleTime = now;
	}

	public async ValueTask DisposeAsync()
	{
		_stopSource.Cancel();
		try
		{
			await _samplingTask.ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogService.Warn($"资源监控停止异常：操作={_operationName}，operation={_operationId:N}，错误={exception.Message}。");
		}
		_process.Dispose();
		_stopSource.Dispose();
		LogService.Info($"资源监控结束：操作={_operationName}，operation={_operationId:N}，样本={_sampleCount}，峰值CPU={_peakCpuPercent:0.0}%，峰值工作集={ToMb(_peakWorkingSet):0}MB，峰值私有内存={ToMb(_peakPrivateMemory):0}MB，峰值托管堆={ToMb(_peakManagedHeap):0}MB。");
	}

	private static double ToMb(long bytes) => bytes / 1024d / 1024d;
	private static double ToMb(ulong bytes) => bytes / 1024d / 1024d;

	private static MemoryStatus ReadMemoryStatus()
	{
		var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
		return GlobalMemoryStatusEx(ref status)
			? new MemoryStatus(status.TotalPhys, status.AvailPhys)
			: new MemoryStatus(0, 0);
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatus
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;

		public MemoryStatus(ulong total, ulong available)
		{
			Length = 0;
			MemoryLoad = 0;
			TotalPhys = total;
			AvailPhys = available;
			TotalPageFile = 0;
			AvailPageFile = 0;
			TotalVirtual = 0;
			AvailVirtual = 0;
			AvailExtendedVirtual = 0;
		}

		public ulong Total => TotalPhys;
		public ulong Available => AvailPhys;
		public double AvailablePercent => TotalPhys > 0 ? (double)AvailPhys / TotalPhys * 100d : 0d;
	}
}
