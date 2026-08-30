using FluentValidation;
using MediatR;

namespace Application.Common.Behaviors;

/// <summary>
/// Runs every registered validator for a request before its handler. Aggregates all
/// failures into one <see cref="ValidationException"/> so the caller gets the complete
/// list rather than discovering problems one round trip at a time.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var applicable = validators.ToList();
        if (applicable.Count == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            applicable.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
