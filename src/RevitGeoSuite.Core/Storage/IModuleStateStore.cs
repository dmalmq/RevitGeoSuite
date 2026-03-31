namespace RevitGeoSuite.Core.Storage;

public interface IModuleStateStore
{
    void Save<TState>(IDocumentHandle document, string moduleId, TState state);

    TState? Load<TState>(IDocumentHandle document, string moduleId) where TState : class;

    bool HasState(IDocumentHandle document, string moduleId);
}
