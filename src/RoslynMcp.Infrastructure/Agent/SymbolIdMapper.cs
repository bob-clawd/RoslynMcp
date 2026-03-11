using System.Collections.Concurrent;

namespace RoslynMcp.Infrastructure.Agent;

public static class SymbolIdMapper
{
    private static readonly ConcurrentDictionary<string, string> internalToExternal = new();
    private static readonly ConcurrentDictionary<string, string> externalToInternal = new();

    private static int _nextId = 0;

    extension(string id)
    {
        public string ToExternal()
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;

            while (true)
            {
                if (internalToExternal.TryGetValue(id, out var existingId))
                    return existingId;

                var externalId = CreateExternalId();
                if (!internalToExternal.TryAdd(id, externalId))
                    continue;

                externalToInternal[externalId] = id;
                return externalId;
            }
        }

        public string ToInternal()
        {
            if (TryToInternal(id, out var internalId))
                return internalId;

            throw new KeyNotFoundException($"External ID not found in mapping: {id}");
        }

        public bool Update(string newInternalId)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(newInternalId))
                return false;

            if (!internalToExternal.TryGetValue(id, out var externalId))
                return false;

            internalToExternal.TryRemove(id, out _);

            internalToExternal[newInternalId] = externalId;
            externalToInternal[externalId] = newInternalId;

            return true;
        }

        public bool TryToInternal(out string? internalId)
        {
            internalId = null;

            if (string.IsNullOrEmpty(id))
                return false;

            return externalToInternal.TryGetValue(id, out internalId);
        }
    }

    private static string CreateExternalId()
    {
        var newId = Interlocked.Increment(ref _nextId);
        return $"S+{newId:D4}";
    }
}
