namespace CartService.Api.IntegrationTests.Observability;

using System.Collections;
using System.Collections.Generic;

/// <summary>
/// A collection that the in-memory exporters of OpenTelemetry can fill from their own threads while a test reads
/// it. Enumerating gives a snapshot.
/// </summary>
internal sealed class LockedCollection<T> : ICollection<T>
{
    private readonly List<T> _items = new List<T>();
    private readonly object _lock = new object();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    public void Add(T item)
    {
        lock (_lock)
        {
            _items.Add(item);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }

    public bool Contains(T item)
    {
        lock (_lock)
        {
            return _items.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_lock)
        {
            _items.CopyTo(array, arrayIndex);
        }
    }

    public bool Remove(T item)
    {
        lock (_lock)
        {
            return _items.Remove(item);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        lock (_lock)
        {
            return new List<T>(_items).GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
