using OwlWidgets.Protocol;

namespace OwlWidgets.Logging;

/// <summary>
/// One record kept in memory. <paramref name="Log"/> is the log it was written to: null for the manager log,
/// otherwise the instance. <paramref name="InstanceId"/> marks a manager record that concerns an instance;
/// such a record is a copy of one in the instance log.
/// </summary>
internal sealed record LogEntry(long Sequence, LogRecord Record, Guid? Log, Guid? InstanceId)
{
    public bool IsManagerCopy => Log == null && InstanceId != null;
}

/// <summary>
/// The records of this session in memory, for the log screen: the latest <see cref="ManagerCapacity"/> of the
/// manager log and the latest <see cref="InstanceCapacity"/> of each instance log. Older ones are only in the files.
/// Thread-safe: records come from any thread, and <see cref="Changed"/> is raised on the writer's thread.
/// </summary>
internal sealed class LogFeed
{
    public const int ManagerCapacity = 1000;
    public const int InstanceCapacity = 500;

    private readonly object _gate = new();
    private readonly Queue<LogEntry> _manager = new();
    private readonly Dictionary<Guid, Queue<LogEntry>> _instances = [];
    private readonly int _managerCapacity;
    private readonly int _instanceCapacity;
    private long _sequence;

    public LogFeed() : this(ManagerCapacity, InstanceCapacity)
    {
    }

    public LogFeed(int managerCapacity, int instanceCapacity)
    {
        _managerCapacity = managerCapacity;
        _instanceCapacity = instanceCapacity;
    }

    /// <summary>
    /// A record was kept (<see cref="LogFeedChange.Added"/>, with the one it pushed out, if any)
    /// or records were dropped by <see cref="Clear"/> or <see cref="RemoveInstance"/> (<see cref="LogFeedChange.Reset"/>).
    /// </summary>
    public event Action<LogFeedChange>? Changed;

    public void AddManager(LogRecord record, Guid? instanceId) => Add(record, log: null, instanceId);

    public void AddInstance(Guid instanceId, LogRecord record) => Add(record, instanceId, instanceId: null);

    /// <summary>The manager log as kept now, oldest first.</summary>
    public IReadOnlyList<LogEntry> Manager()
    {
        lock (_gate)
            return _manager.ToArray();
    }

    /// <summary>The log of the instance as kept now, oldest first.</summary>
    public IReadOnlyList<LogEntry> Instance(Guid instanceId)
    {
        lock (_gate)
            return _instances.TryGetValue(instanceId, out var queue) ? queue.ToArray() : [];
    }

    /// <summary>
    /// Every log at once, oldest first: the instance logs and the manager log without the copies of instance records.
    /// </summary>
    public IReadOnlyList<LogEntry> All()
    {
        lock (_gate)
        {
            return _manager.Where(entry => !entry.IsManagerCopy)
                .Concat(_instances.Values.SelectMany(queue => queue))
                .OrderBy(entry => entry.Sequence)
                .ToArray();
        }
    }

    /// <summary>Drops the kept records of one log (null: the manager log); the files stay as they are.</summary>
    public void Clear(Guid? log)
    {
        lock (_gate)
        {
            if (log is { } id)
                _instances.Remove(id);
            else
                _manager.Clear();
        }

        Changed?.Invoke(LogFeedChange.Reset);
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            _manager.Clear();
            _instances.Clear();
        }

        Changed?.Invoke(LogFeedChange.Reset);
    }

    /// <summary>The instance was deleted: its log goes, and so do the copies of its records in the manager log.</summary>
    public void RemoveInstance(Guid instanceId)
    {
        lock (_gate)
        {
            _instances.Remove(instanceId);
            var kept = _manager.Where(entry => entry.InstanceId != instanceId).ToArray();
            _manager.Clear();
            foreach (var entry in kept)
                _manager.Enqueue(entry);
        }

        Changed?.Invoke(LogFeedChange.Reset);
    }

    private void Add(LogRecord record, Guid? log, Guid? instanceId)
    {
        LogEntry entry;
        LogEntry? evicted = null;
        lock (_gate)
        {
            entry = new LogEntry(++_sequence, record, log, instanceId);
            Queue<LogEntry> queue;
            int capacity;
            if (log is { } id)
            {
                if (!_instances.TryGetValue(id, out queue!))
                    _instances[id] = queue = new Queue<LogEntry>();
                capacity = _instanceCapacity;
            }
            else
            {
                queue = _manager;
                capacity = _managerCapacity;
            }

            queue.Enqueue(entry);
            if (queue.Count > capacity)
                evicted = queue.Dequeue();
        }

        Changed?.Invoke(new LogFeedChange(entry, evicted));
    }
}

/// <summary>
/// A change of <see cref="LogFeed"/>: a record added, with the one it pushed out of its log, or a reset,
/// after which the kept records are to be read again. <see cref="Added"/> null: a reset.
/// </summary>
internal sealed record LogFeedChange(LogEntry? Added, LogEntry? Evicted)
{
    public static LogFeedChange Reset { get; } = new(null, null);
}
