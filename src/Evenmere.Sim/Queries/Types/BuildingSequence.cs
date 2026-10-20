using Evenmere.Core.Ids;

namespace Evenmere.Sim.Queries.Types;

/// <summary>Which buildings a <see cref="BuildingSequence"/> visits.</summary>
internal enum BuildingFilter : byte
{
    /// <summary>Every building, whatever its state.</summary>
    Any = 0,

    /// <summary>Only finished buildings: <see cref="BuildingState.Active"/> and <see cref="BuildingState.Paused"/>.</summary>
    Complete = 1,
}

/// <summary>
/// The source a <see cref="BuildingSequence"/> reads from, implemented by the building registry. It walks by ID rather
/// than by position, so a caller that removes buildings while it iterates still visits the rest in order.
/// </summary>
internal interface IBuildingSource
{
    /// <summary>Finds the first building whose ID is greater than a given one.</summary>
    /// <param name="after">The ID to start after; <see cref="EntityId.None"/> starts at the beginning.</param>
    /// <param name="filter">Which buildings qualify.</param>
    /// <param name="building">The building found, when there is one.</param>
    /// <returns><see langword="true"/> if a qualifying building has a greater ID.</returns>
    bool TryGetNext(EntityId after, BuildingFilter filter, out BuildingInfo building);
}

/// <summary>
/// An allocation-free sequence of buildings in ascending ID order, for <c>foreach</c>. It is a view over the registry, so it
/// is valid only between ticks; copy what you need to keep. A default value is an empty sequence.
/// </summary>
public readonly struct BuildingSequence
{
    private readonly IBuildingSource? _source;
    private readonly BuildingFilter _filter;

    internal BuildingSequence(IBuildingSource source, BuildingFilter filter)
    {
        _source = source;
        _filter = filter;
    }

    /// <summary>Gets an enumerator over the buildings, in ascending ID order.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(_source, _filter);

    /// <summary>Enumerates a <see cref="BuildingSequence"/> without allocating.</summary>
    public struct Enumerator
    {
        private readonly IBuildingSource? _source;
        private readonly BuildingFilter _filter;
        private BuildingInfo _current;

        internal Enumerator(IBuildingSource? source, BuildingFilter filter)
        {
            _source = source;
            _filter = filter;
            _current = default;
        }

        /// <summary>Gets the building the enumerator is at.</summary>
        public readonly BuildingInfo Current => _current;

        /// <summary>Moves to the next building.</summary>
        /// <returns><see langword="true"/> if there is one.</returns>
        public bool MoveNext()
        {
            if (_source is null || !_source.TryGetNext(_current.Id, _filter, out BuildingInfo next))
            {
                return false;
            }

            _current = next;
            return true;
        }
    }
}
