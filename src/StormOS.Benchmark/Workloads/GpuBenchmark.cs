using System.Diagnostics;
using System.Runtime.InteropServices;
using StormOS.Core.Benchmark;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace StormOS.Benchmark.Workloads;

/// <summary>
/// GPU compute benchmark: a Direct3D 11 compute shader running a fused multiply-add chain. Execution time is measured
/// with GPU timestamp queries (not CPU wall time), and throughput is reported in GFLOPS (FMA = 2 FLOPs).
/// Each dispatch is kept well below the Windows GPU timeout (TDR).
/// </summary>
public sealed class GpuBenchmark : IBenchmark
{
    /// <summary>FLOPs per thread per loop iteration (8 float4 FMAs).</summary>
    public const int FlopsPerIteration = 8 * 4 * 2;

    private const int ThreadsPerGroup = 256;
    private const int Groups = 4096;

    private const string Shader = """
        RWStructuredBuffer<float4> Output : register(u0);
        cbuffer Params : register(b0) { uint Iterations; uint3 Padding; };

        [numthreads(256, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            float4 a = float4(id.x * 1e-7, 1.0001, 0.9999, 0.5);
            float4 b = float4(0.9998, 1.0002, 0.5, 1.0);
            const float4 c = float4(1e-7, 2e-7, 3e-7, 4e-7);
            [loop] for (uint i = 0; i < Iterations; i++)
            {
                a = mad(a, b, c); b = mad(b, a, c);
                a = mad(a, b, c); b = mad(b, a, c);
                a = mad(a, b, c); b = mad(b, a, c);
                a = mad(a, b, c); b = mad(b, a, c);
            }
            Output[id.x] = a + b;
        }
        """;

    /// <inheritdoc />
    public BenchmarkType Type => BenchmarkType.Gpu;

    /// <inheritdoc />
    public string Name => "GPU";

    /// <inheritdoc />
    public string? CheckAvailability() =>
        OperatingSystem.IsWindows() ? null : "The GPU benchmark requires Direct3D 11.";

    /// <summary>Computes GFLOPS from a measured dispatch.</summary>
    /// <param name="threads">Threads dispatched.</param>
    /// <param name="iterations">Loop iterations per thread.</param>
    /// <param name="seconds">GPU execution time.</param>
    /// <returns>GFLOPS.</returns>
    public static double Gflops(long threads, int iterations, double seconds) =>
        seconds > 0 ? threads * (double)iterations * FlopsPerIteration / seconds / 1e9 : 0;

    /// <inheritdoc />
    public Task<IReadOnlyList<BenchmarkMetric>> RunAsync(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => Run(options, progress, cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static IReadOnlyList<BenchmarkMetric> Run(BenchmarkRunOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        progress?.Report(new BenchmarkProgress(5, "Compiling compute shader"));
        D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, DeviceCreationFlags.None, [FeatureLevel.Level_11_0], out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        using (device)
        using (context)
        {
            var bytecode = Compiler.Compile(Shader, "main", "storm-gpu.hlsl", "cs_5_0");
            using var shader = device!.CreateComputeShader(bytecode.Span);
            const int Threads = ThreadsPerGroup * Groups;
            using var output = device.CreateBuffer(new BufferDescription(Threads * 16, BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
            using var view = device.CreateUnorderedAccessView(output, new UnorderedAccessViewDescription(output, Format.Unknown, 0, Threads));
            using var constants = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default));
            using var disjoint = device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
            using var start = device.CreateQuery(new QueryDescription(QueryType.Timestamp));
            using var end = device.CreateQuery(new QueryDescription(QueryType.Timestamp));

            context!.CSSetShader(shader);
            context.CSSetUnorderedAccessView(0, view);
            context.CSSetConstantBuffer(0, constants);

            var iterations = 64;
            var measure = new Func<int, double>(count =>
            {
                context.UpdateSubresource(new[] { (uint)count, 0u, 0u, 0u }, constants);
                context.Begin(disjoint);
                context.End(start);
                context.Dispatch(Groups, 1, 1);
                context.End(end);
                context.End(disjoint);
                return ReadSeconds(context, disjoint, start, end, cancellationToken);
            });

            progress?.Report(new BenchmarkProgress(15, "Calibrating"));
            var seconds = measure(iterations);
            while (seconds < 0.05 && iterations < 1 << 20)
            {
                iterations = (int)Math.Min(1 << 20, iterations * Math.Clamp(0.1 / Math.Max(seconds, 1e-4), 2, 16));
                seconds = measure(iterations);
            }

            progress?.Report(new BenchmarkProgress(30, "Measuring"));
            var results = new List<double>();
            var stopwatch = Stopwatch.StartNew();
            var budget = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(3).Ticks, options.Duration.Ticks));
            while (stopwatch.Elapsed < budget)
            {
                results.Add(Gflops(Threads, iterations, measure(iterations)));
                progress?.Report(new BenchmarkProgress(30 + (70 * stopwatch.Elapsed / budget), "Measuring"));
            }

            results.Sort();
            var median = results[results.Count / 2];
            return
            [
                new("gpu.compute.gflops", "FP32 compute (FMA)", median, "GFLOPS"),
                new("gpu.compute.peak", "Best dispatch", results[^1], "GFLOPS"),
            ];
        }
    }

    private static double ReadSeconds(ID3D11DeviceContext context, ID3D11Query disjoint, ID3D11Query start, ID3D11Query end, CancellationToken cancellationToken)
    {
        var disjointData = WaitFor<QueryDataTimestampDisjoint>(context, disjoint, cancellationToken);
        var begin = WaitFor<ulong>(context, start, cancellationToken);
        var finish = WaitFor<ulong>(context, end, cancellationToken);
        if (disjointData.Disjoint || disjointData.Frequency == 0)
        {
            return double.NaN;
        }

        return (finish - begin) / (double)disjointData.Frequency;
    }

    private static unsafe T WaitFor<T>(ID3D11DeviceContext context, ID3D11Asynchronous query, CancellationToken cancellationToken)
        where T : unmanaged
    {
        T value = default;
        var timeout = Stopwatch.StartNew();
        while (context.GetData(query, (IntPtr)(&value), (uint)Marshal.SizeOf<T>(), AsyncGetDataFlags.None).Code != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The GPU did not finish the benchmark dispatch.");
            }

            Thread.Yield();
        }

        return value;
    }
}
