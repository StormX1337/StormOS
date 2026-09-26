using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using StormOS.Core.Benchmark;

namespace StormOS.Benchmark.Workloads;

/// <summary>
/// Disk benchmark using an unbuffered temporary file (FILE_FLAG_NO_BUFFERING + write-through) so results reflect
/// the drive, not the Windows file cache: sequential write, sequential read and 4 KiB random read at queue depth 1.
/// The temporary file is always deleted.
/// </summary>
public sealed unsafe class DiskBenchmark : IBenchmark
{
    private const FileOptions NoBuffering = (FileOptions)0x20000000;
    private const int BlockSize = 1024 * 1024;
    private const int RandomBlock = 4096;

    /// <inheritdoc />
    public BenchmarkType Type => BenchmarkType.Disk;

    /// <inheritdoc />
    public string Name => "Disk";

    /// <inheritdoc />
    public string? CheckAvailability() => null;

    /// <summary>Returns the default test directory.</summary>
    /// <returns>The directory.</returns>
    public static string DefaultDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StormOS", "benchmark");

    /// <inheritdoc />
    public Task<IReadOnlyList<BenchmarkMetric>> RunAsync(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => Run(options, progress, cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static IReadOnlyList<BenchmarkMetric> Run(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directory = options.DiskTestDirectory ?? DefaultDirectory();
        Directory.CreateDirectory(directory);
        var size = Math.Clamp(options.DiskTestFileBytes / BlockSize * BlockSize, 64L * BlockSize, 4096L * BlockSize);
        var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
        if (free < size * 2)
        {
            throw new InvalidOperationException("Not enough free space for the disk benchmark.");
        }

        var path = Path.Combine(directory, $"storm-bench-{Guid.NewGuid():N}.tmp");
        var buffer = (byte*)NativeMemory.AlignedAlloc(BlockSize, 4096);
        try
        {
            new Span<byte>(buffer, BlockSize).Fill(0xA5);
            var options1 = OperatingSystem.IsWindows() ? NoBuffering | FileOptions.WriteThrough : FileOptions.WriteThrough;

            progress?.Report(new BenchmarkProgress(5, "Sequential write"));
            double writeMbps;
            using (var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, options1, size))
            {
                var stopwatch = Stopwatch.StartNew();
                for (long offset = 0; offset < size; offset += BlockSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RandomAccess.Write(handle, new ReadOnlySpan<byte>(buffer, BlockSize), offset);
                }

                writeMbps = size / stopwatch.Elapsed.TotalSeconds / (1024 * 1024);
            }

            progress?.Report(new BenchmarkProgress(40, "Sequential read"));
            double readMbps;
            double iops;
            using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None, OperatingSystem.IsWindows() ? NoBuffering : FileOptions.None))
            {
                var stopwatch = Stopwatch.StartNew();
                for (long offset = 0; offset < size; offset += BlockSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RandomAccess.Read(handle, new Span<byte>(buffer, BlockSize), offset);
                }

                readMbps = size / stopwatch.Elapsed.TotalSeconds / (1024 * 1024);

                progress?.Report(new BenchmarkProgress(70, "4K random read"));
                iops = RandomReads(handle, size, buffer, TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(3).Ticks, options.Duration.Ticks / 3)), cancellationToken);
            }

            progress?.Report(new BenchmarkProgress(100, "Done"));
            return
            [
                new("disk.seqwrite.mbps", "Sequential write", writeMbps, "MB/s"),
                new("disk.seqread.mbps", "Sequential read", readMbps, "MB/s"),
                new("disk.rand4k.iops", "4K random read (QD1)", iops, "IOPS"),
                new("disk.rand4k.mbps", "4K random read (QD1)", iops * RandomBlock / (1024 * 1024), "MB/s"),
            ];
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // The file is removed on the next run's cleanup at worst.
            }
        }
    }

    private static double RandomReads(SafeFileHandle handle, long size, byte* buffer, TimeSpan duration, CancellationToken cancellationToken)
    {
        var random = new Random(7);
        var blocks = size / RandomBlock;
        long reads = 0;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = random.NextInt64(blocks) * RandomBlock;
            RandomAccess.Read(handle, new Span<byte>(buffer, RandomBlock), offset);
            reads++;
        }

        return reads / stopwatch.Elapsed.TotalSeconds;
    }
}
