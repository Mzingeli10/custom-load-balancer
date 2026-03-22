namespace LoadBalancer.Server;

public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, DateTime CachedAt)>> _map = new();
    private readonly LinkedList<(TKey Key, TValue Value, DateTime CachedAt)> _list = new();
    private readonly object _lock = new();
    private readonly TimeSpan _ttl;

    public LruCache(int capacity = 500, TimeSpan? ttl = null)
    {
        _capacity = capacity;
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                if (DateTime.UtcNow - node.Value.CachedAt > _ttl)
                {
                    _list.Remove(node); _map.Remove(key);
                    value = default; return false;
                }
                _list.Remove(node); _list.AddFirst(node); _map[key] = _list.First!;
                value = node.Value.Value; return true;
            }
            value = default; return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing)) { _list.Remove(existing); _map.Remove(key); }
            if (_map.Count >= _capacity) { var lru = _list.Last!; _list.RemoveLast(); _map.Remove(lru.Value.Key); }
            var node = _list.AddFirst((key, value, DateTime.UtcNow));
            _map[key] = node;
        }
    }

    public int Count { get { lock (_lock) return _map.Count; } }
}
