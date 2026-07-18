namespace KnowledgeEngine.Application.Interfaces;

public interface ISummaryPromptManager
{
    string GetSystemPrompt();
    string GetUserPrompt(string title, string contentText, string sourceType);
    string GetPromptVersion();
}
