using WinNotch.Features;
using WinNotch.Scratchpad;

namespace WinNotch.Services;

public sealed class ScratchpadService : IFeatureModule, IDisposable
{
    private readonly IScratchpadStore _store;
    private readonly List<ScratchpadDocument> _documents;

    public ScratchpadService(IScratchpadStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _documents = store.Load().ToList();
        if (_documents.Count == 0) _documents.Add(new ScratchpadDocument());
    }

    public FeatureId Id => BuiltInFeatures.Scratchpad.Id;
    public IReadOnlyList<ScratchpadDocument> Documents => _documents;
    public event Action? Changed;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Changed?.Invoke();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Save();
        return ValueTask.CompletedTask;
    }

    public ScratchpadDocument Add(string? title = null)
    {
        var document = new ScratchpadDocument { Title = title ?? "Note" };
        _documents.Insert(0, document);
        SaveAndNotify();
        return document;
    }

    public bool Remove(Guid id)
    {
        int index = _documents.FindIndex(document => document.Id == id);
        if (index < 0) return false;
        _documents.RemoveAt(index);
        if (_documents.Count == 0) _documents.Add(new ScratchpadDocument());
        SaveAndNotify();
        return true;
    }

    public void Update(Guid id, string title, string content)
    {
        var document = _documents.FirstOrDefault(value => value.Id == id);
        if (document == null) return;
        document.Title = title;
        document.Content = content;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        ScratchpadPolicy.Normalize(document);
        SaveAndNotify();
    }

    public void Save() => _store.Save(_documents);

    private void SaveAndNotify()
    {
        Save();
        Changed?.Invoke();
    }

    public void Dispose() => Save();
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
