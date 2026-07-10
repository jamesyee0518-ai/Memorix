using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Processing;

public class SourceProcessorFactory
{
    private readonly IEnumerable<ISourceProcessor> _processors;

    public SourceProcessorFactory(IEnumerable<ISourceProcessor> processors)
    {
        _processors = processors;
    }

    public ISourceProcessor? GetProcessor(string sourceType)
    {
        return _processors.FirstOrDefault(p => p.Supports(sourceType));
    }
}
