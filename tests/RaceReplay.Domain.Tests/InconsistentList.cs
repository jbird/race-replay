using System.Collections;

namespace RaceReplay.Domain.Tests;

/// <summary>
/// A list whose indexer and successive enumerations return different items. Constructors must take one snapshot and
/// validate that same snapshot; this makes a validate-one-read, keep-another-read bug observable.
/// </summary>
internal sealed class InconsistentList<T>(T[] indexed, params T[][] enumerations) : IReadOnlyList<T>
{
    private int _enumerations;

    public T this[int index] => indexed[index];

    public int Count => indexed.Length;

    public IEnumerator<T> GetEnumerator() =>
        ((IEnumerable<T>)enumerations[Math.Min(_enumerations++, enumerations.Length - 1)]).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}