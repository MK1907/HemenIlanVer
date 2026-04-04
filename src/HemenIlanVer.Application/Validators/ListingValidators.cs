using FluentValidation;
using HemenIlanVer.Contracts.Listings;

namespace HemenIlanVer.Application.Validators;

public sealed class CreateListingRequestValidator : AbstractValidator<CreateListingRequest>
{
    public CreateListingRequestValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(8000);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
