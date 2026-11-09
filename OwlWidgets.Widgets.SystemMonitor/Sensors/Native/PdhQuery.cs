using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OwlWidgets.Widgets.SystemMonitor.Sensors.Native;

/// <summary>
/// A query of performance counters (pdh.dll). Counters are added by their English paths, which work on Windows
/// in any language; the localized paths of <c>PerformanceCounter</c> differ from one language to another.
/// A rate counter needs two collections: its first value appears after the second <see cref="Collect"/>.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x200;
    private const uint PDH_FMT_NOCAP100 = 0x8000;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_CSTATUS_VALID_DATA = 0;
    private const uint PDH_CSTATUS_NEW_DATA = 1;

    private nint _query;

    public PdhQuery()
    {
        Check(PdhOpenQueryW(null, 0, out _query), "PdhOpenQuery");
    }

    /// <param name="path">English path, for example <c>\Processor Information(_Total)\% Processor Utility</c>.</param>
    public nint Add(string path)
    {
        Check(PdhAddEnglishCounterW(_query, path, 0, out var counter), $"PdhAddEnglishCounter {path}");
        return counter;
    }

    public void Collect() => Check(PdhCollectQueryData(_query), "PdhCollectQueryData");

    /// <returns>null: the counter has no value yet or its instance is gone.</returns>
    public static double? Read(nint counter)
    {
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var value) != 0)
            return null;
        return value.CStatus is PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA ? value.DoubleValue : null;
    }

    /// <summary>The values of every instance of a counter with a wildcard instance, such as <c>\GPU Engine(*)\…</c>.</summary>
    public static unsafe List<(string Instance, double Value)> ReadAll(nint counter)
    {
        var result = new List<(string, double)>();
        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, null);
        if (status != PDH_MORE_DATA || size == 0)
            return result;

        var buffer = new byte[size];
        fixed (byte* pointer = buffer)
        {
            if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, pointer) != 0)
                return result;
            var items = (PDH_FMT_COUNTERVALUE_ITEM*)pointer;
            for (var i = 0; i < count; i++)
            {
                if (items[i].Value.CStatus is not (PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA))
                    continue;
                result.Add((new string(items[i].Name), items[i].Value.DoubleValue));
            }
        }

        return result;
    }

    public void Dispose()
    {
        if (_query == 0)
            return;
        PdhCloseQuery(_query);
        _query = 0;
    }

    private static void Check(uint status, string call)
    {
        if (status != 0)
            throw new Win32Exception(unchecked((int)status), $"{call} failed with 0x{status:X8}.");
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public char* Name;
        public PDH_FMT_COUNTERVALUE Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern unsafe uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, byte* items);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(nint query);
}
