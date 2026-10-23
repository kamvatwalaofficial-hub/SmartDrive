using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SmartDrive.Domain.Common;

namespace SmartDrive.Api.Infrastructure;

/// <summary>
/// Turns exceptions into RFC 7807 problem responses. Business-rule violations keep their message and a
/// stable <c>code</c>; anything unexpected becomes a generic 500 with a trace id. Stack traces, SQL and
/// internal details are never sent to the client (they are logged on the server instead).
/// </summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problems, ILogger<ProblemExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title, code, detail) = exception switch
        {
            DomainException d => (d.Kind switch
            {
                ErrorKind.NotFound => StatusCodes.Status404NotFound,
                ErrorKind.Conflict => StatusCodes.Status409Conflict,
                ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest,
            }, d.Kind switch
            {
                ErrorKind.NotFound => "Not found",
                ErrorKind.Conflict => "Conflict",
                ErrorKind.Forbidden => "Forbidden",
                _ => "The request could not be completed",
            }, d.Code, d.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict", "concurrency",
                "The record was changed by someone else. Please refresh and try again."),
            BadHttpRequestException b => (b.StatusCode, "Bad request", "request.invalid", "The request was malformed."),
            OperationCanceledException when http.RequestAborted.IsCancellationRequested => (499, "Client closed request", "request.cancelled", ""),
            _ => (StatusCodes.Status500InternalServerError, "Something went wrong", "server.error",
                "An unexpected error occurred. Please try again; if it keeps happening, contact support with the trace id."),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
        }
        else if (exception is DomainException)
        {
            logger.LogInformation("Business rule {Code} returned {Status} for {Path}", code, status, http.Request.Path);
        }

        http.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Type = $"https://httpstatuses.io/{status}" };
        problem.Extensions["code"] = code;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }
}

/// <summary>
/// Runs any registered FluentValidation validator for each action argument and returns a 400
/// validation problem listing every field error before the controller runs.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                var key = string.IsNullOrEmpty(failure.PropertyName) ? "request" : JsonName(failure.PropertyName);
                if (!errors.TryGetValue(key, out var list))
                {
                    errors[key] = list = [];
                }

                list.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count > 0)
        {
            var problem = new ValidationProblemDetails(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Some fields need attention",
            };
            problem.Extensions["code"] = "validation";
            context.Result = new BadRequestObjectResult(problem);
            return;
        }

        await next();
    }

    private static string JsonName(string property) =>
        string.Join('.', property.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
