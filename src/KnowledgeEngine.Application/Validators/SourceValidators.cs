using FluentValidation;
using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Validators;

public class ImportUrlValidator : AbstractValidator<ImportUrlRequest>
{
    public ImportUrlValidator()
    {
        RuleFor(x => x.TopicId)
            .NotEmpty().WithMessage("TopicId is required");

        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("Url is required")
            .Must(BeValidHttpUrl).WithMessage("Url must be a valid http or https URL")
            .MaximumLength(2048).WithMessage("Url must not exceed 2048 characters");

        RuleFor(x => x.Title)
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters")
            .When(x => x.Title != null);
    }

    private static bool BeValidHttpUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

public class ImportTextValidator : AbstractValidator<ImportTextRequest>
{
    public ImportTextValidator()
    {
        RuleFor(x => x.TopicId)
            .NotEmpty().WithMessage("TopicId is required");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MinimumLength(1).WithMessage("Title is required")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters");

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required")
            .MinimumLength(50).WithMessage("Content should be at least 50 characters")
            .MaximumLength(200000).WithMessage("Content must not exceed 200000 characters");
    }
}
