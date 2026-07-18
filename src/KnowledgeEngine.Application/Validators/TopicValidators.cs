using FluentValidation;
using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Validators;

public class CreateTopicValidator : AbstractValidator<CreateTopicRequest>
{
    public CreateTopicValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MinimumLength(1).WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters");

        RuleFor(x => x.Domain)
            .MaximumLength(255).WithMessage("Domain must not exceed 255 characters");

        RuleFor(x => x.Visibility)
            .Must(v => v == "private" || v == "public")
            .When(x => !string.IsNullOrEmpty(x.Visibility))
            .WithMessage("Visibility must be 'private' or 'public'");
    }
}

public class UpdateTopicValidator : AbstractValidator<UpdateTopicRequest>
{
    public UpdateTopicValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(1).WithMessage("Name must not be empty")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters")
            .When(x => x.Name != null);

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.Domain)
            .MaximumLength(255).WithMessage("Domain must not exceed 255 characters")
            .When(x => x.Domain != null);

        RuleFor(x => x.Visibility)
            .Must(v => v == "private" || v == "public")
            .When(x => !string.IsNullOrEmpty(x.Visibility))
            .WithMessage("Visibility must be 'private' or 'public'");
    }
}
